using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Donkeh.Parser.Compilation;
using Donkeh.Parser.Objects;

namespace Donkeh.Parser
{
    public static partial class Tokenizer
    {
        /// <summary>
        /// Compiled exact-rule matcher built during type initialisation.
        /// Returns the underlying <see cref="TokenKind"/> value, or -1 on no match.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Assigned by the static constructor in <c>Rules/Rules.cs</c> after all
        /// exact rules have been registered. The field is mutable only so that
        /// initialisation can populate it; it must not be reassigned afterwards.
        /// The <see langword="null"/>-forgiving initializer merely keeps the
        /// compiler quiet until the type initializer runs.
        /// </para>
        /// <para>
        /// The matcher dispatches on the first character of the supplied span
        /// and then performs exact length and content comparisons. It does not
        /// trim, normalise or case-fold its input and returns -1 for an empty
        /// span or an unknown first character.
        /// </para>
        /// </remarks>
        /// <seealso cref="MinLengthByFirstChar"/>
        public static Func<ReadOnlySpan<char>, int> ExactMatch = null!;

        /// <summary>
        /// Smallest registered rule length for each possible first character.
        /// A candidate shorter than this can never match, which lets the lexer
        /// skip the (relatively expensive) compiled matcher on the very common
        /// single-letter identifiers.
        /// </summary>
        /// <remarks>
        /// Indexed by UTF-16 code unit (0 to <see cref="char.MaxValue"/>). An
        /// entry of <see cref="int.MaxValue"/> means that no exact rule starts
        /// with that character. The array is populated once, by the static
        /// constructor, and is read-only thereafter even though its elements
        /// are not deeply immutable.
        /// </remarks>
        /// <seealso cref="ExactMatch"/>
        public static readonly int[] MinLengthByFirstChar = new int[char.MaxValue + 1];

        /// <summary>
        /// Largest source length the packed <see cref="Token.Location"/> layout
        /// can address: the start index occupies 24 bits.
        /// </summary>
        private const int MaxSourceLength = 0x00FFFFFF; // Token.StartIndex is 24 bits.

        /// <summary>
        /// Tokenizes <paramref name="source"/> and returns the tokens in source order.
        /// Whitespace and comments are skipped, but every token keeps its original
        /// start and end index so callers can recover the exact source text.
        /// </summary>
        /// <param name="source">The CSS text to tokenize.</param>
        /// <returns>
        /// A new list containing one <see cref="Token"/> per lexical token, in
        /// source order. The list is never <see langword="null"/> but may be empty.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="source"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="source"/> is longer than 16,777,215
        /// characters, which the packed token layout cannot address.
        /// </exception>
        /// <example>
        /// <code>
        /// var tokens = Tokenizer.Parse("a { color: red; }");
        /// foreach (var token in tokens)
        /// {
        ///     var text = InternTable.GetString(token.InternedId);
        ///     Console.WriteLine($"{token.Kind}: {text}");
        /// }
        /// </code>
        /// </example>
        public static List<Token> Parse(string source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Parse(source.AsSpan());
        }

        /// <summary>
        /// Tokenizes <paramref name="source"/> and returns the tokens in source order.
        /// </summary>
        /// <param name="source">The CSS text to tokenize.</param>
        /// <returns>
        /// A new list containing one <see cref="Token"/> per lexical token, in
        /// source order. The list is never <see langword="null"/> but may be empty.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="source"/> is longer than 16,777,215
        /// characters, which the packed token layout cannot address.
        /// </exception>
        /// <remarks>
        /// The returned list is pre-sized from the source length to avoid
        /// repeated reallocation while tokenizing.
        /// </remarks>
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
        /// <param name="source">The CSS text to tokenize.</param>
        /// <param name="tokens">
        /// The destination list. Existing elements are preserved and new tokens
        /// are appended after them. The caller owns the list and should clear it
        /// between unrelated documents.
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// Thrown when <paramref name="tokens"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="source"/> is longer than 16,777,215
        /// characters, which the packed token layout cannot address.
        /// </exception>
        public static void Parse(ReadOnlySpan<char> source, List<Token> tokens)
        {
            ArgumentNullException.ThrowIfNull(tokens);
            EnsureSourceFitsInToken(source.Length);
            ParseCore(source, tokens);
        }

