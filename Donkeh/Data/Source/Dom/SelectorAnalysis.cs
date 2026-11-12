using Donkeh.Parser;
using Donkeh.Parser.Objects;

namespace Donkeh.Data.Dom
{
    /// <summary>
    /// One pass over a selector at store time: it splits the selector into
    /// compounds, computes the packed specificity, parses attribute filters and
    /// structural pseudo classes, and reports whether the selector can be
    /// evaluated by the engine.
    /// </summary>
    /// <remarks>
    /// Compounds are split on explicit combinators and on descendant gaps, with
    /// bracket and parenthesis depth tracked so operators inside
    /// <c>[attr^=v]</c> and <c>:nth-child()</c> are not treated as combinators.
    /// Structural and host-driven pseudo classes (<c>:first-child</c>,
    /// <c>:hover</c>, <c>:focus</c>, <c>:active</c>, <c>:checked</c>, ...) are
    /// evaluated at match time. Pseudo elements and logical functions that need
    /// generated content (<c>::before</c>, <c>:not()</c>, ...) still mark the
    /// compound unsupported, so the selector is never matched wrongly.
    /// </remarks>
    internal static class SelectorAnalysis
    {
        /// <summary>Number of bits reserved for each specificity component.</summary>
        internal const int CountBits = 10;

        /// <summary>Mask for one specificity component (<c>0x3FF</c>).</summary>
        internal const int CountMask = (1 << CountBits) - 1;

        /// <summary>Bit offset of the class/attribute/pseudo component.</summary>
        internal const int ClassShift = CountBits;

        /// <summary>Bit offset of the id component.</summary>
        internal const int IdShift = CountBits * 2;

        /// <summary>The interned id of <c>"."</c>, used to recognise class selectors.</summary>
        internal static readonly int ClassDelimiterId = InternTable.Intern(".");

        // '=' has no dedicated token kind; it is emitted as a delimiter.
        private static readonly int EqualsDelimiterId = InternTable.Intern("=");

        private static readonly int RootId = InternTable.Intern(":root");
        private static readonly int EmptyId = InternTable.Intern(":empty");
        private static readonly int FirstChildId = InternTable.Intern(":first-child");
        private static readonly int LastChildId = InternTable.Intern(":last-child");
        private static readonly int OnlyChildId = InternTable.Intern(":only-child");
        private static readonly int FirstOfTypeId = InternTable.Intern(":first-of-type");
        private static readonly int LastOfTypeId = InternTable.Intern(":last-of-type");
        private static readonly int OnlyOfTypeId = InternTable.Intern(":only-of-type");
        private static readonly int NthChildId = InternTable.Intern(":nth-child");
        private static readonly int NthLastChildId = InternTable.Intern(":nth-last-child");
        private static readonly int NthOfTypeId = InternTable.Intern(":nth-of-type");
        private static readonly int NthLastOfTypeId = InternTable.Intern(":nth-last-of-type");

        private static readonly int HoverId = InternTable.Intern(":hover");
        private static readonly int FocusId = InternTable.Intern(":focus");
        private static readonly int FocusVisibleId = InternTable.Intern(":focus-visible");
        private static readonly int FocusWithinId = InternTable.Intern(":focus-within");
        private static readonly int ActiveId = InternTable.Intern(":active");
        private static readonly int CheckedId = InternTable.Intern(":checked");
        private static readonly int DisabledId = InternTable.Intern(":disabled");
        private static readonly int EnabledId = InternTable.Intern(":enabled");
        private static readonly int LinkId = InternTable.Intern(":link");
        private static readonly int AnyLinkId = InternTable.Intern(":any-link");
        private static readonly int PlaceholderShownId = InternTable.Intern(":placeholder-shown");
        private static readonly int ReadOnlyId = InternTable.Intern(":read-only");
        private static readonly int ReadWriteId = InternTable.Intern(":read-write");
        private static readonly int NotId = InternTable.Intern(":not");

        private const int MaxAnBLength = 32;

