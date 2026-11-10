using System.Globalization;

namespace Ignite.LayoutLab;

/// <summary>
/// Pragmatic CSS value parsing shared by the style resolver. It handles the
/// syntax the layout engine needs, including nested <c>calc()</c>, without
/// allocating a full CSS object model.
/// </summary>
public static class CssValueParser
{
    public static CssLength ParseLength(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return CssLength.Auto;
        }

        var source = value.Trim();
        var tokens = CssValueTokenizer.Tokenize(source);

        if (tokens.Length == 0)
        {
            return CssLength.Auto;
        }

        if (tokens.Length == 1)
        {
            return LengthFromToken(source, tokens, 0);
        }

        // A function may be split across tokens, but a plain multi-token value
        // is not a single length. Callers that need a shorthand split first.
        if (tokens[0].Kind == CssValueTokenKind.Function)
        {
            var index = 0;
            var expression = ParseExpression(source, tokens, ref index);

            if (index >= tokens.Length)
            {
                return new CssLength(0, LengthUnit.Number, expression);
            }
        }

        return LengthFromToken(source, tokens, 0);
    }

    public static CssLength ParseLengthOrAuto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return CssLength.Auto;
        }

        return ParseLength(value);
    }

    public static float ParseNumber(string? value, float fallback = 0)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var source = value.Trim();
        var tokens = CssValueTokenizer.Tokenize(source);

        if (tokens.Length == 0)
        {
            return fallback;
        }

        if (tokens[0].Kind is CssValueTokenKind.Number or CssValueTokenKind.Dimension
            or CssValueTokenKind.Percentage)
        {
            return tokens[0].Number;
        }

        if (tokens[0].Kind == CssValueTokenKind.Function)
        {
            var index = 0;
            var expression = ParseExpression(source, tokens, ref index);
            return expression.Resolve(new LengthContext());
        }

        return fallback;
    }

    public static int ParseInt(string? value, int fallback = 0)
    {
        return (int)MathF.Round(ParseNumber(value, fallback));
    }

    public static bool ParseBool(string? value, bool fallback = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" => true,
            "false" or "0" or "no" => false,
            _ => fallback
        };
    }

    public static bool IsKeyword(string? value, ReadOnlySpan<char> keyword)
    {
        return value is not null
            && value.AsSpan().Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }

    public static string? FirstKeyword(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var source = value.Trim();
        var tokens = CssValueTokenizer.Tokenize(source);

        return tokens.Length == 0 || tokens[0].Kind != CssValueTokenKind.Word
            ? null
            : CssValueTokenizer.GetText(source, tokens[0]);
    }

    /// <summary>
    /// Splits a value at top-level separators, respecting parentheses, quotes
    /// and <c>url()</c> contents. Consecutive whitespace is treated as one
    /// separator; separators inside <c>calc()</c> never split.
    /// </summary>
    public static List<string> SplitTopLevel(string value, char separator)
    {
        var parts = new List<string>(4);

        if (string.IsNullOrEmpty(value))
        {
            return parts;
        }

        var start = 0;
        var depth = 0;
        var bracketDepth = 0;
        var quote = '\0';

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (quote != '\0')
            {
                if (c == '\\')
                {
                    i++;
                }
                else if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (c == '[')
            {
                bracketDepth++;
                continue;
            }

            if (c == ']')
            {
                bracketDepth = Math.Max(0, bracketDepth - 1);
                continue;
            }

            if (depth != 0 || bracketDepth != 0)
            {
                continue;
            }

            if (separator == ' ')
            {
                if (!char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (i > start)
                {
                    parts.Add(value[start..i].Trim());
                }

                while (i + 1 < value.Length && char.IsWhiteSpace(value[i + 1]))
                {
                    i++;
                }

                start = i + 1;
                continue;
            }

            if (c != separator)
            {
                continue;
            }

            if (i > start)
            {
                parts.Add(value[start..i].Trim());
            }

            start = i + 1;
        }

        if (start < value.Length)
        {
            var tail = value[start..].Trim();

            if (tail.Length != 0)
            {
                parts.Add(tail);
            }
        }

        return parts;
    }

    public static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var quote = value[0];

        if ((quote == '"' || quote == '\'') && value[^1] == quote)
        {
            return value[1..^1];
        }

        return value;
    }

    private static CssLength LengthFromToken(string source, CssValueToken[] tokens, int index)
    {
        var token = tokens[index];

        switch (token.Kind)
        {
            case CssValueTokenKind.Number:
                return CssLength.Number(token.Number);

            case CssValueTokenKind.Percentage:
                return CssLength.Percent(token.Number);

            case CssValueTokenKind.Dimension:
                return new CssLength(token.Number, UnitFromName(CssValueTokenizer.GetUnit(source, in token)));

            case CssValueTokenKind.Word:
            {
                var word = CssValueTokenizer.GetSpan(source, in token);

                if (word.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    return CssLength.Auto;
                }

                if (word.Equals("min-content", StringComparison.OrdinalIgnoreCase))
                {
                    return CssLength.MinContent;
                }

                if (word.Equals("max-content", StringComparison.OrdinalIgnoreCase))
                {
                    return CssLength.MaxContent;
                }

                if (word.Equals("fit-content", StringComparison.OrdinalIgnoreCase))
                {
                    return new CssLength(0, LengthUnit.FitContent);
                }

                if (word.Equals("stretch", StringComparison.OrdinalIgnoreCase)
                    || word.Equals("-webkit-fill-available", StringComparison.OrdinalIgnoreCase))
                {
                    return CssLength.Stretch;
                }

                if (word.Equals("none", StringComparison.OrdinalIgnoreCase))
                {
                    return CssLength.Auto;
                }

                return CssLength.Auto;
            }

            case CssValueTokenKind.Function:
            {
                var expression = ParseExpression(source, tokens, ref index);
                return new CssLength(0, LengthUnit.Number, expression);
            }

            default:
                return CssLength.Auto;
        }
    }

    private static LengthUnit UnitFromName(ReadOnlySpan<char> unit)
    {
        if (unit.Equals("px", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Px;
        if (unit.Equals("em", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Em;
        if (unit.Equals("rem", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Rem;
        if (unit.Equals("ex", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Ex;
        if (unit.Equals("ch", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Ch;
        if (unit.Equals("vw", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Vw;
        if (unit.Equals("vh", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Vh;
        if (unit.Equals("vmin", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Vmin;
        if (unit.Equals("vmax", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Vmax;
        if (unit.Equals("cm", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Cm;
        if (unit.Equals("mm", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Mm;
        if (unit.Equals("in", StringComparison.OrdinalIgnoreCase)) return LengthUnit.In;
        if (unit.Equals("pt", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Pt;
        if (unit.Equals("pc", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Pc;
        if (unit.Equals("fr", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Fr;
        if (unit.Equals("%", StringComparison.OrdinalIgnoreCase)) return LengthUnit.Percent;
        return LengthUnit.Number;
    }

    private static CssExpression ParseExpression(string source, CssValueToken[] tokens, ref int index)
    {
        var expression = ParseTerm(source, tokens, ref index);

        while (index < tokens.Length && tokens[index].Kind == CssValueTokenKind.Operator)
        {
            var op = CssValueTokenizer.GetSpan(source, in tokens[index])[0];

            if (op is not ('+' or '-'))
            {
                break;
            }

            index++;
            var right = ParseTerm(source, tokens, ref index);
            expression = new CssBinaryExpression(op, expression, right);
        }

        return expression;
    }

    private static CssExpression ParseTerm(string source, CssValueToken[] tokens, ref int index)
    {
        var expression = ParseFactor(source, tokens, ref index);

        while (index < tokens.Length)
        {
            var token = tokens[index];

            if (token.Kind != CssValueTokenKind.Operator && token.Kind != CssValueTokenKind.Slash)
            {
                break;
            }

            var op = token.Kind == CssValueTokenKind.Slash
                ? '/'
                : CssValueTokenizer.GetSpan(source, in token)[0];

            if (op is not ('*' or '/'))
            {
                break;
            }

            index++;
            var right = ParseFactor(source, tokens, ref index);
            expression = new CssBinaryExpression(op, expression, right);
        }

        return expression;
    }

    private static CssExpression ParseFactor(string source, CssValueToken[] tokens, ref int index)
    {
        if (index >= tokens.Length)
        {
            return new CssLiteralExpression(CssLength.Zero);
        }

        var token = tokens[index];

        if (token.Kind == CssValueTokenKind.Operator)
        {
            var op = CssValueTokenizer.GetSpan(source, in token)[0];
            index++;
            var operand = ParseFactor(source, tokens, ref index);
            return new CssUnaryExpression(op == '-', operand);
        }

        if (token.Kind == CssValueTokenKind.OpenParen)
        {
            index++;
            var inner = ParseExpression(source, tokens, ref index);

            if (index < tokens.Length && tokens[index].Kind == CssValueTokenKind.CloseParen)
            {
                index++;
            }

            return inner;
        }

        if (token.Kind == CssValueTokenKind.Function)
        {
            var name = CssValueTokenizer.GetSpan(source, in token);
            index++;

            if (name.Equals("calc", StringComparison.OrdinalIgnoreCase))
            {
                var inner = ParseExpression(source, tokens, ref index);

                if (index < tokens.Length && tokens[index].Kind == CssValueTokenKind.CloseParen)
                {
                    index++;
                }

                return inner;
            }

            if (name.Equals("min", StringComparison.OrdinalIgnoreCase)
                || name.Equals("max", StringComparison.OrdinalIgnoreCase)
                || name.Equals("clamp", StringComparison.OrdinalIgnoreCase))
            {
                var kind = name.Equals("min", StringComparison.OrdinalIgnoreCase)
                    ? CssFunctionKind.Min
                    : name.Equals("max", StringComparison.OrdinalIgnoreCase)
                        ? CssFunctionKind.Max
                        : CssFunctionKind.Clamp;

                var arguments = new List<CssExpression>(3);

                while (index < tokens.Length && tokens[index].Kind != CssValueTokenKind.CloseParen)
                {
                    arguments.Add(ParseExpression(source, tokens, ref index));

                    if (index < tokens.Length && tokens[index].Kind == CssValueTokenKind.Comma)
                    {
                        index++;
                    }
                    else
                    {
                        break;
                    }
                }

                if (index < tokens.Length && tokens[index].Kind == CssValueTokenKind.CloseParen)
                {
                    index++;
                }

                return new CssFunctionExpression(kind, arguments.ToArray());
            }

            // Unknown functions are zero-length; the surrounding declaration will
            // generally fail closed rather than producing a bogus box.
            SkipFunction(tokens, ref index);
            return new CssLiteralExpression(CssLength.Zero);
        }

        index++;

        return new CssLiteralExpression(LengthFromToken(source, tokens, index - 1));
    }

    private static void SkipFunction(CssValueToken[] tokens, ref int index)
    {
        var depth = 1;

        while (index < tokens.Length && depth > 0)
        {
            if (tokens[index].Kind == CssValueTokenKind.Function
                || tokens[index].Kind == CssValueTokenKind.OpenParen)
            {
                depth++;
            }
            else if (tokens[index].Kind == CssValueTokenKind.CloseParen)
            {
                depth--;
            }

            index++;
        }
    }
}