        /// <summary>
        /// Tokenizes the whole span without materialising a token list, consuming
        /// the span, and returns the number of tokens found. This is the
        /// list-free path used to measure raw tokenizer throughput.
        /// </summary>
        /// <param name="span">
        /// The CSS text to tokenize. It is reset to <see langword="default"/>
        /// before the method returns, leaving an empty span.
        /// </param>
        /// <returns>The number of tokens that were recognized.</returns>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="span"/> is longer than 16,777,215
        /// characters, which the packed token layout cannot address.
        /// </exception>
        /// <remarks>
        /// Tokens are still interned through <see cref="InternTable"/>, so this
        /// path avoids the token list but not the interning cost. It is intended
        /// for benchmarks and for callers that only need a token count.
        /// </remarks>
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

        /// <summary>
        /// Returns whether a colon starts a declaration rather than a pseudo-class.
        /// </summary>
        /// <param name="tokens">Tokens emitted so far.</param>
        /// <param name="count">Number of valid tokens.</param>
        /// <returns>
        /// <see langword="true"/> when the previous token is a known property name
        /// and the one before it opens a declaration block or follows a semicolon.
        /// </returns>
        /// <remarks>
        /// The tokenizer is context-free, so <c>background-position:right</c> would
        /// otherwise lex as the pseudo-class <c>:right</c> when the declaration
        /// omits whitespace after the colon. Selectors keep their pseudo-class
        /// tokens because a selector's type/class token is not preceded by
        /// <c>{</c> or <c>;</c>.
        /// </remarks>
        private static bool IsDeclarationColon(ReadOnlySpan<Token> tokens, int count)
        {
            if (count < 2 || tokens[count - 1].Kind != TokenKind.PropertyName)
            {
                return false;
            }

            return tokens[count - 2].Kind is TokenKind.LeftCurlyBrace or TokenKind.SemiColon;
        }

        /// <summary>
        /// Runs the lexer over <paramref name="source"/>, optionally writing
        /// tokens into <paramref name="tokens"/>.
        /// </summary>
        /// <param name="source">The CSS text to tokenize.</param>
        /// <param name="tokens">
        /// Destination list, or <see langword="null"/> to count tokens without
        /// storing them. When supplied, the list's count is temporarily grown to
        /// reserve capacity and then trimmed to the exact number of tokens added.
        /// </param>
        /// <returns>The number of tokens recognized.</returns>
        /// <remarks>
        /// The list is written through a <see cref="Span{T}"/> obtained from
        /// <see cref="CollectionsMarshal.AsSpan{T}(List{T})"/>; this avoids
        /// <see cref="List{T}.Add"/> on the hot path but requires the count to be
        /// restored before returning. Existing elements are never overwritten:
        /// the write window starts at the list's original count.
        /// </remarks>
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
                    Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
                    continue;
                }

                if (c == '-' && i + 3 <= length
                    && source[i + 1] == '-' && source[i + 2] == '>')
                {
                    i += 3;
                    Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
                    continue;
                }