        /// <summary>
        /// Analyses <paramref name="selector"/>, appending its compounds, attribute
        /// filters and pseudo classes to the supplied lists, and returning the
        /// packed specificity.
        /// </summary>
        /// <param name="selector">The selector tokens, already normalised.</param>
        /// <param name="compounds">Receives one record per compound, left to right.</param>
        /// <param name="filters">
        /// Receives the parsed attribute filters. Offsets in the compound records are
        /// relative to this list, so the caller must rebase them.
        /// </param>
        /// <param name="pseudos">
        /// Receives the parsed pseudo classes, with the same relative-offset contract
        /// as <paramref name="filters"/>.
        /// </param>
        /// <param name="matchable">
        /// Set to <see langword="true"/> only when every construct is supported and
        /// at least one compound was produced.
        /// </param>
        /// <param name="pseudoElement">
        /// Receives the generated pseudo-element targeted by the selector, or
        /// <see cref="PseudoElementKind.None"/> for the element itself.
        /// </param>
        /// <returns>
        /// The specificity packed as ids in bits 20-29, classes in bits 10-19 and
        /// types in bits 0-9.
        /// </returns>
        /// <remarks>
        /// The lists are appended to, never cleared, so the caller can analyse
        /// several selectors into one arena and record offsets between calls. Token
        /// text is read from the intern table; the analysis itself allocates no
        /// per-compound arrays.
        /// </remarks>
        internal static int Analyze(
            ReadOnlySpan<Token> selector,
            List<CompoundRecord> compounds,
            List<AttributeFilter> filters,
            List<PseudoRecord> pseudos,
            out bool matchable,
            out PseudoElementKind pseudoElement)
        {
            var idCount = 0;
            var classCount = 0;
            var typeCount = 0;
            var pseudoElementKind = PseudoElementKind.None;

            var supported = true;
            var firstCompound = compounds.Count;
            var compoundStart = 0;
            var leading = Combinator.None;
            var bracketDepth = 0;
            var parenDepth = 0;

            for (var i = 0; i < selector.Length; i++)
            {
                if (bracketDepth == 0 && parenDepth == 0)
                {
                    var combinator = ToCombinator(selector[i].Kind);

                    if (combinator != Combinator.None)
                    {
                        AddCompound(
                            selector, compoundStart, i, leading,
                            compounds, filters, pseudos,
                            ref idCount, ref classCount, ref typeCount, ref supported,
                            ref pseudoElementKind);

                        leading = combinator;
                        compoundStart = i + 1;
                        continue;
                    }

                    if (i > compoundStart && DataTable.HasGap(selector, i))
                    {
                        AddCompound(
                            selector, compoundStart, i, leading,
                            compounds, filters, pseudos,
                            ref idCount, ref classCount, ref typeCount, ref supported,
                            ref pseudoElementKind);

                        leading = Combinator.Descendant;
                        compoundStart = i;
                    }
                }

                switch (selector[i].Kind)
                {
                    case TokenKind.LeftSquareBracket:
                        bracketDepth++;
                        break;

                    case TokenKind.RightSquareBracket:
                        if (bracketDepth > 0)
                        {
                            bracketDepth--;
                        }
                        else
                        {
                            supported = false;
                        }

                        break;

                    case TokenKind.LeftParenthesis:
                        parenDepth++;
                        break;

                    case TokenKind.RightParenthesis:
                        if (parenDepth > 0)
                        {
                            parenDepth--;
                        }
                        else
                        {
                            supported = false;
                        }

                        break;

                    case TokenKind.ColumnCombinator:
                    case TokenKind.Comma:
                        supported = false;
                        break;
                }
            }

            AddCompound(
                selector, compoundStart, selector.Length, leading,
                compounds, filters, pseudos,
                ref idCount, ref classCount, ref typeCount, ref supported,
                ref pseudoElementKind);

            if (bracketDepth != 0 || parenDepth != 0)
            {
                supported = false;
            }

            pseudoElement = pseudoElementKind;
            matchable = supported && compounds.Count > firstCompound;
            return Pack(idCount, classCount, typeCount);
        }

