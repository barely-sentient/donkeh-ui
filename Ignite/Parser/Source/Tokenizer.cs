using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Ignite.Parser.Compilation;
using Ignite.Parser.Objects;

namespace Ignite.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Compiled exact-rule matcher built during type initialisation.
        /// Returns the underlying <see cref="TokenKind"/> value, or -1 on no match.
        /// </summary>
        public static Func<ReadOnlySpan<char>, int> ExactMatch = null!;

        /// <summary>
        /// Smallest registered rule length for each possible first character.
        /// A candidate shorter than this can never match, which lets the lexer
        /// skip the (relatively expensive) compiled matcher on the very common
        /// single-letter identifiers.
        /// </summary>
        public static readonly int[] MinLengthByFirstChar = new int[char.MaxValue + 1];

        private const int MaxSourceLength = 0x00FFFFFF; // Token.StartIndex is 24 bits.

        /// <summary>
        /// Tokenizes <paramref name="source"/> and returns the tokens in source order.
        /// Whitespace and comments are skipped, but every token keeps its original
        /// start and end index so callers can recover the exact source text.
        /// </summary>
        public static List<Token> Parse(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Parse(source.AsSpan());
        }

        /// <summary>
        /// Tokenizes <paramref name="source"/> and returns the tokens in source order.
        /// </summary>
        public static List<Token> Parse(ReadOnlySpan<char> source)
        {
            EnsureSourceFitsInToken(source.Length);

            var tokens = new List<Token>(InitialCapacity(source.Length));
            ParseCore(source, tokens);
            return tokens;
        }

        /// <summary>
        /// Tokenizes <paramref name="source"/>, appending every token to
        /// <paramref name="tokens"/>. The list is not cleared first, so it can be
        /// reused across calls to avoid allocating a new token buffer.
        /// </summary>
        public static void Parse(ReadOnlySpan<char> source, List<Token> tokens)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            EnsureSourceFitsInToken(source.Length);
            ParseCore(source, tokens);
        }

        /// <summary>
        /// Tokenizes the whole span without materialising a token list, consuming
        /// the span, and returns the number of tokens found. This is the
        /// allocation-free path used to measure raw tokenizer throughput.
        /// </summary>
        public static int Parse(ref ReadOnlySpan<char> span)
        {
            EnsureSourceFitsInToken(span.Length);
            var count = ParseCore(span, tokens: null);
            span = default;
            return count;
        }

        /// <summary>
        /// Rough token count for a CSS document: around 2.5 characters per token.
        /// Reserving this up front lets the hot loop write straight into the
        /// list's backing array without ever calling <see cref="List{T}.Add"/>.
        /// </summary>
        private static int InitialCapacity(int sourceLength)
        {
            return Math.Max(16, sourceLength / 2 + 16);
        }

        private static int ParseCore(ReadOnlySpan<char> source, List<Token>? tokens)
        {
            // When a list is supplied, reserve the whole estimated token buffer
            // once and write into it by index. The list's count is trimmed back
            // to the real token count at the end.
            var offset = 0;
            var buffer = default(Span<Token>);

            if (tokens is not null)
            {
                offset = tokens.Count;
                CollectionsMarshal.SetCount(tokens, offset + InitialCapacity(source.Length));
                buffer = CollectionsMarshal.AsSpan(tokens).Slice(offset);
            }

            var count = 0;
            var i = 0;
            var length = source.Length;

            while (i < length)
            {
                // At most one token is emitted per iteration, so this keeps the
                // fast path to a single predictable compare.
                if (count == buffer.Length && tokens is not null)
                {
                    CollectionsMarshal.SetCount(
                        tokens,
                        offset + Math.Max(16, buffer.Length + (buffer.Length >> 1)));
                    buffer = CollectionsMarshal.AsSpan(tokens).Slice(offset);
                }

                var c = source[i];

                if (IsWhitespace(c))
                {
                    i++;
                    continue;
                }

                // Comments are not tokens; they simply separate neighbours.
                if (c == '/' && i + 1 < length && source[i + 1] == '*')
                {
                    i = SkipComment(source, i);
                    continue;
                }

                var start = i;

                // CDO / CDC tokens (<!-- and -->) used to hide CSS inside HTML
                // comments. Emit them as a single delimiter rather than splitting.
                if (c == '<' && i + 4 <= length
                    && source[i + 1] == '!' && source[i + 2] == '-' && source[i + 3] == '-')
                {
                    i += 4;
                    Emit(buffer, TokenKind.Delimiter, start, i, ref count);
                    continue;
                }

                if (c == '-' && i + 3 <= length
                    && source[i + 1] == '-' && source[i + 2] == '>')
                {
                    i += 3;
                    Emit(buffer, TokenKind.Delimiter, start, i, ref count);
                    continue;
                }

                switch (c)
                {
                    case '"':
                    case '\'':
                        i = ScanString(source, i);
                        Emit(buffer, TokenKind.String, start, i, ref count);
                        break;

                    case '@':
                        i++;
                        if (i < length && IsNameStart(source[i]))
                        {
                            i = ScanName(source, i);
                            Emit(buffer, TokenKind.AtKeyword, start, i, ref count);
                        }
                        else
                        {
                            Emit(buffer, TokenKind.Delimiter, start, i, ref count);
                        }
                        break;

                    case '#':
                        if (i + 1 < length && (IsNameChar(source[i + 1]) || source[i + 1] == '\\'))
                        {
                            i = ScanName(source, i + 1);
                            Emit(buffer, TokenKind.Hash, start, i, ref count);
                        }
                        else
                        {
                            i++;
                            Emit(buffer, TokenKind.Delimiter, start, i, ref count);
                        }
                        break;

                    case ':':
                        i = ScanPseudo(source, i, buffer, ref count);
                        break;

                    case ';':
                        i++;
                        Emit(buffer, TokenKind.SemiColon, start, i, ref count);
                        break;

                    case '{':
                        i++;
                        Emit(buffer, TokenKind.LeftCurlyBrace, start, i, ref count);
                        break;

                    case '}':
                        i++;
                        Emit(buffer, TokenKind.RightCurlyBrace, start, i, ref count);
                        break;

                    case '[':
                        i++;
                        Emit(buffer, TokenKind.LeftSquareBracket, start, i, ref count);
                        break;

                    case ']':
                        i++;
                        Emit(buffer, TokenKind.RightSquareBracket, start, i, ref count);
                        break;

                    case '(':
                        i++;
                        Emit(buffer, TokenKind.LeftParenthesis, start, i, ref count);
                        break;

                    case ')':
                        i++;
                        Emit(buffer, TokenKind.RightParenthesis, start, i, ref count);
                        break;

                    case '>':
                        i++;
                        Emit(buffer, TokenKind.DirectChildBracket, start, i, ref count);
                        break;

                    case ',':
                        i++;
                        Emit(buffer, TokenKind.Comma, start, i, ref count);
                        break;

                    case '+':
                        i++;
                        Emit(buffer, TokenKind.Plus, start, i, ref count);
                        break;

                    case '~':
                        i++;
                        Emit(buffer, TokenKind.Tilde, start, i, ref count);
                        break;

                    case '*':
                        i++;
                        Emit(buffer, TokenKind.Asterisk, start, i, ref count);
                        break;

                    case '=':
                        i++;
                        Emit(buffer, TokenKind.Equals, start, i, ref count);
                        break;

                    default:
                        if (IsNameStart(c) && !IsDigitFollowsSign(source, i))
                        {
                            i = ScanName(source, i);

                            // A lone '-' (for example in calc(100% - 1px)) is a
                            // delimiter, not an identifier.
                            var kind = i == start + 1 && c == '-'
                                ? TokenKind.Delimiter
                                : ClassifyName(source, start, i);

                            Emit(buffer, kind, start, i, ref count);
                        }
                        else if (IsNumberStart(source, i))
                        {
                            i = ScanNumber(source, i);
                            i = EmitNumeric(source, start, i, buffer, ref count);
                        }
                        else
                        {
                            i++;
                            Emit(buffer, TokenKind.Delimiter, start, i, ref count);
                        }
                        break;
                }
            }

            if (tokens is not null)
            {
                CollectionsMarshal.SetCount(tokens, offset + count);
            }

            return count;
        }

        /// <summary>
        /// A '+' or '-' directly followed by a digit starts a number, not an
        /// identifier, so the name path must yield to the numeric path.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigitFollowsSign(ReadOnlySpan<char> source, int i)
        {
            var c = source[i];
            if (c != '-' && c != '+')
            {
                return false;
            }

            if (i + 1 >= source.Length)
            {
                return false;
            }

            if (IsDigit(source[i + 1]))
            {
                return true;
            }

            // '-.5' is a number too.
            return i + 2 < source.Length && source[i + 1] == '.' && IsDigit(source[i + 2]);
        }

        private static int EmitNumeric(
            ReadOnlySpan<char> source,
            int start,
            int i,
            Span<Token> buffer,
            ref int count)
        {
            if (i < source.Length && source[i] == '%')
            {
                i++;
                Emit(buffer, TokenKind.Percentage, start, i, ref count);
                return i;
            }

            if (i < source.Length && IsNameStart(source[i]))
            {
                i = ScanName(source, i);
                Emit(buffer, TokenKind.Dimension, start, i, ref count);
                return i;
            }

            Emit(buffer, TokenKind.Number, start, i, ref count);
            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static TokenKind ClassifyName(ReadOnlySpan<char> source, int start, int end)
        {
            // Any identifier immediately followed by '(' is a function. This
            // also disambiguates names that are both properties and functions,
            // such as 'color', 'rotate' or 'scale'.
            if (end < source.Length && source[end] == '(')
            {
                return TokenKind.Function;
            }

            // Skip the matcher entirely when the candidate is shorter than any
            // registered rule with the same first character.
            var minLen = MinLengthByFirstChar[(int)source[start]];
            var kind = (end - start) < minLen
                ? -1
                : ExactMatch(source.Slice(start, end - start));
            if (kind == -1 || kind == (int)TokenKind.Function)
            {
                // A function name used without '(' is just an identifier.
                return TokenKind.Identifier;
            }

            return (TokenKind)kind;
        }

        private static int ScanPseudo(ReadOnlySpan<char> source, int i, Span<Token> buffer, ref int count)
        {
            var start = i;
            var length = source.Length;
            var nameStart = i + 1;
            var isElement = nameStart < length && source[nameStart] == ':';

            if (isElement)
            {
                nameStart++;
            }

            if (nameStart < length && IsNameStart(source[nameStart]))
            {
                var end = ScanName(source, nameStart);
                var kind = (end - start) < MinLengthByFirstChar[(int)source[start]]
                    ? -1
                    : ExactMatch(source.Slice(start, end - start));

                if (isElement)
                {
                    if (kind == (int)TokenKind.PseudoElement)
                    {
                        Emit(buffer, TokenKind.PseudoElement, start, end, ref count);
                        return end;
                    }

                    // '::' on its own, or an unknown pseudo-element. Emit the
                    // colon pair and let the identifier scanner handle the name.
                    Emit(buffer, TokenKind.PseudoElementColon, start, start + 2, ref count);
                    return start + 2;
                }

                if (kind == (int)TokenKind.PseudoClass)
                {
                    Emit(buffer, TokenKind.PseudoClass, start, end, ref count);
                    return end;
                }

                Emit(buffer, TokenKind.Colon, start, start + 1, ref count);
                return start + 1;
            }

            if (isElement)
            {
                Emit(buffer, TokenKind.PseudoElementColon, start, start + 2, ref count);
                return start + 2;
            }

            Emit(buffer, TokenKind.Colon, start, start + 1, ref count);
            return start + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Emit(
            Span<Token> buffer,
            TokenKind kind,
            int start,
            int end,
            ref int count)
        {
            var length = end - start;

            if ((uint)length > byte.MaxValue)
            {
                ThrowTokenTooLong(start, length);
            }

            // Directly pack Kind and Location. This skips the StartIndex /
            // EndIndex property setters, each of which would unpack and repack
            // the location field.
            if ((uint)count < (uint)buffer.Length)
            {
                buffer[count] = new Token
                {
                    Kind = kind,
                    Location = (length << 24) | (start & 0x00FFFFFF)
                };
            }

            count++;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTokenTooLong(int start, int length)
        {
            throw new InvalidOperationException(
                $"Token at index {start} is {length} characters long; " +
                "the packed Token layout supports at most 255 characters per token.");
        }

        private static int SkipComment(ReadOnlySpan<char> source, int i)
        {
            i += 2;
            var length = source.Length;

            while (i + 1 < length)
            {
                if (source[i] == '*' && source[i + 1] == '/')
                {
                    return i + 2;
                }

                i++;
            }

            return length;
        }

        private static int ScanString(ReadOnlySpan<char> source, int i)
        {
            var quote = source[i];
            i++;
            var length = source.Length;

            while (i < length)
            {
                var c = source[i];

                if (c == quote)
                {
                    return i + 1;
                }

                if (c == '\\')
                {
                    i = ScanEscape(source, i);
                    continue;
                }

                // An unescaped newline terminates a malformed string.
                if (c == '\n' || c == '\r' || c == '\f')
                {
                    return i;
                }

                i++;
            }

            return i;
        }

        private static int ScanName(ReadOnlySpan<char> source, int i)
        {
            var length = source.Length;

            while (i < length)
            {
                var c = source[i];

                if (IsNameChar(c))
                {
                    i++;
                    continue;
                }

                if (c == '\\')
                {
                    i = ScanEscape(source, i);
                    continue;
                }

                break;
            }

            return i;
        }

        /// <summary>
        /// Consumes a CSS escape sequence. <paramref name="i"/> points at the
        /// backslash; the returned index is one past the escape.
        /// </summary>
        private static int ScanEscape(ReadOnlySpan<char> source, int i)
        {
            i++;
            var length = source.Length;
            if (i >= length)
            {
                return i;
            }

            // An escaped newline is a line continuation: both the backslash and
            // the newline are removed from the resulting token.
            if (source[i] == '\n' || source[i] == '\r' || source[i] == '\f')
            {
                if (source[i] == '\r' && i + 1 < length && source[i + 1] == '\n')
                {
                    i++;
                }

                return i + 1;
            }

            if (!IsHex(source[i]))
            {
                return i + 1;
            }

            var digits = 0;
            while (i < length && digits < 6 && IsHex(source[i]))
            {
                i++;
                digits++;
            }

            // A single trailing whitespace character belongs to the escape.
            if (i < length && IsWhitespace(source[i]))
            {
                i++;
            }

            return i;
        }

        private static int ScanNumber(ReadOnlySpan<char> source, int i)
        {
            var length = source.Length;

            if (i < length && (source[i] == '+' || source[i] == '-'))
            {
                i++;
            }

            while (i < length && IsDigit(source[i]))
            {
                i++;
            }

            if (i + 1 < length && source[i] == '.' && IsDigit(source[i + 1]))
            {
                i++;
                while (i < length && IsDigit(source[i]))
                {
                    i++;
                }
            }

            if (i < length && (source[i] == 'e' || source[i] == 'E'))
            {
                var exponent = i + 1;
                if (exponent < length && (source[exponent] == '+' || source[exponent] == '-'))
                {
                    exponent++;
                }

                if (exponent < length && IsDigit(source[exponent]))
                {
                    i = exponent + 1;
                    while (i < length && IsDigit(source[i]))
                    {
                        i++;
                    }
                }
            }

            return i;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNumberStart(ReadOnlySpan<char> source, int i)
        {
            var c = source[i];

            if (IsDigit(c))
            {
                return true;
            }

            if (c == '.')
            {
                return i + 1 < source.Length && IsDigit(source[i + 1]);
            }

            if (c == '+' || c == '-')
            {
                if (i + 1 < source.Length && IsDigit(source[i + 1]))
                {
                    return true;
                }

                return i + 2 < source.Length && source[i + 1] == '.' && IsDigit(source[i + 2]);
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(char c)
        {
            return (uint)(c - '0') < 10;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHex(char c)
        {
            return IsDigit(c)
                || (uint)(c - 'a') < 6
                || (uint)(c - 'A') < 6;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNameStart(char c)
        {
            return (uint)(c - 'a') < 26
                || (uint)(c - 'A') < 26
                || c == '_'
                || c == '-'
                || c >= 0x80
                || c == '\\';
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsNameChar(char c)
        {
            // Escapes are handled separately by ScanName, so '\' is not a
            // name character here.
            return (uint)(c - 'a') < 26
                || (uint)(c - 'A') < 26
                || IsDigit(c)
                || c == '_'
                || c == '-'
                || c >= 0x80;
        }

        private static void EnsureSourceFitsInToken(int length)
        {
            if (length > MaxSourceLength)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(length),
                    $"The packed Token layout supports sources up to {MaxSourceLength} characters.");
            }
        }
    }
}
