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

            if (terminator == ';')
            {
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

            // @layer, @keyframes, @font-face, @page, unknown at-rules, etc. Their
            // contents are still traversed, but they do not inherit the enclosing
            // style selector.
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

            // Descriptor blocks (@font-face, @page, ...) have no style selector.
            // Do not pay to intern text that can never be matched.
            if (selectors.IsEmpty || valueEnd <= valueStart)
            {
                _position = terminator.Kind == TokenKind.SemiColon
                    ? terminator.Index + 1
                    : terminator.Index;

                return;
            }

            var propertyLength = NormalizeTokens(start, colon, lowerCase: !customProperty);
            var propertyId = InternTable.Intern(_textBuffer.AsSpan(0, propertyLength));

            var valueLength = NormalizeTokens(valueStart, valueEnd, lowerCase: false);
            var valueId = InternTable.Intern(_textBuffer.AsSpan(0, valueLength));

            var conditionId = _context.ConditionId;

            for (var i = 0; i < selectors.Length; i++)
            {
                _table.AddRule(selectors[i], propertyId, valueId, conditionId);
            }

            _position = terminator.Kind == TokenKind.SemiColon
                ? terminator.Index + 1
                : terminator.Index;
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

        private readonly record struct DeclarationTerminator(TokenKind Kind, int Index);
    }
}
