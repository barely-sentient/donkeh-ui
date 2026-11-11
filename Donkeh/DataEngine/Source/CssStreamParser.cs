using System.Buffers;
using System.Runtime.InteropServices;
using Donkeh.Data;
using Donkeh.Parser;
using Donkeh.Parser.Objects;

namespace Donkeh.DataEngine
{
    /// <summary>
    /// A zero-copy parser over the token list. The tokenizer has already removed
    /// whitespace and comments, so source positions are used only to recover
    /// value text and descendant-combinator gaps.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The parser is a forward-only state machine over the token span; it
    /// never backtracks and never tokenizes text a second time. It recognises
    /// style rules (including nesting), declarations, custom properties, and the
    /// following at-rules: <c>@media</c>, <c>@supports</c>, <c>@container</c>,
    /// <c>@document</c>, <c>@-moz-document</c>, <c>@starting-style</c>,
    /// <c>@nest</c>, <c>@keyframes</c>, <c>@-webkit-keyframes</c>,
    /// <c>@scope</c>, <c>@layer</c>, <c>@import</c>, <c>@charset</c>,
    /// <c>@namespace</c>, <c>@font-face</c>, <c>@page</c>, <c>@property</c>,
    /// <c>@counter-style</c>, <c>@font-palette-values</c>,
    /// <c>@view-transition</c>, and the <c>@page</c> margin boxes such as
    /// <c>@top-center</c>.
    /// </para>
    /// <para>
    /// Recovery is local: a malformed declaration is skipped up to the next
    /// top-level <c>;</c>, <c>{</c> or <c>}</c>; a malformed selector prelude
    /// still consumes its block but hides the enclosing selector so declarations
    /// cannot leak onto the parent; an at-rule whose prelude ends at EOF or at a
    /// closing brace is abandoned without consuming the brace. No parse error is
    /// reported to the caller.
    /// </para>
    /// <para>
    /// The struct borrows the token span and the destination
    /// <see cref="DataTable"/>; both must outlive it. It owns a pooled character
    /// buffer and must therefore be disposed. It is a ref struct, so instances
    /// are stack-only and not usable across <see langword="await"/> or as fields
    /// of heap objects.
    /// </para>
    /// </remarks>
    internal ref struct CssStreamParser
    {
        /// <summary>
        /// Interned id of the <c>&amp;</c> delimiter, used to recognise the CSS
        /// nesting selector without string comparisons.
        /// </summary>
        private static readonly int AmpersandId = InternTable.Intern("&");

        /// <summary>
        /// The token stream being parsed. Borrowed, not copied; tokens keep their
        /// original source offsets for gap detection.
        /// </summary>
        private readonly ReadOnlySpan<Token> _tokens;
        /// <summary>
        /// Destination for interned selectors, conditions and rules.
        /// </summary>
        private readonly DataTable _table;
        /// <summary>
        /// Compiler used once per distinct condition prelude.
        /// </summary>
        private readonly ConditionCompiler _compiler;
        /// <summary>
        /// Selector and condition nesting state for the constructs currently
        /// being parsed.
        /// </summary>
        private readonly StreamContext _context;

        // Cascade layer tracking.
        /// <summary>
        /// Ids of the cascade layers currently entered, outermost first and
        /// innermost last. Rules record only <see cref="CurrentLayerId"/>.
        /// </summary>
        private readonly List<int> _layerStack = new();

        // Keyframes tracking.
        /// <summary>
        /// <see langword="true"/> while declarations inside an <c>@keyframes</c>
        /// block are being recorded as <see cref="CssKeyframe"/> entries rather
        /// than style rules.
        /// </summary>
        private bool _inKeyframes;
        /// <summary>
        /// Interned name of the keyframes block currently being recorded, or -1
        /// outside a keyframes block.
        /// </summary>
        private int _keyframesNameId = -1;

        // Descriptor at-rule tracking (font-face, page, property, counter-style, ...).
        /// <summary>
        /// Name of the descriptor at-rule currently open (<c>@font-face</c>,
        /// <c>@page</c>, <c>@property</c>, ...), or <see langword="null"/> when
        /// the parser is not inside one.
        /// </summary>
        private string? _descriptorAtRule;
        /// <summary>
        /// Interned prelude of the current descriptor at-rule, for example the
        /// page selector of <c>@page :first</c>. Empty preludes intern an empty
        /// string rather than -1.
        /// </summary>
        private int _descriptorContextId = -1;

        // Margin box tracking (inside @page).
        /// <summary>
        /// <see langword="true"/> while parsing declarations inside a margin box
        /// such as <c>@top-center</c> nested in <c>@page</c>.
        /// </summary>
        private bool _inMarginBox;
        /// <summary>
        /// Interned margin-box name currently open, or -1 when not in a margin
        /// box.
        /// </summary>
        private int _marginBoxId = -1;

        // Scope tracking.
        // (removed)

        // Reused for every composed (nested) selector.
        /// <summary>
        /// Scratch buffer holding the flattened token sequence of the selector
        /// currently being composed. Reused for every parent/child combination;
        /// the tokens are copied into the table before the next composition.
        /// </summary>
        private readonly List<Token> _composer;
        /// <summary>
        /// Selector ids produced by flattening the current rule's selector list.
        /// Cleared per rule and de-duplicated with a linear scan.
        /// </summary>
        private readonly List<int> _selectorScratch;

        // Reused for interned property and value text.
        /// <summary>
        /// Pooled character buffer used to normalise property names and values
        /// into one contiguous span before interning. Grows geometrically and is
        /// returned to <c>ArrayPool&lt;char&gt;.Shared</c> by <see cref="Dispose"/>.
        /// </summary>
        private char[] _textBuffer;
        /// <summary>
        /// Index of the next token to parse.
        /// </summary>
        private int _position;

        /// <summary>
        /// Initialises a parser over <paramref name="tokens"/> that appends to
        /// <paramref name="table"/>.
        /// </summary>
        /// <param name="tokens">
        /// The token stream to parse. It is borrowed and must remain valid until
        /// the parser is disposed.
        /// </param>
        /// <param name="table">
        /// The destination table. It is mutated in place and must outlive the
        /// parser.
        /// </param>
        /// <param name="compiler">
        /// The compiler invoked once per distinct condition prelude.
        /// </param>
        /// <remarks>
        /// The constructor rents a 256-character buffer from the shared array
        /// pool, so callers must call <see cref="Dispose"/> even when parsing
        /// throws.
        /// </remarks>
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

        /// <summary>
        /// Returns the pooled text buffer to the shared array pool. Calling this
        /// more than once is harmless because the field is cleared afterwards.
        /// </summary>
        /// <remarks>
        /// Disposing does not flush or validate anything: all table mutations are
        /// performed eagerly while parsing.
        /// </remarks>
        public void Dispose()
        {
            if (_textBuffer.Length != 0)
            {
                ArrayPool<char>.Shared.Return(_textBuffer);
                _textBuffer = [];
            }
        }

        /// <summary>
        /// Parses the entire token span as a top-level stylesheet and appends
        /// every recognised rule and at-rule to the destination table.
        /// </summary>
        /// <remarks>
        /// The method is single-use in practice: it always starts at token zero
        /// and runs until the end of the span. Stray closing braces at top level
        /// are consumed and ignored so a truncated stylesheet still yields the
        /// rules that precede the damage.
        /// </remarks>
        public void Parse()
        {
            ParseBlock(topLevel: true);
        }

        // ------------------------------------------------------------------
        // Blocks and statements
        // ------------------------------------------------------------------

        /// <summary>
        /// Parses statements until the matching closing brace or the end of the
        /// token stream.
        /// </summary>
        /// <param name="topLevel">
        /// <see langword="true"/> for the stylesheet root, where a closing brace is
        /// unexpected and skipped; <see langword="false"/> inside a block, where a
        /// closing brace terminates the block and is consumed.
        /// </param>
        /// <remarks>
        /// Semicolons are consumed without producing anything, which is how stray
        /// declarations and empty statements recover. Every other token is handed
        /// to <see cref="ParseAtRule"/> or <see cref="ParseSegment"/>.
        /// </remarks>
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

        /// <summary>
        /// Parses the at-rule whose at-keyword is at <c>_position</c>,
        /// including a following block when the prelude ends with <c>{</c>.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The prelude is scanned first to find its terminator. A <c>;</c>
        /// terminates statement at-rules (<c>@import</c>, <c>@charset</c>,
        /// <c>@namespace</c>, <c>@layer a, b;</c>); <c>{</c> starts a block; EOF or
        /// a closing brace means the at-rule was truncated and is abandoned
        /// without consuming the brace.
        /// </para>
        /// <para>
        /// Block at-rules are dispatched in this order: <c>@nest</c> (parsed as a
        /// nested style rule), conditions (<c>@media</c>, <c>@supports</c>,
        /// <c>@container</c>, <c>@document</c>, <c>@-moz-document</c>,
        /// <c>@starting-style</c>), keyframes, <c>@scope</c>, <c>@layer</c>,
        /// <c>@page</c> margin boxes, descriptor at-rules, then an unknown
        /// container. Conditions keep the enclosing selector so style rules inside
        /// inherit it; every other container hides it so declarations cannot leak
        /// onto the parent selector.
        /// </para>
        /// <para>
        /// The condition prelude passed to <see cref="ConditionCompiler"/> runs
        /// from the at-keyword up to, but excluding, the opening brace. Nested
        /// conditions form an AND chain through
        /// <see cref="DataTable.GetOrAddCondition"/>: the enclosing condition is the
        /// parent, and the executor is shared by preludes with identical tokens even
        /// when their parents differ. A compiler failure is caught by the table and
        /// stored as an always-false executor.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Parses one statement that is not an at-rule, distinguishing a style
        /// rule from a declaration by looking for a top-level opening brace.
        /// </summary>
        /// <remarks>
        /// Custom properties are recognised before the rule/declaration split so
        /// that a value containing braces, such as a <c>--x: { ... }</c> token
        /// sequence, is treated as a declaration value rather than a nested rule.
        /// </remarks>
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

        /// <summary>
        /// Parses a style rule whose selector prelude occupies
        /// <c>[start, end)</c>, then parses its declarations and nested rules.
        /// </summary>
        /// <param name="start">Index of the first selector token.</param>
        /// <param name="end">
        /// Index of the opening brace, exclusive. <c>_position</c> is already one
        /// past the brace when this method is called.
        /// </param>
        /// <remarks>
        /// <para>
        /// The prelude is split on commas that are not inside parentheses or
        /// square brackets, so selectors such as <c>:is(.a, .b)</c> and
        /// <c>[data-x="a,b"]</c> stay intact. Each branch is flattened against
        /// every selector of the enclosing rule, then de-duplicated.
        /// </para>
        /// <para>
        /// When no selector survives flattening, the block is still consumed as a
        /// container so the token stream stays in sync; hiding the parent selector
        /// prevents declarations from being credited to the enclosing rule.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Flattens one comma-separated branch of a selector against all enclosing
        /// selectors and records the resulting selector ids.
        /// </summary>
        /// <param name="child">
        /// The branch's tokens. Empty branches, produced by a trailing comma, are
        /// ignored.
        /// </param>
        /// <param name="parents">
        /// Ids of the enclosing rule's selectors, or empty at the stylesheet root
        /// or inside a container at-rule.
        /// </param>
        /// <remarks>
        /// Without a parent the branch is interned verbatim. Otherwise it is
        /// composed once per parent, which is the cartesian product required by CSS
        /// nesting; the number of interned selectors is therefore
        /// <c>parents.Length</c> per branch before de-duplication.
        /// </remarks>
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

        /// <summary>
        /// Adds a selector id to the current rule's list unless it is already
        /// present.
        /// </summary>
        /// <param name="id">The interned selector id to add.</param>
        /// <remarks>
        /// Selector lists are short, so a linear scan beats allocating a hash set
        /// per rule. The scratch list is owned by the parser and cleared at the
        /// start of every rule.
        /// </remarks>
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
        /// <param name="parent">Tokens of the enclosing selector.</param>
        /// <param name="child">Tokens of the nested selector branch.</param>
        /// <remarks>
        /// <para>
        /// The result is appended to the parser's composer list and is only valid
        /// until the next call: it is reused for every parent/child pair to avoid
        /// allocating. The caller interns it before the next composition.
        /// </para>
        /// <para>
        /// Multiple <c>&amp;</c> tokens are each replaced with the whole parent
        /// selector. A gap around <c>&amp;</c> is copied from the child's original
        /// spacing, so <c>&amp; .a</c> stays a descendant and <c>&amp;.a</c> stays
        /// a compound. A leading <c>&amp;</c> never introduces a leading gap,
        /// because the composed selector starts at position zero.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Copies tokens into the parser's composer list, rewriting their packed
        /// positions so the composed selector is dense and gap-free except where
        /// the original source had a meaningful separator.
        /// </summary>
        /// <param name="tokens">The slice to append.</param>
        /// <param name="gapBeforeFirst">
        /// Whether the first token was separated from the preceding text by
        /// whitespace. Ignored when <paramref name="cursor"/> is zero, since a
        /// selector cannot start with a descendant gap.
        /// </param>
        /// <param name="cursor">
        /// The synthetic position to continue from; advanced to the end of the
        /// last copied token.
        /// </param>
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

        /// <summary>
        /// Determines whether a token is the CSS nesting selector <c>&amp;</c>.
        /// </summary>
        /// <param name="token">The token to test, by readonly reference.</param>
        /// <returns>
        /// <see langword="true"/> when the token is the interned <c>&amp;</c>
        /// delimiter.
        /// </returns>
        private static bool IsAmpersand(in Token token)
        {
            return token.Kind == TokenKind.Delimiter && token.InternedId == AmpersandId;
        }

        // ------------------------------------------------------------------
        // Declarations
        // ------------------------------------------------------------------

        /// <summary>
        /// Parses a declaration or a declaration-like segment beginning at
        /// <paramref name="start"/>, then advances past its terminating semicolon
        /// or to the enclosing closing brace.
        /// </summary>
        /// <param name="start">Index of the first token of the property name.</param>
        /// <param name="customProperty">
        /// <see langword="true"/> when the property is a <c>--name</c> custom
        /// property, whose name must not be lower-cased.
        /// </param>
        /// <remarks>
        /// <para>
        /// The branch taken depends on parser state. Inside a descriptor at-rule
        /// the text is recorded as a <see cref="CssDescriptor"/>, or as a
        /// <see cref="CssMarginBox"/> when inside a margin box. Inside
        /// <c>@keyframes</c> it is recorded once per current selector, which is
        /// the keyframe stop (<c>0%</c>, <c>from</c>, <c>to</c>). Otherwise it
        /// becomes a style rule for every selector of the enclosing rule.
        /// </para>
        /// <para>
        /// Property names are normalised with single spaces where the source had
        /// them and lower-cased unless they are custom properties. Values are
        /// normalised the same way but keep their case, because CSS values can be
        /// case-sensitive. Declarations with no selector or an empty value are
        /// discarded without interning.
        /// </para>
        /// <para>
        /// A malformed declaration with no top-level colon is delegated to
        /// <see cref="SkipDeclaration"/> for local error recovery.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Writes the text of a token range into <c>_textBuffer</c>,
        /// inserting one space wherever the source had whitespace between tokens.
        /// </summary>
        /// <param name="start">Index of the first token, inclusive.</param>
        /// <param name="endExclusive">Index one past the last token.</param>
        /// <param name="lowerCase">
        /// <see langword="true"/> to ASCII-lower-case the text, used for property
        /// names; <see langword="false"/> to preserve case for values.
        /// </param>
        /// <returns>The number of characters written.</returns>
        /// <remarks>
        /// Comments and whitespace never appear as tokens, so a gap is detected by
        /// comparing the previous token's end index with the current token's start
        /// index. The buffer can be reallocated and is owned by the parser; the
        /// returned text is valid until the next normalisation call.
        /// </remarks>
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

        /// <summary>
        /// Appends interned token text to <c>_textBuffer</c>, optionally
        /// ASCII-lower-casing it.
        /// </summary>
        /// <param name="text">The interned text to append; never <see langword="null"/>.</param>
        /// <param name="lowerCase">
        /// Whether to convert <c>A</c>–<c>Z</c> to lower case.
        /// </param>
        /// <param name="length">
        /// The current write offset, advanced by the number of characters written.
        /// </param>
        /// <remarks>
        /// Only ASCII letters are folded, matching CSS keyword case-insensitivity
        /// without the allocations and culture rules of <see cref="string.ToLower()"/>.
        /// </remarks>
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

        /// <summary>
        /// Appends a single character to <c>_textBuffer</c>.
        /// </summary>
        /// <param name="c">The character to append.</param>
        /// <param name="length">
        /// The current write offset, advanced by one.
        /// </param>
        private void AppendChar(char c, ref int length)
        {
            EnsureTextCapacity(length + 1);
            _textBuffer[length++] = c;
        }

        /// <summary>
        /// Ensures <c>_textBuffer</c> can hold at least
        /// <paramref name="required"/> characters, renting a larger array from the
        /// shared pool when necessary.
        /// </summary>
        /// <param name="required">The minimum required capacity.</param>
        /// <remarks>
        /// Growth is at least double the current length. The old buffer is
        /// returned to the pool immediately; the new array may be larger than
        /// requested, which is why callers always track their own logical length
        /// instead of using <c>Length</c>.
        /// </remarks>
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

        /// <summary>
        /// Determines whether the token at <paramref name="index"/> begins a custom
        /// property declaration such as <c>--brand-color: red</c>.
        /// </summary>
        /// <param name="index">Index of the candidate property token.</param>
        /// <returns>
        /// <see langword="true"/> when the token is an identifier or property name
        /// whose interned text starts with two hyphens and is followed by a colon
        /// token. Whitespace before the colon is permitted, as it is in CSS.
        /// </returns>
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

        /// <summary>
        /// Recovers from a segment that has no top-level colon by skipping to the
        /// next statement boundary.
        /// </summary>
        /// <param name="start">Index of the first token of the malformed segment.</param>
        /// <remarks>
        /// Scanning is parenthesis- and bracket-aware, so a semicolon inside
        /// <c>url(...)</c> or an attribute selector does not end the skip.
        /// <c>_position</c> is left at the opening or closing brace that
        /// caused the stop, or just past a semicolon, letting the enclosing block
        /// consume it normally. Reaching EOF moves the position to the end.
        /// </remarks>
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

        /// <summary>
        /// Scans an at-rule prelude for its top-level terminator.
        /// </summary>
        /// <param name="start">
        /// Index of the first token after the at-keyword.
        /// </param>
        /// <param name="terminator">
        /// Receives <c>';'</c> or <c>'{'</c> when one is found, <c>'}'</c> when the
        /// enclosing block closes first, or <c>'\0'</c> at EOF.
        /// </param>
        /// <returns>
        /// The index of the terminating token, or <c>_tokens.Length</c> at EOF.
        /// </returns>
        /// <remarks>
        /// Parentheses and square brackets are tracked, so <c>;</c> inside
        /// <c>url(...)</c> and braces inside attribute selectors do not terminate
        /// the prelude. Braces are not tracked because the first top-level one
        /// starts the block.
        /// </remarks>
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

        /// <summary>
        /// Scans the header of a statement that is not an at-rule, stopping at the
        /// first top-level <c>{</c>, <c>;</c> or <c>}</c>.
        /// </summary>
        /// <param name="start">Index of the first token of the statement.</param>
        /// <param name="terminator">
        /// Receives the terminating character, or <c>'\0'</c> at EOF.
        /// </param>
        /// <returns>
        /// The index of the terminating token, or the token count at EOF.
        /// </returns>
        /// <remarks>
        /// This is the declaration/rule discriminator: a <c>{</c> means the segment
        /// is a style rule, while a semicolon or closing brace means it is a
        /// declaration. Parentheses and brackets are tracked so pseudo-class
        /// arguments and attribute selectors do not end the scan early.
        /// </remarks>
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

        /// <summary>
        /// Finds the first colon that separates a declaration's property name from
        /// its value, ignoring colons nested in parentheses or brackets.
        /// </summary>
        /// <param name="start">Index of the first token of the segment.</param>
        /// <returns>
        /// The index of the colon, or -1 when a top-level <c>{</c>, <c>;</c> or
        /// <c>}</c> is reached first, or the token stream ends.
        /// </returns>
        /// <remarks>
        /// A colon inside <c>:is(...)</c> or <c>[data-x="a:b"]</c> therefore does
        /// not split the declaration. Because <see cref="ParseSegment"/> resolves
        /// rule headers first, pseudo-class colons in selectors are never seen
        /// here.
        /// </remarks>
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

        /// <summary>
        /// Scans a declaration value up to its terminator, tracking parentheses,
        /// square brackets and balanced curly braces.
        /// </summary>
        /// <param name="start">Index of the first value token.</param>
        /// <param name="terminator">
        /// Receives the terminating token kind and index. At EOF the kind is
        /// <see cref="TokenKind.Delimiter"/> and the index is the token count.
        /// </param>
        /// <returns>The index of the terminating token or the token count.</returns>
        /// <remarks>
        /// Balanced braces allow custom properties and values that contain blocks
        /// to be consumed without ending the declaration. A <c>}</c> at brace depth
        /// zero belongs to the enclosing block and is not consumed; a <c>;</c> at
        /// depth zero ends the declaration. <c>!important</c> and comments are
        /// already part of the value token range.
        /// </remarks>
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

        /// <summary>
        /// Determines whether an at-keyword introduces a condition block whose
        /// prelude is compiled by the <see cref="ConditionCompiler"/>.
        /// </summary>
        /// <param name="atKeyword">
        /// The interned at-keyword including <c>@</c>, or <see langword="null"/>
        /// when the token text is unavailable.
        /// </param>
        /// <returns>
        /// <see langword="true"/> for <c>@media</c>, <c>@supports</c>,
        /// <c>@container</c>, <c>@document</c>, <c>@-moz-document</c> and
        /// <c>@starting-style</c>.
        /// </returns>
        /// <remarks>
        /// Matching is ordinal and case-insensitive. The parser only groups these
        /// blocks under a condition id; whether the condition can be evaluated is
        /// entirely up to the compiler. A compiler that does not understand
        /// <c>@supports</c> returns an always-false predicate.
        /// </remarks>
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

        /// <summary>
        /// Determines whether an at-keyword is the legacy <c>@nest</c> rule.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns><see langword="true"/> for <c>@nest</c>, case-insensitively.</returns>
        /// <remarks>
        /// <c>@nest</c> is parsed exactly like a nested style rule, so its prelude
        /// is composed with the enclosing selector and may contain <c>&amp;</c>.
        /// </remarks>
        private static bool IsNest(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@nest", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an at-keyword introduces a keyframes block.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns>
        /// <see langword="true"/> for <c>@keyframes</c> and the WebKit alias
        /// <c>@-webkit-keyframes</c>, case-insensitively.
        /// </returns>
        private static bool IsKeyframes(string? atKeyword)
        {
            return atKeyword is not null
                && (atKeyword.Equals("@keyframes", StringComparison.OrdinalIgnoreCase)
                    || atKeyword.Equals("@-webkit-keyframes", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Determines whether an at-keyword introduces an <c>@scope</c> block.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns><see langword="true"/> for <c>@scope</c>, case-insensitively.</returns>
        private static bool IsScope(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@scope", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an at-keyword introduces a cascade-layer block.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns><see langword="true"/> for <c>@layer</c>, case-insensitively.</returns>
        private static bool IsLayer(string? atKeyword)
        {
            return atKeyword is not null
                && atKeyword.Equals("@layer", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether an at-keyword introduces a descriptor block whose
        /// declarations describe a resource instead of styling an element.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns>
        /// <see langword="true"/> for <c>@font-face</c>, <c>@page</c>,
        /// <c>@property</c>, <c>@counter-style</c>, <c>@font-palette-values</c> and
        /// <c>@view-transition</c>, case-insensitively.
        /// </returns>
        /// <remarks>
        /// Declarations inside these blocks are recorded as
        /// <see cref="CssDescriptor"/> entries and never become style rules, so
        /// they cannot accidentally inherit the enclosing selector.
        /// </remarks>
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

        /// <summary>
        /// Determines whether an at-keyword names one of the sixteen
        /// <c>@page</c> margin boxes.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, possibly <see langword="null"/>.</param>
        /// <returns>
        /// <see langword="true"/> for the corner boxes, the top and bottom
        /// left/center/right boxes, and the left and right top/middle/bottom
        /// boxes, case-insensitively.
        /// </returns>
        /// <remarks>
        /// The name alone is not sufficient: <see cref="ParseAtRule"/> only treats
        /// a match as a margin box when a descriptor at-rule, normally
        /// <c>@page</c>, is already open. Elsewhere the same at-rule falls through
        /// to the generic unknown-container path.
        /// </remarks>
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

        /// <summary>
        /// Reconstructs an at-rule prelude or name from its tokens, joining token
        /// text with a single space wherever the source had whitespace, and interns
        /// the result.
        /// </summary>
        /// <param name="start">Index of the first prelude token, inclusive.</param>
        /// <param name="end">Index one past the last prelude token.</param>
        /// <returns>
        /// The interned id of the text, including the id of the empty string when
        /// the prelude is empty.
        /// </returns>
        /// <remarks>
        /// The buffer is sized exactly from the token texts first, so a long
        /// single-token name such as <c>progress-bar-stripes</c> is never truncated.
        /// Unlike <see cref="NormalizeTokens"/> this allocates a temporary array and
        /// does not touch the pooled text buffer, because preludes are rare and can
        /// be arbitrarily long.
        /// </remarks>
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

        /// <summary>
        /// Extracts the URL of an <c>@import</c> prelude.
        /// </summary>
        /// <param name="start">Index of the first prelude token.</param>
        /// <param name="end">Index one past the last prelude token.</param>
        /// <returns>
        /// The interned id of the first string token, or the interned text of the
        /// whole prelude when no string token is present.
        /// </returns>
        /// <remarks>
        /// A quoted URL is taken from its string token, whose interned text still
        /// includes the surrounding quote characters. An unquoted
        /// <c>url(foo.css)</c> has no string token, so the returned text includes
        /// the <c>url(</c> wrapper as well; callers must be prepared for both
        /// forms.
        /// </remarks>
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

        /// <summary>
        /// Records a statement at-rule, one terminated by <c>;</c> rather than by a
        /// block.
        /// </summary>
        /// <param name="atKeyword">The at-keyword text, or <see langword="null"/>.</param>
        /// <param name="start">Index of the at-keyword token.</param>
        /// <param name="end">Index of the terminating semicolon.</param>
        /// <remarks>
        /// <para>
        /// <c>@import</c> records the URL with the currently active layer and
        /// condition. <c>@charset</c> records its prelude verbatim, including any
        /// quotes. <c>@namespace</c> records the token immediately before the first
        /// string as the optional prefix, when that token is an identifier; if no
        /// string is present, the whole prelude becomes the URI.
        /// <c>@layer a, b;</c> records each name as a cascade layer.
        /// </para>
        /// <para>
        /// Only the enclosing layer and condition are recorded for an import; the
        /// <c>layer(...)</c>, <c>supports(...)</c> and media modifiers in the
        /// import's own prelude are not parsed, and the supports-condition id is
        /// always -1. All other statement at-rules are ignored. Imports are never
        /// fetched or inlined; the host is responsible for resolving them.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Records every comma-separated layer name in a prelude, in declaration
        /// order.
        /// </summary>
        /// <param name="start">Index of the first name token.</param>
        /// <param name="end">Index one past the last name token.</param>
        /// <remarks>
        /// Empty segments are skipped. Nested layer paths are interned as written;
        /// the parser does not split <c>a.b</c> into a parent <c>a</c> and a child
        /// <c>b</c>. The first call for a name fixes its cascade order in
        /// <see cref="DataTable.GetOrAddLayer"/>.
        /// </remarks>
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

        /// <summary>
        /// Records the names of a block <c>@layer</c> prelude and returns the layer
        /// id to push while its block is parsed.
        /// </summary>
        /// <param name="start">Index of the at-keyword token.</param>
        /// <param name="end">Index of the opening brace.</param>
        /// <returns>
        /// The id of the layer that subsequent rules should record, used as the
        /// innermost entry of the layer stack.
        /// </returns>
        /// <remarks>
        /// The whole prelude is interned as the block's name and every
        /// comma-separated name is also recorded. A block with several names is
        /// tolerated but the pushed id describes the combined text rather than one
        /// of the listed layers.
        /// </remarks>
        private int RecordLayer(int start, int end)
        {
            // Single name (or first name) for block push.
            var nameId = InternPreludeText(start + 1, end);
            // Handle comma: record all, return first.
            RecordLayerNames(start + 1, end);
            return _table.GetOrAddLayer(nameId);
        }

        /// <summary>
        /// Records a whole <c>@keyframes</c> block by parsing it with keyframe
        /// recording enabled.
        /// </summary>
        /// <param name="atKeyword">The at-keyword, <c>@keyframes</c> or the WebKit alias.</param>
        /// <param name="start">Index of the at-keyword token.</param>
        /// <param name="end">Index of the opening brace.</param>
        /// <remarks>
        /// <para>
        /// The animation name is the whole prelude interned as text. Inside the
        /// block, keyframe stops such as <c>0%</c>, <c>from</c> and <c>to</c> are
        /// parsed as ordinary rules, and each declaration becomes one
        /// <see cref="CssKeyframe"/> per stop. The enclosing condition applies, so
        /// keyframes declared in a media query are gated by that query.
        /// </para>
        /// <para>
        /// A container frame hides any enclosing style selector, so keyframes
        /// nested in a rule cannot compose their stops with it. The flags are reset
        /// even when the block reaches EOF.
        /// </para>
        /// </remarks>
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

        /// <summary>
        /// Records an <c>@scope</c> boundary from its prelude.
        /// </summary>
        /// <param name="start">Index of the at-keyword token.</param>
        /// <param name="end">Index of the opening brace.</param>
        /// <remarks>
        /// The prelude, containing the root and optional limit selectors, is stored
        /// as interned text in <see cref="DataTable.AddScope"/>. The parser does not
        /// rewrite or constrain the selectors inside the scope block; the record is
        /// available to the host for its own scoping model.
        /// </remarks>
        private void RecordScope(int start, int end)
        {
            var preludeId = InternPreludeText(start + 1, end);
            _table.AddScope(preludeId);
        }

        /// <summary>
        /// Interns the prelude that distinguishes one descriptor block from another,
        /// such as the page selector of <c>@page :first</c>.
        /// </summary>
        /// <param name="atKeyword">
        /// The descriptor at-rule name. Currently unused; the context is derived
        /// from the prelude alone.
        /// </param>
        /// <param name="start">Index of the at-keyword token.</param>
        /// <param name="end">Index of the opening brace.</param>
        /// <returns>The interned prelude id.</returns>
        /// <remarks>
        /// An empty prelude interns the empty string rather than returning -1, so
        /// every descriptor has a valid context id.
        /// </remarks>
        private int RecordDescriptorContext(string? atKeyword, int start, int end)
        {
            var contextId = InternPreludeText(start + 1, end);
            return contextId;
        }

        /// <summary>
        /// Gets the id of the innermost cascade layer, or -1 when no layer is open.
        /// </summary>
        /// <value>
        /// A non-negative layer id while a block <c>@layer</c> is being parsed, or
        /// -1 inside unlayered styles.
        /// </value>
        /// <remarks>
        /// Rules store this id, and <see cref="DataTable"/> orders layered rules
        /// before unlayered ones. Only the innermost layer is recorded, so a nested
        /// layer is not emitted as a compound path.
        /// </remarks>
        private int CurrentLayerId => _layerStack.Count > 0 ? _layerStack[^1] : -1;

        /// <summary>
        /// Describes where a declaration's value ended.
        /// </summary>
        /// <param name="Kind">
        /// <see cref="TokenKind.SemiColon"/> or
        /// <see cref="TokenKind.RightCurlyBrace"/> for a real terminator, or
        /// <see cref="TokenKind.Delimiter"/> as the EOF sentinel.
        /// </param>
        /// <param name="Index">
        /// Index of the terminating token, or the token count at EOF.
        /// </param>
        private readonly record struct DeclarationTerminator(TokenKind Kind, int Index);
    }
}