        /// <summary>
        /// Adds one compound slice, counts its specificity contributions and parses
        /// its filters and pseudo classes.
        /// </summary>
        /// <param name="selector">The full selector tokens.</param>
        /// <param name="start">First token of the compound.</param>
        /// <param name="end">One past the last token of the compound.</param>
        /// <param name="leading">The combinator linking the compound to its left.</param>
        /// <param name="compounds">Receives the compound record.</param>
        /// <param name="filters">Receives attribute filters.</param>
        /// <param name="pseudos">Receives pseudo classes.</param>
        /// <param name="idCount">Accumulates the id count.</param>
        /// <param name="classCount">Accumulates the class/attribute/pseudo count.</param>
        /// <param name="typeCount">Accumulates the type/pseudo element count.</param>
        /// <param name="supported">Cleared when the compound cannot be evaluated.</param>
        /// <param name="pseudoElement">
        /// Receives the generated pseudo-element targeted by this compound.
        /// </param>
        private static void AddCompound(
            ReadOnlySpan<Token> selector,
            int start,
            int end,
            Combinator leading,
            List<CompoundRecord> compounds,
            List<AttributeFilter> filters,
            List<PseudoRecord> pseudos,
            ref int idCount,
            ref int classCount,
            ref int typeCount,
            ref bool supported,
            ref PseudoElementKind pseudoElement)
        {
            var filterOffset = filters.Count;
            var pseudoOffset = pseudos.Count;
            var compoundSupported = true;

            if (end <= start)
            {
                compoundSupported = false;
            }

            for (var i = start; i < end; i++)
            {
                var token = selector[i];

                switch (token.Kind)
                {
                    case TokenKind.Identifier:
                    case TokenKind.PropertyName:
                        // A class name is counted with its '.'. Names that happen
                        // to be CSS property keywords (".content", ".color", ...)
                        // are tokenised as PropertyName, but they are still valid
                        // class/type names in a selector.
                        if (i > start && IsClassDelimiter(selector, i - 1))
                        {
                            break;
                        }

                        typeCount++;
                        break;

                    case TokenKind.Asterisk:
                        break;

                    case TokenKind.Hash:
                        idCount++;
                        break;

                    case TokenKind.Delimiter:
                        if (token.InternedId != ClassDelimiterId)
                        {
                            compoundSupported = false;
                            break;
                        }

                        classCount++;

                        if (i + 1 >= end
                            || selector[i + 1].Kind is not (TokenKind.Identifier or TokenKind.PropertyName))
                        {
                            compoundSupported = false;
                        }

                        break;

                    case TokenKind.LeftSquareBracket:
                        classCount++;

                        if (TryParseAttributeFilter(selector, i, end, out var close, out var filter))
                        {
                            filters.Add(filter);
                            i = close;
                        }
                        else
                        {
                            compoundSupported = false;
                            i = SkipToBracketEnd(selector, i, end);
                        }

                        break;

                    case TokenKind.PseudoClass:
                        classCount++;

                        if (TryParsePseudoClass(selector, i, end, start, out var pseudo, out var next))
                        {
                            pseudos.Add(pseudo);
                            i = next;
                        }
                        else
                        {
                            compoundSupported = false;
                        }

                        break;

                    case TokenKind.PseudoElement:
                        typeCount++;

                        // Only ::before and ::after generate boxes, and a
                        // pseudo-element must be the subject (the final token).
                        // Every other pseudo-element remains unmatchable.
                        var pseudoName = InternTable.GetString(token.InternedId);
                        var isSubject = i == end - 1;

                        if (isSubject && pseudoName is "::before" or ":before")
                        {
                            pseudoElement = PseudoElementKind.Before;
                        }
                        else if (isSubject && pseudoName is "::after" or ":after")
                        {
                            pseudoElement = PseudoElementKind.After;
                        }
                        else
                        {
                            compoundSupported = false;
                        }

                        break;

                    case TokenKind.Function:
                        classCount++;
                        compoundSupported = false;
                        break;

                    default:
                        compoundSupported = false;
                        break;
                }
            }

            if (!compoundSupported)
            {
                supported = false;
            }

            compounds.Add(new CompoundRecord
            {
                TokenStart = start,
                TokenLength = end - start,
                FilterOffset = filterOffset,
                FilterCount = filters.Count - filterOffset,
                PseudoOffset = pseudoOffset,
                PseudoCount = pseudos.Count - pseudoOffset,
                LeadingCombinator = leading,
                IsSupported = compoundSupported
            });
        }

