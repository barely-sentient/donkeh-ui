using System.Buffers;
using System.Runtime.InteropServices;
using Ignite.Data;
using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.DataEngine
{
    /// <summary>
    /// A zero-copy parser over the token list. The tokenizer has already removed
    /// whitespace and comments, so source positions are used only to recover
    /// value text and descendant-combinator gaps.
    /// </summary>
    internal ref struct CssStreamParser
    {
        private static readonly int AmpersandId = InternTable.Intern("&");

        private readonly ReadOnlySpan<Token> _tokens;
        private readonly DataTable _table;
        private readonly ConditionCompiler _compiler;
        private readonly StreamContext _context;

        // Cascade layer tracking.
        private readonly List<int> _layerStack = new();

        // Keyframes tracking.
        private bool _inKeyframes;
        private int _keyframesNameId = -1;

        // Descriptor at-rule tracking (font-face, page, property, counter-style, ...).
        private string? _descriptorAtRule;
        private int _descriptorContextId = -1;

        // Margin box tracking (inside @page).
        private bool _inMarginBox;
        private int _marginBoxId = -1;

        // Scope tracking.
        // (removed)

        // Reused for every composed (nested) selector.
        private readonly List<Token> _composer;
        private readonly List<int> _selectorScratch;

        // Reused for interned property and value text.
        private char[] _textBuffer;
        private int _position;

        public CssStreamParser(
            ReadOnlySpan<Token> tokens,
            DataTable table,
            ConditionCompiler compiler)
        {
            _tokens = tokens;
            _table = table;
            _compiler = compiler;
            _context = new StreamContext();
            _composer = new List<Token>(16);
            _selectorScratch = new List<int>(8);
            _textBuffer = ArrayPool<char>.Shared.Rent(256);
            _position = 0;
        }

        public void Dispose()
        {
            if (_textBuffer.Length != 0)
            {
                ArrayPool<char>.Shared.Return(_textBuffer);
                _textBuffer = [];
            }
        }

        public void Parse()
        {
            ParseBlock(topLevel: true);
        }

        // ------------------------------------------------------------------
        // Blocks and statements
        // ------------------------------------------------------------------

        private void ParseBlock(bool topLevel = false)
        {
            while (_position < _tokens.Length)
            {
                var kind = _tokens[_position].Kind;

                if (kind == TokenKind.RightCurlyBrace)
                {
                    _position++;
                    if (!topLevel)
                    {
                        return;
                    }

                    continue;
                }

                if (kind == TokenKind.SemiColon)
                {
                    _position++;
                    continue;
                }

                if (kind == TokenKind.AtKeyword)
                {
                    ParseAtRule();
                    continue;
                }

                ParseSegment();
            }
        }

        private void ParseAtRule()
        {
            var start = _position;
            var atKeyword = InternTable.GetString(_tokens[start].InternedId);
            var end = FindPreludeEnd(start + 1, out var terminator);

            // Semicolon-terminated at-rules: @import, @charset, @namespace, @layer name;
            if (terminator == ';')
            {
                RecordSemicolonAtRule(atKeyword, start, end);
                _position = end + 1;
                return;
            }

            if (terminator != '{')
            {
                // EOF or the enclosing block closed before the prelude did.
                _position = end;
                return;
            }

            _position = end + 1;

            if (IsNest(atKeyword))
            {
                // @nest <selector> { ... } behaves like a nested style rule.
                ParseRule(start + 1, end);
                return;
            }

            if (IsCondition(atKeyword))
            {
                var conditionId = _table.GetOrAddCondition(
                    _tokens.Slice(start, end - start),
                    _context.ConditionId,
                    _compiler);

                _context.PushCondition(conditionId);
                ParseBlock();
                _context.Pop();
                return;
            }

            if (IsKeyframes(atKeyword))
            {
                RecordKeyframes(atKeyword, start, end);
                return;
            }

            if (IsScope(atKeyword))
            {
                RecordScope(start, end);
                _context.PushContainer();
                ParseBlock();
                _context.Pop();
                return;
            }

            if (IsLayer(atKeyword))
            {
                // @layer name { } or @layer a, b { }.
                var layerId = RecordLayer(start, end);
                if (layerId >= 0)
                {
                    _layerStack.Add(layerId);
                }

                _context.PushContainer();
                ParseBlock();
                _context.Pop();

                if (layerId >= 0 && _layerStack.Count > 0)
                {
                    _layerStack.RemoveAt(_layerStack.Count - 1);
                }

                return;
            }

            if (IsMarginBox(atKeyword) && _descriptorAtRule is not null)
            {
                _inMarginBox = true;
                _marginBoxId = InternTable.Intern(atKeyword!);

                _context.PushContainer();
                ParseBlock();
                _context.Pop();

                _inMarginBox = false;
                _marginBoxId = -1;
                return;
            }
            if (IsDescriptorAtRule(atKeyword))
            {
                _descriptorAtRule = atKeyword;
                _descriptorContextId = RecordDescriptorContext(atKeyword, start, end);

                // @page may contain margin boxes (@top-center { }).
                _context.PushContainer();
                ParseBlock();
                _context.Pop();

                _descriptorAtRule = null;
                _descriptorContextId = -1;
                return;
            }

            // Unknown at-rules and containers (font-face handled above, keyframes
            // handled above). Their contents are still traversed, but they do not
            // inherit the enclosing style selector.
            _context.PushContainer();
            ParseBlock();
            _context.Pop();
        }

        private void ParseSegment()
        {
            var start = _position;

            if (IsCustomPropertyDeclaration(start))
            {
                ParseDeclaration(start, customProperty: true);
                return;
            }

            var end = FindHeaderEnd(start, out var terminator);

            if (terminator == '{')
            {
                _position = end + 1;
                ParseRule(start, end);
                return;
            }

            ParseDeclaration(start, customProperty: false);
        }

        // ------------------------------------------------------------------
        // Style rules and selector flattening
        // ------------------------------------------------------------------

        private void ParseRule(int start, int end)
        {
            _selectorScratch.Clear();

            var parents = _context.CurrentSelectors;
            var childStart = start;
            var parenthesisDepth = 0;
            var bracketDepth = 0;

            for (var i = start; i < end; i++)
            {
                var kind = _tokens[i].Kind;

                switch (kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.Comma when parenthesisDepth == 0 && bracketDepth == 0:
                        FlattenChild(_tokens.Slice(childStart, i - childStart), parents);
                        childStart = i + 1;
                        break;
                }
            }

            FlattenChild(_tokens.Slice(childStart, end - childStart), parents);

            if (_selectorScratch.Count != 0)
            {
                _context.PushRule(CollectionsMarshal.AsSpan(_selectorScratch));
                ParseBlock();
                _context.Pop();
            }
            else
            {
                // Malformed or empty selector prelude: consume the block so the
                // stream stays in sync. The container frame hides the enclosing
                // selector, otherwise declarations would leak onto the parent.
                _context.PushContainer();
                ParseBlock();
                _context.Pop();
            }
        }

        private void FlattenChild(ReadOnlySpan<Token> child, ReadOnlySpan<int> parents)
        {
            if (child.IsEmpty)
            {
                return;
            }

            if (parents.IsEmpty)
            {
                AddSelectorId(_table.InternSelector(child));
                return;
            }

            for (var i = 0; i < parents.Length; i++)
            {
                var parent = _table.GetSelectorSpan(parents[i]);
                Compose(parent, child);
                AddSelectorId(_table.InternSelector(CollectionsMarshal.AsSpan(_composer)));
            }
        }

        private void AddSelectorId(int id)
        {
            var scratch = _selectorScratch;

            // Selector lists are short; a linear scan avoids a hash set.
            for (var i = 0; i < scratch.Count; i++)
            {
                if (scratch[i] == id)
                {
                    return;
                }
            }

            scratch.Add(id);
        }

        /// <summary>
        /// Builds a flattened selector, preserving the CSS nesting rules:
        /// a child without '&amp;' becomes a descendant of the parent, while an
        /// '&amp;' is replaced in place. Token positions are re-laid out so that a
        /// gap still means an implied descendant combinator and adjacency still
        /// means the same compound selector.
        /// </summary>
        private void Compose(ReadOnlySpan<Token> parent, ReadOnlySpan<Token> child)
        {
            _composer.Clear();
            var cursor = 0;

            var hasAmpersand = false;
            for (var i = 0; i < child.Length; i++)
            {
                if (IsAmpersand(child[i]))
                {
                    hasAmpersand = true;
                    break;
                }
            }

            if (!hasAmpersand)
            {
                AppendSlice(parent, gapBeforeFirst: false, ref cursor);
                AppendSlice(child, gapBeforeFirst: true, ref cursor);
                return;
            }

            var segmentStart = 0;

            for (var i = 0; i < child.Length; i++)
            {
                if (!IsAmpersand(child[i]))
                {
                    continue;
                }

                var gapBeforeSegment = segmentStart > 0
                    && child[segmentStart].StartIndex > child[segmentStart - 1].EndIndex;

                AppendSlice(child.Slice(segmentStart, i - segmentStart), gapBeforeSegment, ref cursor);

                var gapBeforeParent = i > 0 && child[i].StartIndex > child[i - 1].EndIndex;
                AppendSlice(parent, gapBeforeParent, ref cursor);

                segmentStart = i + 1;
            }

            if (segmentStart < child.Length)
            {
                var gapBeforeChild = child[segmentStart].StartIndex > child[segmentStart - 1].EndIndex;
                AppendSlice(child.Slice(segmentStart), gapBeforeChild, ref cursor);
            }
        }

        private void AppendSlice(ReadOnlySpan<Token> tokens, bool gapBeforeFirst, ref int cursor)
        {
            for (var i = 0; i < tokens.Length; i++)
            {
                var gap = i > 0
                    ? tokens[i].StartIndex > tokens[i - 1].EndIndex
                    : gapBeforeFirst && cursor > 0;

                var token = tokens[i];
                if (gap)
                {
                    cursor++;
                }

                token.StartIndex = cursor;
                cursor = token.EndIndex;
                _composer.Add(token);
            }
        }

        private static bool IsAmpersand(in Token token)
        {
            return token.Kind == TokenKind.Delimiter && token.InternedId == AmpersandId;
        }

        // ------------------------------------------------------------------
        // Declarations
        // ------------------------------------------------------------------

        private void ParseDeclaration(int start, bool customProperty)
        {
            var colon = FindColon(start);

            if (colon < 0)
            {
                SkipDeclaration(start);
                return;
            }

            var valueStart = colon + 1;
            var valueEnd = FindValueEnd(valueStart, out var terminator);

            var selectors = _context.CurrentSelectors;

            // Descriptor at-rules (@font-face, @page, @property, ...) have no
            // style selector. Record the descriptor and skip rule creation.
            if (_descriptorAtRule is not null)
            {
                if (valueEnd > valueStart)
                {
                    var propertyLength = NormalizeTokens(start, colon, lowerCase: !customProperty);
                    var propertyId = InternTable.Intern(_textBuffer.AsSpan(0, propertyLength));

                    var valueLength = NormalizeTokens(valueStart, valueEnd, lowerCase: false);
                    var valueId = InternTable.Intern(_textBuffer.AsSpan(0, valueLength));

                    var contextId = _descriptorContextId;
                    // Margin boxes: if inside a margin box, context is the box name.
                    if (_inMarginBox)
                    {
                        _table.AddMarginBox(_marginBoxId, propertyId, valueId);
                    }
                    else
                    {
                        _table.AddDescriptor(
                            InternTable.Intern(_descriptorAtRule),
                            contextId,
                            propertyId,
                            valueId);
                    }
                }

                _position = terminator.Kind == TokenKind.SemiColon
                    ? terminator.Index + 1
                    : terminator.Index;

                return;
            }

            // Keyframes: record each declaration as a keyframe.
            if (_inKeyframes)
            {
                if (valueEnd > valueStart && !selectors.IsEmpty)
                {
                    var propertyLength = NormalizeTokens(start, colon, lowerCase: !customProperty);
                    var propertyId = InternTable.Intern(_textBuffer.AsSpan(0, propertyLength));

                    var valueLength = NormalizeTokens(valueStart, valueEnd, lowerCase: false);
                    var valueId = InternTable.Intern(_textBuffer.AsSpan(0, valueLength));

                    var conditionId = _context.ConditionId;
                    foreach (var selectorId in selectors)
                    {
                        _table.AddKeyframe(
                            _keyframesNameId,
                            selectorId,
                            propertyId,
                            valueId,
                            conditionId);
                    }
                }

                _position = terminator.Kind == TokenKind.SemiColon
                    ? terminator.Index + 1
                    : terminator.Index;

                return;
            }

            // Descriptor blocks (@font-face, @page, ...) have no style selector.
            // Do not pay to intern text that can never be matched.
            if (selectors.IsEmpty || valueEnd <= valueStart)
            {
                _position = terminator.Kind == TokenKind.SemiColon
                    ? terminator.Index + 1
                    : terminator.Index;

                return;
            }

            var propertyLength2 = NormalizeTokens(start, colon, lowerCase: !customProperty);
            var propertyId2 = InternTable.Intern(_textBuffer.AsSpan(0, propertyLength2));

            var valueLength2 = NormalizeTokens(valueStart, valueEnd, lowerCase: false);
            var valueId2 = InternTable.Intern(_textBuffer.AsSpan(0, valueLength2));

            var conditionId2 = _context.ConditionId;
            var layerId = CurrentLayerId;

            for (var i = 0; i < selectors.Length; i++)
            {
                _table.AddRule(selectors[i], propertyId2, valueId2, conditionId2, layerId);
            }

            _position = terminator.Kind == TokenKind.SemiColon
                ? terminator.Index + 1
                : terminator.Index;

            return;
        }

        private int NormalizeTokens(int start, int endExclusive, bool lowerCase)
        {
            var length = 0;
            var previousEnd = -1;

            for (var i = start; i < endExclusive; i++)
            {
                var token = _tokens[i];

                if (previousEnd >= 0 && token.StartIndex > previousEnd)
                {
                    AppendChar(' ', ref length);
                }

                var text = InternTable.GetString(token.InternedId);
                if (text is not null)
                {
                    AppendText(text, lowerCase, ref length);
                }

                previousEnd = token.EndIndex;
            }

            return length;
        }

        private void AppendText(string text, bool lowerCase, ref int length)
        {
            EnsureTextCapacity(length + text.Length);

            var buffer = _textBuffer;
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (lowerCase && c >= 'A' && c <= 'Z')
                {
                    c = (char)(c + 32);
                }

                buffer[length++] = c;
            }
        }

        private void AppendChar(char c, ref int length)
        {
            EnsureTextCapacity(length + 1);
            _textBuffer[length++] = c;
        }

        private void EnsureTextCapacity(int required)
        {
            if (required <= _textBuffer.Length)
            {
                return;
            }

            var bigger = ArrayPool<char>.Shared.Rent(Math.Max(required, _textBuffer.Length * 2));
            Array.Copy(_textBuffer, bigger, _textBuffer.Length);
            ArrayPool<char>.Shared.Return(_textBuffer);
            _textBuffer = bigger;
        }

        private bool IsCustomPropertyDeclaration(int index)
        {
            if (index + 1 >= _tokens.Length || _tokens[index + 1].Kind != TokenKind.Colon)
            {
                return false;
            }

            var kind = _tokens[index].Kind;
            if (kind != TokenKind.Identifier && kind != TokenKind.PropertyName)
            {
                return false;
            }

            var text = InternTable.GetString(_tokens[index].InternedId);
            return text is not null
                && text.Length >= 2
                && text[0] == '-'
                && text[1] == '-';
        }

        private void SkipDeclaration(int start)
        {
            var parenthesisDepth = 0;
            var bracketDepth = 0;

            for (var i = start; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.LeftCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        _position = i;
                        return;

                    case TokenKind.SemiColon when parenthesisDepth == 0 && bracketDepth == 0:
                        _position = i + 1;
                        return;

                    case TokenKind.RightCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        _position = i;
                        return;
                }
            }

            _position = _tokens.Length;
        }

        // ------------------------------------------------------------------
        // Token-stream scanning
        // ------------------------------------------------------------------

        private int FindPreludeEnd(int start, out char terminator)
        {
            var parenthesisDepth = 0;
            var bracketDepth = 0;

            for (var i = start; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.LeftCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = '{';
                        return i;

                    case TokenKind.SemiColon when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = ';';
                        return i;

                    case TokenKind.RightCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = '}';
                        return i;
                }
            }

            terminator = '\0';
            return _tokens.Length;
        }

        private int FindHeaderEnd(int start, out char terminator)
        {
            var parenthesisDepth = 0;
            var bracketDepth = 0;

            for (var i = start; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.LeftCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = '{';
                        return i;

                    case TokenKind.SemiColon when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = ';';
                        return i;

                    case TokenKind.RightCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        terminator = '}';
                        return i;
                }
            }

            terminator = '\0';
            return _tokens.Length;
        }

        private int FindColon(int start)
        {
            var parenthesisDepth = 0;
            var bracketDepth = 0;

            for (var i = start; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.Colon when parenthesisDepth == 0 && bracketDepth == 0:
                        return i;

                    case TokenKind.LeftCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                    case TokenKind.SemiColon when parenthesisDepth == 0 && bracketDepth == 0:
                    case TokenKind.RightCurlyBrace when parenthesisDepth == 0 && bracketDepth == 0:
                        return -1;
                }
            }

            return -1;
        }

        private int FindValueEnd(int start, out DeclarationTerminator terminator)
        {
            var parenthesisDepth = 0;
            var bracketDepth = 0;
            var braceDepth = 0;

            for (var i = start; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case TokenKind.LeftParenthesis:
                        parenthesisDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenthesisDepth > 0)
                        {
                            parenthesisDepth--;
                        }
                        break;

                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        break;

                    case TokenKind.LeftCurlyBrace:
                        braceDepth++;
                        break;

                    case TokenKind.RightCurlyBrace:
                        if (braceDepth > 0)
                        {
                            braceDepth--;
                            break;
                        }

                        terminator = new DeclarationTerminator(TokenKind.RightCurlyBrace, i);
                        return i;

                    case TokenKind.SemiColon when parenthesisDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                        terminator = new DeclarationTerminator(TokenKind.SemiColon, i);
                        return i;
                }
            }

            terminator = new DeclarationTerminator(TokenKind.Delimiter, _tokens.Length);
            return _tokens.Length;
        }

        private static bool IsCondition(string? atKeyword)
        {
            return atKeyword is not null
                && (atKeyword.Equals("@media", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@supports", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@container", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@document", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@-moz-document", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@starting-style", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsNest(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@nest", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsKeyframes(string? atKeyword)
        {
            return atKeyword is not null
                && (atKeyword.Equals("@keyframes", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@-webkit-keyframes", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsScope(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@scope", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLayer(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@layer", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDescriptorAtRule(string? atKeyword)
        {
            if (atKeyword is null)
            {
                return false;
            }

            return atKeyword.Equals("@font-face", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@page", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@property", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@counter-style", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@font-palette-values", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@view-transition", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsMarginBox(string? atKeyword)
        {
            if (atKeyword is null || !atKeyword.StartsWith("@", StringComparison.Ordinal))
            {
                return false;
            }

            return atKeyword.Equals("@top-left-corner", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@top-left", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@top-center", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@top-right", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@top-right-corner", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@bottom-left-corner", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@bottom-left", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@bottom-center", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@bottom-right", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@bottom-right-corner", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@left-top", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@left-middle", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@left-bottom", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@right-top", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@right-middle", StringComparison.OrdinalIgnoreCase)
                || atKeyword.Equals("@right-bottom", StringComparison.OrdinalIgnoreCase);
        }

        // ------------------------------------------------------------------
        // Recording helpers
        // ------------------------------------------------------------------

        private int InternPreludeText(int start, int end)
        {
            // Size the buffer from the actual token text so long single-token
            // preludes (for example @keyframes progress-bar-stripes) are not
            // truncated by a fixed capacity estimate.
            var capacity = 0;
            var previousEnd = -1;

            for (var i = start; i < end; i++)
            {
                var token = _tokens[i];

                if (previousEnd >= 0 && token.StartIndex > previousEnd)
                {
                    capacity++;
                }

                capacity += InternTable.GetString(token.InternedId)?.Length ?? 0;
                previousEnd = token.EndIndex;
            }

            var length = 0;
            var buffer = new char[capacity];
            previousEnd = -1;

            for (var i = start; i < end; i++)
            {
                var token = _tokens[i];
                if (previousEnd >= 0 && token.StartIndex > previousEnd)
                {
                    buffer[length++] = ' ';
                }

                var text = InternTable.GetString(token.InternedId);
                if (text is not null)
                {
                    text.CopyTo(0, buffer, length, text.Length);
                    length += text.Length;
                }

                previousEnd = token.EndIndex;
            }

            return InternTable.Intern(buffer.AsSpan(0, length));
        }

        private int ExtractUrlId(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (_tokens[i].Kind == TokenKind.String)
                {
                    return _tokens[i].InternedId;
                }

                if (_tokens[i].Kind == TokenKind.Function)
                {
                    // url(...) — find string inside parens.
                    // Fall through to text extraction.
                }
            }

            // Fall back to the whole prelude text.
            return InternPreludeText(start, end);
        }

        private void RecordSemicolonAtRule(string? atKeyword, int start, int end)
        {
            if (atKeyword is null)
            {
                return;
            }

            if (atKeyword.Equals("@import", StringComparison.OrdinalIgnoreCase))
            {
                var urlId = ExtractUrlId(start + 1, end);
                _table.AddImport(urlId, CurrentLayerId, _context.ConditionId, -1);
                return;
            }

            if (atKeyword.Equals("@charset", StringComparison.OrdinalIgnoreCase))
            {
                var charsetId = InternPreludeText(start + 1, end);
                _table.AddCharset(charsetId);
                return;
            }

            if (atKeyword.Equals("@namespace", StringComparison.OrdinalIgnoreCase))
            {
                // Optional prefix followed by a url.
                int prefixId = -1;
                int uriId = -1;

                // Find url/string token.
                for (var i = start + 1; i < end; i++)
                {
                    if (_tokens[i].Kind == TokenKind.String)
                    {
                        uriId = _tokens[i].InternedId;
                        // Prefix is the token before it, if an identifier.
                        if (i > start + 1 && _tokens[i - 1].Kind is TokenKind.Identifier or TokenKind.PropertyName)
                        {
                            prefixId = _tokens[i - 1].InternedId;
                        }
                        break;
                    }
                }

                if (uriId < 0)
                {
                    uriId = InternPreludeText(start + 1, end);
                }

                _table.AddNamespace(prefixId, uriId);
                return;
            }

            if (atKeyword.Equals("@layer", StringComparison.OrdinalIgnoreCase))
            {
                // @layer a, b; (statement) — record each layer name.
                RecordLayerNames(start + 1, end);
                return;
            }
        }

        private void RecordLayerNames(int start, int end)
        {
            var nameStart = start;
            for (var i = start; i <= end; i++)
            {
                var isComma = i < end && _tokens[i].Kind == TokenKind.Comma;
                var isEnd = i == end;

                if (isComma || isEnd)
                {
                    if (i > nameStart)
                    {
                        var nameId = InternPreludeText(nameStart, i);
                        _table.GetOrAddLayer(nameId);
                    }
                    nameStart = i + 1;
                }
            }
        }

        private int RecordLayer(int start, int end)
        {
            // Single name (or first name) for block push.
            var nameId = InternPreludeText(start + 1, end);
            // Handle comma: record all, return first.
            RecordLayerNames(start + 1, end);
            return _table.GetOrAddLayer(nameId);
        }

        private void RecordKeyframes(string? atKeyword, int start, int end)
        {
            _inKeyframes = true;
            var nameId = InternPreludeText(start + 1, end);
            _keyframesNameId = nameId;

            _context.PushContainer();
            ParseBlock();
            _context.Pop();

            _inKeyframes = false;
            _keyframesNameId = -1;
        }

        private void RecordScope(int start, int end)
        {
            var preludeId = InternPreludeText(start + 1, end);
            _table.AddScope(preludeId);
        }

        private int RecordDescriptorContext(string? atKeyword, int start, int end)
        {
            var contextId = InternPreludeText(start + 1, end);
            return contextId;
        }

        private int CurrentLayerId => _layerStack.Count > 0 ? _layerStack[^1] : -1;

        private readonly record struct DeclarationTerminator(TokenKind Kind, int Index);
    }
}
