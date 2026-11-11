namespace Ignite.LayoutLab;

/// <summary>Lexical categories produced by <see cref="CssValueTokenizer"/>.</summary>
/// <remarks>
/// The set is deliberately small and covers only value syntax: it has no
/// selector, at-rule or declaration tokens. <see cref="Function"/> and
/// <see cref="Word"/> are distinguished by whether an opening parenthesis
/// followed the identifier.
/// </remarks>
internal enum CssValueTokenKind : byte
{
    /// <summary>An identifier such as <c>auto</c> or <c>none</c>, with no following parenthesis.</summary>
    Word,
    /// <summary>A unitless numeric literal.</summary>
    Number,
    /// <summary>A number followed by an identifier unit, such as <c>12px</c>.</summary>
    Dimension,
    /// <summary>A number followed by <c>%</c>.</summary>
    Percentage,
    /// <summary>A <c>#</c> followed by name characters, such as a hex colour.</summary>
    Hash,
    /// <summary>A single- or double-quoted string, including its quotes.</summary>
    String,
    /// <summary>An identifier immediately followed by <c>(</c>, such as <c>calc</c>.</summary>
    Function,
    /// <summary>A <c>,</c> separator.</summary>
    Comma,
    /// <summary>A <c>/</c> separator.</summary>
    Slash,
    /// <summary>An arithmetic operator: <c>+</c>, <c>-</c> or <c>*</c>.</summary>
    Operator,
    /// <summary>A <c>(</c> that was not consumed as part of a function token.</summary>
    OpenParen,
    /// <summary>A <c>)</c>.</summary>
    CloseParen,
    /// <summary>
    /// A <c>:</c> token. Declared for completeness, but the tokenizer currently
    /// emits <c>:</c> as a generic <see cref="Delimiter"/>.
    /// </summary>
    Colon,
    /// <summary>Any other single character, including <c>!</c>, <c>;</c> and unhandled symbols.</summary>
    Delimiter
}

/// <summary>
/// A single token produced by <see cref="CssValueTokenizer"/>.
/// </summary>
/// <remarks>
/// Tokens do not own their text: <see cref="Start"/> and <see cref="Length"/>
/// index into the original source string, which must be supplied again to
/// <see cref="CssValueTokenizer.GetText(string, in CssValueToken)"/> or
/// <see cref="CssValueTokenizer.GetSpan(string, in CssValueToken)"/>. This keeps
/// token extraction allocation-free; only the token array and the list used to
/// build it are allocated.
/// </remarks>
internal readonly struct CssValueToken
{
    /// <summary>Initialises a token.</summary>
    /// <param name="kind">Lexical category.</param>
    /// <param name="start">Zero-based index of the first character in the source.</param>
    /// <param name="length">Number of characters in the token, including sign, unit and quotes.</param>
    /// <param name="number">Parsed numeric value for numeric kinds; zero otherwise.</param>
    /// <param name="unitStart">Index of the first unit character for a dimension; zero otherwise.</param>
    /// <param name="unitLength">Length of the unit text for a dimension; zero otherwise.</param>
    public CssValueToken(
        CssValueTokenKind kind,
        int start,
        int length,
        float number = 0,
        int unitStart = 0,
        int unitLength = 0)
    {
        Kind = kind;
        Start = start;
        Length = length;
        Number = number;
        UnitStart = unitStart;
        UnitLength = unitLength;
    }

    /// <summary>Gets the lexical category.</summary>
    public CssValueTokenKind Kind { get; }

    /// <summary>Gets the zero-based index of the first character in the source text.</summary>
    public int Start { get; }

    /// <summary>Gets the token length in characters.</summary>
    public int Length { get; }

    /// <summary>
    /// Gets the parsed numeric value for <see cref="CssValueTokenKind.Number"/>,
    /// dimensions and percentages.
    /// </summary>
    public float Number { get; }

    /// <summary>Gets the index of the unit text for a dimension, or zero when there is none.</summary>
    public int UnitStart { get; }

    /// <summary>Gets the unit text length for a dimension, or zero when there is none.</summary>
    public int UnitLength { get; }
}

