using Ignite.Parser;
using Ignite.Parser.Objects;

namespace Ignite.Data.Dom
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
    /// Pseudo classes and pseudo elements that need state or generated content
    /// (<c>:hover</c>, <c>::before</c>, <c>:not()</c>, ...) mark the compound
    /// unsupported, so the selector is never matched rather than matched wrongly.
    /// </remarks>
    internal static class SelectorAnalysis
    {
        internal const int CountBits = 10;
        internal const int CountMask = (1 << CountBits) - 1;
        internal const int ClassShift = CountBits;
        internal const int IdShift = CountBits * 2;

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

        private const int MaxAnBLength = 32;

        /// <summary>
        /// Analyses <paramref name="selector"/>, appending its compounds, attribute
        /// filters and pseudo classes to the supplied lists, and returning the
        /// packed specificity.
        /// </summary>
        internal static int Analyze(
            ReadOnlySpan<Token> selector,
            List<CompoundRecord> compounds,
            List<AttributeFilter> filters,
            List<PseudoRecord> pseudos,
            out bool matchable)
        {
            var idCount = 0;
            var classCount = 0;
            var typeCount = 0;

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
                            ref idCount, ref classCount, ref typeCount, ref supported);

                        leading = combinator;
                        compoundStart = i + 1;
                        continue;
                    }

                    if (i > compoundStart && DataTable.HasGap(selector, i))
                    {
                        AddCompound(
                            selector, compoundStart, i, leading,
                            compounds, filters, pseudos,
                            ref idCount, ref classCount, ref typeCount, ref supported);

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
                ref idCount, ref classCount, ref typeCount, ref supported);

            if (bracketDepth != 0 || parenDepth != 0)
            {
                supported = false;
            }

            matchable = supported && compounds.Count > firstCompound;
            return Pack(idCount, classCount, typeCount);
        }

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
            ref bool supported)
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

                        if (TryParsePseudoClass(selector, i, end, out var pseudo, out var next))
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
                        compoundSupported = false;
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

        private static bool TryParsePseudoClass(
            ReadOnlySpan<Token> selector,
            int index,
            int end,
            out PseudoRecord pseudo,
            out int next)
        {
            pseudo = default;
            next = index;

            var id = selector[index].InternedId;

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

        private static int Pack(int idCount, int classCount, int typeCount)
        {
            return (Math.Min(idCount, CountMask) << IdShift)
                | (Math.Min(classCount, CountMask) << ClassShift)
                | Math.Min(typeCount, CountMask);
        }
    }
}