        /// <summary>
        /// Parses an attribute selector starting at its opening bracket.
        /// </summary>
        /// <param name="selector">The full selector tokens.</param>
        /// <param name="open">Index of the <c>[</c> token.</param>
        /// <param name="end">One past the compound's last token.</param>
        /// <param name="close">Receives the index of the closing <c>]</c>.</param>
        /// <param name="filter">Receives the parsed filter on success.</param>
        /// <returns><see langword="true"/> when the filter was fully understood.</returns>
        private static bool TryParseAttributeFilter(
            ReadOnlySpan<Token> selector,
            int open,
            int end,
            out int close,
            out AttributeFilter filter)
        {
            filter = default;
            close = -1;

            for (var i = open + 1; i < end; i++)
            {
                if (selector[i].Kind == TokenKind.RightSquareBracket)
                {
                    close = i;
                    break;
                }
            }

            if (close < 0)
            {
                return false;
            }

            var inner = selector[(open + 1)..close];

            if (inner.IsEmpty || inner[0].Kind != TokenKind.Identifier)
            {
                return false;
            }

            var nameId = inner[0].InternedId;

            if (inner.Length == 1)
            {
                filter = new AttributeFilter(nameId, -1, AttributeOperator.Exists, false);
                return true;
            }

            var operatorIndex = 1;
            AttributeOperator op;

            if (IsEquals(inner[operatorIndex]))
            {
                op = AttributeOperator.Equals;
            }
            else if (inner[operatorIndex].Kind == TokenKind.Tilde
                && inner.Length > 2
                && IsEquals(inner[2]))
            {
                op = AttributeOperator.Includes;
                operatorIndex++;
            }
            else if (inner[operatorIndex].Kind == TokenKind.Asterisk
                && inner.Length > 2
                && IsEquals(inner[2]))
            {
                op = AttributeOperator.Substring;
                operatorIndex++;
            }
            else if (inner[operatorIndex].Kind == TokenKind.Delimiter
                && inner.Length > 2
                && IsEquals(inner[2]))
            {
                op = InternTable.GetString(inner[operatorIndex].InternedId) switch
                {
                    "^" => AttributeOperator.Prefix,
                    "$" => AttributeOperator.Suffix,
                    "|" => AttributeOperator.DashMatch,
                    _ => (AttributeOperator)byte.MaxValue
                };

                if (op == (AttributeOperator)byte.MaxValue)
                {
                    return false;
                }

                operatorIndex++;
            }
            else
            {
                return false;
            }

            var valueIndex = operatorIndex + 1;

            if (valueIndex >= inner.Length)
            {
                return false;
            }

            var value = inner[valueIndex];

            if (value.Kind is not (TokenKind.Identifier or TokenKind.PropertyName or TokenKind.String))
            {
                return false;
            }

            var ignoreCase = false;
            var flagIndex = valueIndex + 1;

            if (flagIndex < inner.Length)
            {
                if (inner.Length != flagIndex + 1 || inner[flagIndex].Kind != TokenKind.Identifier)
                {
                    return false;
                }

                var flag = InternTable.GetString(inner[flagIndex].InternedId);

                if (string.Equals(flag, "i", StringComparison.OrdinalIgnoreCase))
                {
                    ignoreCase = true;
                }
                else if (!string.Equals(flag, "s", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            filter = new AttributeFilter(nameId, value.InternedId, op, ignoreCase);
            return true;
        }

        /// <summary>
        /// Parses a pseudo class token, including the <c>An+B</c> argument of the
        /// <c>:nth-*</c> forms.
        /// </summary>
        /// <param name="selector">The full selector tokens.</param>
        /// <param name="index">Index of the pseudo class token.</param>
        /// <param name="end">One past the compound's last token.</param>
        /// <param name="compoundStart">First token of the containing compound.</param>
        /// <param name="pseudo">Receives the parsed pseudo class on success.</param>
        /// <param name="next">
        /// Receives the index of the last token consumed (the closing parenthesis
        /// for a functional form) so the caller can skip the argument list.
        /// </param>
        /// <returns><see langword="true"/> when the pseudo class is supported.</returns>
        /// <remarks>
        /// The <c>An+B</c> text is rebuilt from the argument tokens into a fixed
        /// stack buffer of <see cref="MaxAnBLength"/> characters; a longer argument
        /// is rejected rather than truncated.
        /// </remarks>
        internal static bool TryParsePseudoClass(
            ReadOnlySpan<Token> selector,
            int index,
            int end,
            int compoundStart,
            out PseudoRecord pseudo,
            out int next)
        {
            pseudo = default;
            next = index;

            var id = selector[index].InternedId;

            if (id == NotId)
            {
                if (index + 2 >= end || selector[index + 1].Kind != TokenKind.LeftParenthesis)
                {
                    return false;
                }

                var depth = 0;
                var closeIndex = -1;

                for (var i = index + 1; i < end; i++)
                {
                    if (selector[i].Kind == TokenKind.LeftParenthesis)
                    {
                        depth++;
                    }
                    else if (selector[i].Kind == TokenKind.RightParenthesis)
                    {
                        depth--;

                        if (depth == 0)
                        {
                            closeIndex = i;
                            break;
                        }
                    }
                }

                if (closeIndex <= index + 2)
                {
                    return false;
                }

                // A single simple compound is the common Bootstrap pattern
                // (:not(.show), :not(.collapsed), :not(.disabled)). Selector
                // lists and combinators inside the argument are left
                // unsupported rather than matched incorrectly.
                for (var i = index + 2; i < closeIndex; i++)
                {
                    if (selector[i].Kind is TokenKind.Comma
                        or TokenKind.DirectChildBracket
                        or TokenKind.Plus
                        or TokenKind.Tilde
                        or TokenKind.ColumnCombinator)
                    {
                        return false;
                    }

                    if (DataTable.HasGap(selector, i))
                    {
                        return false;
                    }
                }

                pseudo.Kind = PseudoClassKind.Not;
                pseudo.ArgumentStart = index + 2 - compoundStart;
                pseudo.ArgumentLength = closeIndex - (index + 2);
                next = closeIndex;
                return true;
            }

            if (id == RootId)
            {
                pseudo.Kind = PseudoClassKind.Root;
                return true;
            }

            if (id == EmptyId)
            {
                pseudo.Kind = PseudoClassKind.Empty;
                return true;
            }

            if (id == FirstChildId)
            {
                pseudo.Kind = PseudoClassKind.FirstChild;
                return true;
            }

            if (id == LastChildId)
            {
                pseudo.Kind = PseudoClassKind.LastChild;
                return true;
            }

            if (id == OnlyChildId)
            {
                pseudo.Kind = PseudoClassKind.OnlyChild;
                return true;
            }

            if (id == FirstOfTypeId)
            {
                pseudo.Kind = PseudoClassKind.FirstOfType;
                return true;
            }

            if (id == LastOfTypeId)
            {
                pseudo.Kind = PseudoClassKind.LastOfType;
                return true;
            }

            if (id == OnlyOfTypeId)
            {
                pseudo.Kind = PseudoClassKind.OnlyOfType;
                return true;
            }

            if (id == HoverId)
            {
                pseudo.Kind = PseudoClassKind.Hover;
                return true;
            }

            if (id == FocusId)
            {
                pseudo.Kind = PseudoClassKind.Focus;
                return true;
            }

            if (id == FocusVisibleId)
            {
                pseudo.Kind = PseudoClassKind.FocusVisible;
                return true;
            }

            if (id == FocusWithinId)
            {
                pseudo.Kind = PseudoClassKind.FocusWithin;
                return true;
            }

            if (id == ActiveId)
            {
                pseudo.Kind = PseudoClassKind.Active;
                return true;
            }

            if (id == CheckedId)
            {
                pseudo.Kind = PseudoClassKind.Checked;
                return true;
            }

            if (id == DisabledId)
            {
                pseudo.Kind = PseudoClassKind.Disabled;
                return true;
            }

            if (id == EnabledId)
            {
                pseudo.Kind = PseudoClassKind.Enabled;
                return true;
            }

            if (id == LinkId)
            {
                pseudo.Kind = PseudoClassKind.Link;
                return true;
            }

            if (id == AnyLinkId)
            {
                pseudo.Kind = PseudoClassKind.AnyLink;
                return true;
            }

            if (id == PlaceholderShownId)
            {
                pseudo.Kind = PseudoClassKind.PlaceholderShown;
                return true;
            }

            if (id == ReadOnlyId)
            {
                pseudo.Kind = PseudoClassKind.ReadOnly;
                return true;
            }

            if (id == ReadWriteId)
            {
                pseudo.Kind = PseudoClassKind.ReadWrite;
                return true;
            }

            if (id != NthChildId
                && id != NthLastChildId
                && id != NthOfTypeId
                && id != NthLastOfTypeId)
            {
                return false;
            }

            if (index + 1 >= end || selector[index + 1].Kind != TokenKind.LeftParenthesis)
            {
                return false;
            }

            var close = FindCloseParenthesis(selector, index + 1, end);

            if (close < 0)
            {
                return false;
            }

            Span<char> argument = stackalloc char[MaxAnBLength];
            var length = 0;

            for (var i = index + 2; i < close; i++)
            {
                var text = InternTable.GetString(selector[i].InternedId);

                if (text is null || length + text.Length > argument.Length)
                {
                    return false;
                }

                text.AsSpan().CopyTo(argument[length..]);
                length += text.Length;
            }

            if (!TryParseAnB(argument[..length], out var a, out var b))
            {
                return false;
            }

            pseudo.Kind = id == NthChildId
                ? PseudoClassKind.NthChild
                : id == NthLastChildId
                    ? PseudoClassKind.NthLastChild
                    : id == NthOfTypeId
                        ? PseudoClassKind.NthOfType
                        : PseudoClassKind.NthLastOfType;

            pseudo.A = a;
            pseudo.B = b;
            next = close;
            return true;
        }

        /// <summary>
        /// Parses the <c>An+B</c> microsyntax. Whitespace was already dropped by the
        /// tokenizer, so the text is a single run such as <c>2n+1</c>, <c>-n+3</c>,
        /// <c>odd</c> or <c>4</c>.
        /// </summary>
        private static bool TryParseAnB(ReadOnlySpan<char> text, out int a, out int b)
        {
            a = 0;
            b = 0;

            if (text.IsEmpty)
            {
                return false;
            }

            if (text.Equals("odd", StringComparison.OrdinalIgnoreCase))
            {
                a = 2;
                b = 1;
                return true;
            }

            if (text.Equals("even", StringComparison.OrdinalIgnoreCase))
            {
                a = 2;
                return true;
            }

            var i = 0;
            var sign = 1;

            if (text[i] is '+' or '-')
            {
                if (text[i] == '-')
                {
                    sign = -1;
                }

                i++;
            }

            var digitsStart = i;

            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
            }

            var hasDigits = i > digitsStart;

            if (i < text.Length && (text[i] == 'n' || text[i] == 'N'))
            {
                if (hasDigits)
                {
                    if (!int.TryParse(text[digitsStart..i], out var coefficient))
                    {
                        return false;
                    }

                    a = sign * coefficient;
                }
                else
                {
                    a = sign;
                }

                i++;

                if (i == text.Length)
                {
                    return true;
                }

                var bSign = 1;

                if (text[i] is '+' or '-')
                {
                    if (text[i] == '-')
                    {
                        bSign = -1;
                    }

                    i++;
                }
                else
                {
                    return false;
                }

                var bStart = i;

                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i == bStart || i != text.Length || !int.TryParse(text[bStart..i], out var offset))
                {
                    return false;
                }

                b = bSign * offset;
                return true;
            }

            if (!hasDigits || i != text.Length || !int.TryParse(text[digitsStart..i], out var value))
            {
                return false;
            }

            b = sign * value;
            return true;
        }

        /// <summary>
        /// Finds the closing parenthesis of the functional pseudo class at
        /// <paramref name="open"/>.
        /// </summary>
        /// <param name="selector">The full selector tokens.</param>
        /// <param name="open">Index of the opening parenthesis.</param>
        /// <param name="end">One past the compound's last token.</param>
        /// <returns>The index of the closing parenthesis, or -1 when it is missing or
        /// the argument contains a nested function.</returns>
        private static int FindCloseParenthesis(ReadOnlySpan<Token> selector, int open, int end)
        {
            for (var i = open + 1; i < end; i++)
            {
                switch (selector[i].Kind)
                {
                    case TokenKind.RightParenthesis:
                        return i;

                    // Nested functions are not supported; the caller marks the
                    // compound unsupported.
                    case TokenKind.LeftParenthesis:
                        return -1;
                }
            }

            return -1;
        }

        /// <summary>
        /// Advances past an unparsable bracket group so its contents are not
        /// misread as separate compounds.
        /// </summary>
        /// <param name="selector">The full selector tokens.</param>
        /// <param name="open">Index of the opening bracket.</param>
        /// <param name="end">One past the compound's last token.</param>
        /// <returns>The index of the closing bracket, or <paramref name="end"/>.</returns>
        private static int SkipToBracketEnd(ReadOnlySpan<Token> selector, int open, int end)
        {
            for (var i = open + 1; i < end; i++)
            {
                if (selector[i].Kind == TokenKind.RightSquareBracket)
                {
                    return i;
                }
            }

            return end;
        }

        private static bool IsClassDelimiter(ReadOnlySpan<Token> selector, int index)
        {
            return index >= 0
                && selector[index].Kind == TokenKind.Delimiter
                && selector[index].InternedId == ClassDelimiterId;
        }

        private static bool IsEquals(in Token token)
        {
            return token.Kind == TokenKind.Equals
                || (token.Kind == TokenKind.Delimiter && token.InternedId == EqualsDelimiterId);
        }

        /// <summary>
        /// Maps a raw token kind to a combinator, treating the descendant gap as a
        /// separate case handled by the caller.
        /// </summary>
        /// <param name="kind">The token kind to classify.</param>
        /// <returns>The combinator, or <see cref="Combinator.None"/>.</returns>
        private static Combinator ToCombinator(TokenKind kind)
        {
            return kind switch
            {
                TokenKind.DirectChildBracket => Combinator.Child,
                TokenKind.Plus => Combinator.AdjacentSibling,
                TokenKind.Tilde => Combinator.GeneralSibling,
                _ => Combinator.None
            };
        }

        /// <summary>
        /// Packs the three raw counts into one specificity value, saturating each
        /// component at <see cref="CountMask"/>.
        /// </summary>
        /// <param name="idCount">The number of id selectors.</param>
        /// <param name="classCount">The number of class-like selectors.</param>
        /// <param name="typeCount">The number of type-like selectors.</param>
        /// <returns>The packed specificity.</returns>
        /// <remarks>
        /// Saturation is unreachable for realistic selectors; it only prevents one
        /// huge component from spilling into the next.
        /// </remarks>
        private static int Pack(int idCount, int classCount, int typeCount)
        {
            return (Math.Min(idCount, CountMask) << IdShift)
                | (Math.Min(classCount, CountMask) << ClassShift)
                | Math.Min(typeCount, CountMask);
        }
    }
}