/// <summary>
/// A tiny, allocation-free-at-token-level CSS value tokenizer. It only needs to
/// understand value syntax, not selectors, so it is much smaller than the main
/// stylesheet tokenizer.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Tokenize(string)"/> returns an array, so the tokenizer itself
/// allocates once per call plus growth of its internal list; extracting text
/// or units through the span helpers does not allocate. Static state is
/// immutable and the type is thread safe.
/// </para>
/// <para>
/// Comments are skipped. Function identifiers are emitted without their
/// opening parenthesis, which is consumed as part of the function token; the
/// matching <see cref="CssValueTokenKind.CloseParen"/> is emitted normally.
/// <c>url(</c> is emitted as a <see cref="CssValueTokenKind.Word"/> followed by
/// a separate opening parenthesis rather than as a function token.
/// </para>
/// </remarks>
internal static class CssValueTokenizer
{
    /// <summary>Splits a CSS declaration value into tokens.</summary>
    /// <param name="source">Value text, already stripped of the property name and braces.</param>
    /// <returns>
    /// The tokens in source order, or an empty array when
    /// <paramref name="source"/> is null or empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Whitespace separates tokens but is not itself emitted. A <c>+</c> or
    /// <c>-</c> is part of a number at the start of the value or directly after
    /// an operator, comma or opening parenthesis; otherwise it is an
    /// <see cref="CssValueTokenKind.Operator"/>, which is how <c>calc()</c>
    /// subtraction is distinguished from a signed literal.
    /// </para>
    /// <para>
    /// Numeric scanning accepts a leading sign, a decimal point and an
    /// exponent. A malformed exponent (no digits) is left for the next token.
    /// Dimensions accept a letter unit (including non-ASCII letters); a
    /// trailing <c>%</c> directly after the number is a percentage instead.
    /// Quoted strings honour backslash escapes and keep their quotes in the
    /// token span.
    /// </para>
    /// </remarks>
    public static CssValueToken[] Tokenize(string source)
    {
        if (string.IsNullOrEmpty(source))
        {
            return [];
        }

        var tokens = new List<CssValueToken>(8);
        var i = 0;

        while (i < source.Length)
        {
            var c = source[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                i += 2;

                while (i + 1 < source.Length && !(source[i] == '*' && source[i + 1] == '/'))
                {
                    i++;
                }

                i = Math.Min(source.Length, i + 2);
                continue;
            }

            if (c == ',')
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.Comma, i++, 1));
                continue;
            }

            if (c == '/')
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.Slash, i++, 1));
                continue;
            }

            if (c == '(')
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.OpenParen, i++, 1));
                continue;
            }

            if (c == ')')
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.CloseParen, i++, 1));
                continue;
            }

            if ((c is '+' or '*' or '-') && !IsNumberStart(source, i))
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.Operator, i++, 1));
                continue;
            }

            if (c == '!')
            {
                tokens.Add(new CssValueToken(CssValueTokenKind.Delimiter, i++, 1));
                continue;
            }

            if (c is '"' or '\'')
            {
                var start = i;
                var quote = c;
                i++;

                while (i < source.Length)
                {
                    if (source[i] == '\\')
                    {
                        i += 2;
                        continue;
                    }

                    if (source[i] == quote)
                    {
                        i++;
                        break;
                    }

                    i++;
                }

                tokens.Add(new CssValueToken(CssValueTokenKind.String, start, i - start));
                continue;
            }

            if (c == '#')
            {
                var start = i;
                i++;

                while (i < source.Length && IsNameChar(source[i]))
                {
                    i++;
                }

                tokens.Add(new CssValueToken(CssValueTokenKind.Hash, start, i - start));
                continue;
            }

            if (IsNumberStart(source, i))
            {
                var start = i;
                i = ScanNumber(source, i);
                var number = ParseFloat(source.AsSpan(start, i - start));

                if (i < source.Length && source[i] == '%')
                {
                    tokens.Add(new CssValueToken(
                        CssValueTokenKind.Percentage,
                        start,
                        ++i - start,
                        number));
                    continue;
                }

                var unitStart = i;

                while (i < source.Length && (char.IsLetter(source[i]) || source[i] == '%'))
                {
                    i++;
                }

                if (i > unitStart)
                {
                    tokens.Add(new CssValueToken(
                        CssValueTokenKind.Dimension,
                        start,
                        i - start,
                        number,
                        unitStart,
                        i - unitStart));
                }
                else
                {
                    tokens.Add(new CssValueToken(CssValueTokenKind.Number, start, i - start, number));
                }

                continue;
            }

            if (IsNameStart(c))
            {
                var start = i;
                i++;

                while (i < source.Length && IsNameChar(source[i]))
                {
                    i++;
                }

                if (i < source.Length && source[i] == '(' && !source.AsSpan(start, i - start)
                        .Equals("url", StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(new CssValueToken(CssValueTokenKind.Function, start, i - start));
                    i++;
                }
                else
                {
                    tokens.Add(new CssValueToken(CssValueTokenKind.Word, start, i - start));
                }

                continue;
            }

            tokens.Add(new CssValueToken(CssValueTokenKind.Delimiter, i++, 1));
        }

        return tokens.ToArray();
    }

    /// <summary>Gets a token's source text as a new string.</summary>
    /// <param name="source">Original source text passed to <see cref="Tokenize(string)"/>.</param>
    /// <param name="token">Token to slice.</param>
    /// <returns>
    /// The token text, including quotes for strings. A function token contains
    /// only its name because the opening parenthesis is consumed separately.
    /// </returns>
    /// <remarks>
    /// Allocates one string; use
    /// <see cref="GetSpan(string, in CssValueToken)"/> on hot paths.
    /// </remarks>
    public static string GetText(string source, in CssValueToken token)
    {
        return source.Substring(token.Start, token.Length);
    }

    /// <summary>Gets a token's source text as a span without allocating.</summary>
    /// <param name="source">Original source text passed to <see cref="Tokenize(string)"/>.</param>
    /// <param name="token">Token to slice.</param>
    /// <returns>A span over the token's characters.</returns>
    public static ReadOnlySpan<char> GetSpan(string source, in CssValueToken token)
    {
        return source.AsSpan(token.Start, token.Length);
    }

    /// <summary>Gets the unit text of a dimension token.</summary>
    /// <param name="source">Original source text passed to <see cref="Tokenize(string)"/>.</param>
    /// <param name="token">Dimension token; other kinds return an empty span.</param>
    /// <returns>The unit characters, such as <c>px</c> or <c>rem</c>, or an empty span.</returns>
    /// <remarks>No allocation occurs; the returned span aliases <paramref name="source"/>.</remarks>
    public static ReadOnlySpan<char> GetUnit(string source, in CssValueToken token)
    {
        return token.UnitLength == 0
            ? ReadOnlySpan<char>.Empty
            : source.AsSpan(token.UnitStart, token.UnitLength);
    }

    /// <summary>Tests whether a token is a word equal to a keyword.</summary>
    /// <param name="source">Original source text.</param>
    /// <param name="token">Token to test.</param>
    /// <param name="word">Keyword to compare against.</param>
    /// <returns><see langword="true"/> for a case-insensitive match on a word token.</returns>
    public static bool IsWord(string source, in CssValueToken token, ReadOnlySpan<char> word)
    {
        return token.Kind == CssValueTokenKind.Word
            && GetSpan(source, in token).Equals(word, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Decides whether a number starts at an index.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="index">Index to test.</param>
    /// <returns>
    /// <see langword="true"/> for a digit, a dot followed by a digit, or a sign
    /// in a numeric context.
    /// </returns>
    /// <remarks>
    /// The sign rule is what lets <c>calc(10px - 2px)</c> produce an operator
    /// while <c>translate(-10px)</c> produces a negative dimension: a sign is
    /// numeric only at the start of the value or directly after
    /// <c>(</c>, <c>,</c>, <c>+</c>, <c>-</c>, <c>*</c> or <c>/</c>.
    /// </remarks>
    private static bool IsNumberStart(string source, int index)
    {
        var c = source[index];

        if (char.IsDigit(c))
        {
            return true;
        }

        if (c == '.')
        {
            return index + 1 < source.Length && char.IsDigit(source[index + 1]);
        }

        if (c is '-' or '+')
        {
            if (index + 1 < source.Length && (char.IsDigit(source[index + 1]) || source[index + 1] == '.'))
            {
                // A sign is part of a number only at the start of a value or
                // after an operator/comma/open paren. Otherwise it is the
                // subtraction operator in calc().
                return index == 0 || IsSignContext(source, index - 1);
            }
        }

        return false;
    }

    /// <summary>Tests whether the character before a sign puts the sign in a numeric context.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="previous">Index of the character immediately before the sign.</param>
    /// <returns>
    /// <see langword="true"/> when the preceding non-whitespace character is an
    /// operator or separator.
    /// </returns>
    private static bool IsSignContext(string source, int previous)
    {
        while (previous >= 0 && char.IsWhiteSpace(source[previous]))
        {
            previous--;
        }

        if (previous < 0)
        {
            return true;
        }

        return source[previous] is '(' or ',' or '+' or '-' or '*' or '/';
    }

    /// <summary>Scans a numeric literal and returns the index one past it.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="i">Index of the first character of the number.</param>
    /// <returns>The exclusive end index of the numeric text.</returns>
    /// <remarks>
    /// Accepts an optional sign, digits with an optional decimal point, and an
    /// exponent. If the exponent has no digits the index is rewound to the
    /// <c>e</c> so it can be tokenized as a unit or word.
    /// </remarks>
    private static int ScanNumber(string source, int i)
    {
        if (i < source.Length && source[i] is '+' or '-')
        {
            i++;
        }

        var digits = 0;

        while (i < source.Length && char.IsDigit(source[i]))
        {
            i++;
            digits++;
        }

        if (i < source.Length && source[i] == '.')
        {
            i++;

            while (i < source.Length && char.IsDigit(source[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
        {
            return i;
        }

        if (i < source.Length && source[i] is 'e' or 'E')
        {
            var exponentStart = i;
            i++;

            if (i < source.Length && source[i] is '+' or '-')
            {
                i++;
            }

            var exponentDigits = 0;

            while (i < source.Length && char.IsDigit(source[i]))
            {
                i++;
                exponentDigits++;
            }

            if (exponentDigits == 0)
            {
                i = exponentStart;
            }
        }

        return i;
    }

    /// <summary>Parses numeric text with invariant culture.</summary>
    /// <param name="span">Numeric characters.</param>
    /// <returns>The parsed value, or zero when parsing fails.</returns>
    /// <remarks>
    /// Failure is silent because the tokenizer is expected to have produced
    /// well-formed text.
    /// </remarks>
    private static float ParseFloat(ReadOnlySpan<char> span)
    {
        return float.TryParse(
            span,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;
    }

    /// <summary>Tests whether a character can start an identifier.</summary>
    /// <param name="c">Character to test.</param>
    /// <returns><see langword="true"/> for a letter, underscore, backslash or non-ASCII character.</returns>
    private static bool IsNameStart(char c)
    {
        return char.IsLetter(c) || c is '_' or '\\' || c >= 0x80;
    }

    /// <summary>Tests whether a character can continue an identifier.</summary>
    /// <param name="c">Character to test.</param>
    /// <returns>
    /// <see langword="true"/> for a letter, digit, underscore, hyphen,
    /// backslash or non-ASCII character.
    /// </returns>
    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or '-' or '\\' || c >= 0x80;
    }
}
