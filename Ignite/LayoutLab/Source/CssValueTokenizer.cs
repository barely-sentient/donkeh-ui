namespace Ignite.LayoutLab;

internal enum CssValueTokenKind : byte
{
    Word,
    Number,
    Dimension,
    Percentage,
    Hash,
    String,
    Function,
    Comma,
    Slash,
    Operator,
    OpenParen,
    CloseParen,
    Colon,
    Delimiter
}

internal readonly struct CssValueToken
{
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

    public CssValueTokenKind Kind { get; }
    public int Start { get; }
    public int Length { get; }
    public float Number { get; }
    public int UnitStart { get; }
    public int UnitLength { get; }
}

/// <summary>
/// A tiny, allocation-free-at-token-level CSS value tokenizer. It only needs to
/// understand value syntax, not selectors, so it is much smaller than the main
/// stylesheet tokenizer.
/// </summary>
internal static class CssValueTokenizer
{
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

    public static string GetText(string source, in CssValueToken token)
    {
        return source.Substring(token.Start, token.Length);
    }

    public static ReadOnlySpan<char> GetSpan(string source, in CssValueToken token)
    {
        return source.AsSpan(token.Start, token.Length);
    }

    public static ReadOnlySpan<char> GetUnit(string source, in CssValueToken token)
    {
        return token.UnitLength == 0
            ? ReadOnlySpan<char>.Empty
            : source.AsSpan(token.UnitStart, token.UnitLength);
    }

    public static bool IsWord(string source, in CssValueToken token, ReadOnlySpan<char> word)
    {
        return token.Kind == CssValueTokenKind.Word
            && GetSpan(source, in token).Equals(word, StringComparison.OrdinalIgnoreCase);
    }

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

    private static bool IsNameStart(char c)
    {
        return char.IsLetter(c) || c is '_' or '\\' || c >= 0x80;
    }

    private static bool IsNameChar(char c)
    {
        return char.IsLetterOrDigit(c) || c is '_' or '-' or '\\' || c >= 0x80;
    }
}