                switch (c)
                {
                    case '"':
                    case '\'':
                        i = ScanString(source, i);
                        Emit(source, buffer, TokenKind.String, start, i, ref count);
                        break;

                    case '@':
                        i++;
                        if (i < length && IsNameStart(source[i]))
                        {
                            i = ScanName(source, i);
                            Emit(source, buffer, TokenKind.AtKeyword, start, i, ref count);
                        }
                        else
                        {
                            Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
                        }
                        break;

                    case '#':
                        if (i + 1 < length && (IsNameChar(source[i + 1]) || source[i + 1] == '\\'))
                        {
                            i = ScanName(source, i + 1);
                            Emit(source, buffer, TokenKind.Hash, start, i, ref count);
                        }
                        else
                        {
                            i++;
                            Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
                        }
                        break;

                    case ':':
                        if (IsDeclarationColon(buffer, count))
                        {
                            i++;
                            Emit(source, buffer, TokenKind.Colon, start, i, ref count);
                            break;
                        }

                        i = ScanPseudo(source, i, buffer, ref count);
                        break;

                    case ';':
                        i++;
                        Emit(source, buffer, TokenKind.SemiColon, start, i, ref count);
                        break;

                    case '{':
                        i++;
                        Emit(source, buffer, TokenKind.LeftCurlyBrace, start, i, ref count);
                        break;

                    case '}':
                        i++;
                        Emit(source, buffer, TokenKind.RightCurlyBrace, start, i, ref count);
                        break;

                    case '[':
                        i++;
                        Emit(source, buffer, TokenKind.LeftSquareBracket, start, i, ref count);
                        break;

                    case ']':
                        i++;
                        Emit(source, buffer, TokenKind.RightSquareBracket, start, i, ref count);
                        break;

                    case '(':
                        i++;
                        Emit(source, buffer, TokenKind.LeftParenthesis, start, i, ref count);
                        break;

                    case ')':
                        i++;
                        Emit(source, buffer, TokenKind.RightParenthesis, start, i, ref count);
                        break;

                    case '>':
                        i++;
                        Emit(source, buffer, TokenKind.DirectChildBracket, start, i, ref count);
                        break;

                    case ',':
                        i++;
                        Emit(source, buffer, TokenKind.Comma, start, i, ref count);
                        break;

                    case '+':
                        i++;
                        Emit(source, buffer, TokenKind.Plus, start, i, ref count);
                        break;

                    case '~':
                        i++;
                        Emit(source, buffer, TokenKind.Tilde, start, i, ref count);
                        break;

                    case '*':
                        i++;
                        Emit(source, buffer, TokenKind.Asterisk, start, i, ref count);
                        break;

                    case '|':
                        if (i + 1 < length && source[i + 1] == '|')
                        {
                            i += 2;
                            Emit(source, buffer, TokenKind.ColumnCombinator, start, i, ref count);
                        }
                        else
                        {
                            i++;
                            Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
                        }
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

                            Emit(source, buffer, kind, start, i, ref count);
                        }
                        else if (IsNumberStart(source, i))
                        {
                            i = ScanNumber(source, i);
                            i = EmitNumeric(source, start, i, buffer, ref count);
                        }
                        else
                        {
                            i++;
                            Emit(source, buffer, TokenKind.Delimiter, start, i, ref count);
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

        /// <summary>
        /// Classifies a scanned number as a percentage, a dimension or a plain
        /// number and emits the corresponding token.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="start">Index of the first character of the number.</param>
        /// <param name="i">Index one past the numeric part.</param>
        /// <param name="buffer">Destination token buffer.</param>
        /// <param name="count">Running token count; incremented by the emit.</param>
        /// <returns>Index one past the emitted token's text.</returns>
        /// <remarks>
        /// A trailing <c>%</c> produces <see cref="TokenKind.Percentage"/> and an
        /// immediately following name start produces
        /// <see cref="TokenKind.Dimension"/>; anything else is a
        /// <see cref="TokenKind.Number"/>.
        /// </remarks>
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
                Emit(source, buffer, TokenKind.Percentage, start, i, ref count);
                return i;
            }

            if (i < source.Length && IsNameStart(source[i]))
            {
                i = ScanName(source, i);
                Emit(source, buffer, TokenKind.Dimension, start, i, ref count);
                return i;
            }

            Emit(source, buffer, TokenKind.Number, start, i, ref count);
            return i;
        }

        /// <summary>
        /// Determines the token kind for a scanned identifier, consulting the
        /// compiled exact-rule matcher.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="start">Index of the identifier's first character.</param>
        /// <param name="end">Index one past the identifier's last character.</param>
        /// <returns>
        /// <see cref="TokenKind.Function"/> when the name is immediately followed
        /// by <c>'('</c>; otherwise the registered kind, or
        /// <see cref="TokenKind.Identifier"/> when no rule matches.
        /// </returns>
        /// <remarks>
        /// The matcher is skipped when the candidate is shorter than the smallest
        /// registered rule with the same first character, which makes the common
        /// single-character identifier case cheap.
        /// </remarks>
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

        /// <summary>
        /// Scans a colon-prefixed pseudo-class or pseudo-element, emitting either
        /// a combined token or a standalone colon token.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the first <c>':'</c>.</param>
        /// <param name="buffer">Destination token buffer.</param>
        /// <param name="count">Running token count; incremented by each emit.</param>
        /// <returns>Index one past the characters consumed.</returns>
        /// <remarks>
        /// <para>
        /// A known <c>:name</c> produces <see cref="TokenKind.PseudoClass"/>
        /// covering the whole construct. A known <c>::name</c> produces
        /// <see cref="TokenKind.PseudoElement"/>.
        /// </para>
        /// <para>
        /// When the name is unknown the method emits only
        /// <see cref="TokenKind.Colon"/> or
        /// <see cref="TokenKind.PseudoElementColon"/> and leaves the identifier
        /// for the main lexer loop. A lone <c>::</c> is treated the same way.
        /// </para>
        /// </remarks>
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
                        Emit(source, buffer, TokenKind.PseudoElement, start, end, ref count);
                        return end;
                    }

                    // '::' on its own, or an unknown pseudo-element. Emit the
                    // colon pair and let the identifier scanner handle the name.
                    Emit(source, buffer, TokenKind.PseudoElementColon, start, start + 2, ref count);
                    return start + 2;
                }

                if (kind == (int)TokenKind.PseudoClass)
                {
                    Emit(source, buffer, TokenKind.PseudoClass, start, end, ref count);
                    return end;
                }

                Emit(source, buffer, TokenKind.Colon, start, start + 1, ref count);
                return start + 1;
            }

            if (isElement)
            {
                Emit(source, buffer, TokenKind.PseudoElementColon, start, start + 2, ref count);
                return start + 2;
            }

            Emit(source, buffer, TokenKind.Colon, start, start + 1, ref count);
            return start + 1;
        }

        /// <summary>
        /// Emits one logical token, splitting text longer than 255 characters
        /// into consecutive chunks that share the same token kind.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="buffer">Destination token buffer.</param>
        /// <param name="kind">The kind assigned to every chunk.</param>
        /// <param name="start">Index of the token's first character.</param>
        /// <param name="end">Index one past the token's last character.</param>
        /// <param name="count">Running token count; incremented once per chunk.</param>
        /// <remarks>
        /// The packed <see cref="Token"/> layout stores the length in a single
        /// byte, so a 255+ character literal cannot be represented in one token.
        /// Chunking keeps the original text recoverable by concatenating the
        /// chunk text in order.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Emit(
            ReadOnlySpan<char> source,
            Span<Token> buffer,
            TokenKind kind,
            int start,
            int end,
            ref int count)
        {
            var length = end - start;

            // The packed Token layout stores the length in a single byte, so a
            // token can never be longer than 255 characters. Rather than throwing
            // on the occasional long string (for example a data: URI), split it
            // into consecutive chunks. Downstream code concatenates the chunk
            // text, so the reconstructed value is identical to the original.
            if (length <= byte.MaxValue)
            {
                EmitOne(source, buffer, kind, start, end, ref count);
                return;
            }

            var pos = start;
            while (pos < end)
            {
                var chunkEnd = Math.Min(pos + byte.MaxValue, end);
                EmitOne(source, buffer, kind, pos, chunkEnd, ref count);
                pos = chunkEnd;
            }
        }

        /// <summary>
        /// Writes a single chunk of at most 255 characters to the buffer and
        /// advances <paramref name="count"/>.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="buffer">Destination token buffer.</param>
        /// <param name="kind">The token kind to assign.</param>
        /// <param name="start">Index of the chunk's first character.</param>
        /// <param name="end">Index one past the chunk's last character.</param>
        /// <param name="count">
        /// Running token count. It is incremented even when the buffer is full, so
        /// the caller can detect overflow and grow the buffer; no token is written
        /// in that case.
        /// </param>
        /// <remarks>
        /// The packed location is built directly here rather than through
        /// <see cref="Token.StartIndex"/> and <see cref="Token.EndIndex"/>, which
        /// would each unpack and repack the field. The token's text is interned
        /// through <see cref="InternTable.Intern(ReadOnlySpan{char})"/>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EmitOne(
            ReadOnlySpan<char> source,
            Span<Token> buffer,
            TokenKind kind,
            int start,
            int end,
            ref int count)
        {
            var length = end - start;

            // Directly pack Kind and Location. This skips the StartIndex /
            // EndIndex property setters, each of which would unpack and repack
            // the location field.
            if ((uint)count < (uint)buffer.Length)
            {
                // Intern the token's source text and keep the id on the token.
                var internedId = InternTable.Intern(source.Slice(start, length));

                buffer[count] = new Token
                {
                    Kind = kind,
                    InternedId = internedId,
                    Location = (length << 24) | (start & 0x00FFFFFF)
                };
            }

            count++;
        }

        /// <summary>
        /// Skips a <c>/* ... */</c> comment and returns the index one past its
        /// closing delimiter.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the opening <c>'/'</c>.</param>
        /// <returns>
        /// Index one past <c>*&#47;</c>, or the source length when the comment is
        /// unterminated.
        /// </returns>
        /// <remarks>Comments are never emitted as tokens.</remarks>
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

        /// <summary>
        /// Scans a single- or double-quoted CSS string, honouring backslash
        /// escapes.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the opening quote.</param>
        /// <returns>
        /// Index one past the closing quote, or the index of the terminating
        /// newline when the string is malformed and unterminated on its line.
        /// </returns>
        /// <remarks>
        /// An unescaped newline, carriage return or form feed ends a malformed
        /// string; the offending character is not consumed.
        /// </remarks>
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

        /// <summary>
        /// Scans a CSS identifier, consuming name characters and escapes.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the first character to inspect.</param>
        /// <returns>Index one past the last character of the identifier.</returns>
        /// <remarks>
        /// The scan stops at the first character that is neither a name
        /// character nor the start of an escape. It does not validate that the
        /// first character is a legal name start.
        /// </remarks>
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
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the backslash.</param>
        /// <returns>
        /// Index one past the escape. At most six hexadecimal digits and one
        /// optional trailing whitespace character are consumed.
        /// </returns>
        /// <remarks>
        /// An escaped newline is a line continuation and consumes both the
        /// backslash and the newline (including a full <c>CRLF</c> pair). A
        /// trailing backslash at the end of the source consumes only itself.
        /// </remarks>
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

        /// <summary>
        /// Scans the numeric part of a CSS number: an optional sign, digits, an
        /// optional fractional part and an optional exponent.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the first character of the number.</param>
        /// <returns>Index one past the last numeric character.</returns>
        /// <remarks>
        /// A decimal point is only consumed when followed by a digit, and an
        /// exponent is only consumed when it is followed by an optional sign and
        /// at least one digit.
        /// </remarks>
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

        /// <summary>
        /// Determines whether a number can start at <paramref name="i"/>.
        /// </summary>
        /// <param name="source">The full source text.</param>
        /// <param name="i">Index of the candidate first character.</param>
        /// <returns>
        /// <see langword="true"/> for a digit, for <c>'.'</c> followed by a
        /// digit, or for a sign followed by a digit or <c>".digit"</c>.
        /// </returns>
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

        /// <summary>
        /// Tests for the CSS whitespace characters: space, tab, line feed,
        /// carriage return and form feed.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns><see langword="true"/> when <paramref name="c"/> is whitespace.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsWhitespace(char c)
        {
            return c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f';
        }

        /// <summary>
        /// Tests whether <paramref name="c"/> is an ASCII decimal digit.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns><see langword="true"/> for <c>'0'</c> through <c>'9'</c>.</returns>
        /// <remarks>
        /// The subtraction trick gives a single unsigned comparison and is the
        /// reason this is aggressively inlined.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsDigit(char c)
        {
            return (uint)(c - '0') < 10;
        }

        /// <summary>
        /// Tests whether <paramref name="c"/> is an ASCII hexadecimal digit.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns>
        /// <see langword="true"/> for <c>0</c>–<c>9</c>, <c>a</c>–<c>f</c> and
        /// <c>A</c>–<c>F</c>.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsHex(char c)
        {
            return IsDigit(c)
                || (uint)(c - 'a') < 6
                || (uint)(c - 'A') < 6;
        }

        /// <summary>
        /// Tests whether <paramref name="c"/> can start a CSS identifier.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns>
        /// <see langword="true"/> for ASCII letters, <c>'_'</c>, <c>'-'</c>,
        /// non-ASCII characters (<c>&gt;= 0x80</c>) and the backslash escape
        /// introducer.
        /// </returns>
        /// <remarks>
        /// The CSS specification also allows two hyphens or an escape to start a
        /// name; this predicate is deliberately permissive and lets the scanner
        /// resolve those cases.
        /// </remarks>
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

        /// <summary>
        /// Tests whether <paramref name="c"/> can appear inside a CSS identifier.
        /// </summary>
        /// <param name="c">The character to test.</param>
        /// <returns>
        /// <see langword="true"/> for ASCII letters, digits, <c>'_'</c>,
        /// <c>'-'</c> and non-ASCII characters (<c>&gt;= 0x80</c>).
        /// </returns>
        /// <remarks>
        /// Escapes are handled separately by <see cref="ScanName"/>, so a
        /// backslash is intentionally not a name character here.
        /// </remarks>
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

        /// <summary>
        /// Validates that a source length can be addressed by the 24-bit start
        /// index stored in <see cref="Token.Location"/>.
        /// </summary>
        /// <param name="length">The source length in UTF-16 code units.</param>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when <paramref name="length"/> exceeds
        /// <see cref="MaxSourceLength"/>.
        /// </exception>
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
