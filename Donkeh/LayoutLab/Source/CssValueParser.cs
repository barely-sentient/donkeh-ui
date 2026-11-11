using System.Globalization;

namespace Donkeh.LayoutLab;

/// <summary>
/// Pragmatic CSS value parsing shared by the style resolver. It handles the
/// syntax the layout engine needs, including nested <c>calc()</c>, without
/// allocating a full CSS object model.
/// </summary>
/// <remarks>
/// <para>
/// The parser is deliberately forgiving: unknown keywords and unsupported
/// functions degrade to <see cref="CssLength.Auto"/> or a zero-valued
/// expression rather than throwing, because an invalid declaration must not
/// abort the rest of the rule. Callers that need to distinguish "invalid" from
/// "auto" should inspect the original text first, for example with
/// <see cref="IsKeyword(string?, ReadOnlySpan{char})"/>.
/// </para>
/// <para>
/// All members are static and stateless, so the type is thread safe. Parsing a
/// length or number tokenizes the string, which allocates a token array; cache
/// the resulting <see cref="CssLength"/> rather than re-parsing per frame.
/// </para>
/// <para>
/// Lengths are returned unresolved so <c>em</c>, percentages and viewport
/// units can be evaluated against the correct <see cref="LengthContext"/>.
/// </para>
/// </remarks>
/// <seealso cref="CssLength"/>
/// <seealso cref="CssValueTokenizer"/>
public static class CssValueParser
{
    /// <summary>Parses a CSS length, dimension, percentage or <c>calc()</c> expression.</summary>
    /// <param name="value">
    /// Value text such as <c>12px</c>, <c>50%</c>, <c>2em</c> or
    /// <c>calc(100% - 2rem)</c>.
    /// </param>
    /// <returns>
    /// The parsed length. Returns <see cref="CssLength.Auto"/> for null,
    /// whitespace, unknown keywords and punctuation-led values. A function-led
    /// value yields a <see cref="LengthUnit.Number"/> length carrying an
    /// expression tree; unknown functions resolve to zero.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A single token is converted directly. A value beginning with a function
    /// token is parsed as an expression; if the expression consumes every
    /// token, the result carries the expression tree. Otherwise the first token
    /// alone is used, which is why callers must split shorthands before calling
    /// this method. A bare number is kept as <see cref="LengthUnit.Number"/>
    /// rather than being treated as pixels.
    /// </para>
    /// <para>
    /// Recognised keywords are <c>auto</c>, <c>none</c> (both auto),
    /// <c>min-content</c>, <c>max-content</c>, <c>fit-content</c> and
    /// <c>stretch</c>/<c>-webkit-fill-available</c>. Units are case
    /// insensitive; an unrecognised unit becomes
    /// <see cref="LengthUnit.Number"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var width = CssValueParser.ParseLength("calc(50% - 8px)");
    /// var pixels = width.Resolve(context, 400);
    /// </code>
    /// </example>
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

    /// <summary>Parses a length, mapping the <c>auto</c> keyword explicitly.</summary>
    /// <param name="value">Length text, or <c>auto</c>.</param>
    /// <returns>
    /// <see cref="CssLength.Auto"/> for null, whitespace or <c>auto</c>;
    /// otherwise the parsed length.
    /// </returns>
    /// <remarks>
    /// Functionally similar to <see cref="ParseLength(string?)"/>, but makes the
    /// auto case explicit so callers can rely on it even if the general parser
    /// changes its unknown-keyword fallback.
    /// </remarks>
    public static CssLength ParseLengthOrAuto(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return CssLength.Auto;
        }

        return ParseLength(value);
    }

    /// <summary>Parses a unitless number, ignoring any unit suffix.</summary>
    /// <param name="value">Numeric text, or a numeric function such as <c>calc(2 * 3)</c>.</param>
    /// <param name="fallback">Value returned when no number can be read.</param>
    /// <returns>The parsed number, or <paramref name="fallback"/>.</returns>
    /// <remarks>
    /// <para>
    /// For <see cref="CssValueTokenKind.Dimension"/> and
    /// <see cref="CssValueTokenKind.Percentage"/> only the numeric part is
    /// returned, so <c>50%</c> parses as 50 and <c>12px</c> as 12. This is
    /// intended for properties whose grammar already fixes the unit.
    /// </para>
    /// <para>
    /// A function expression is evaluated with a default
    /// <see cref="LengthContext"/>, which has a 16 px font size, a zero
    /// viewport and a non-finite percentage basis. Percentages therefore
    /// resolve to zero here; do not use this overload for percentage-bearing
    /// <c>calc()</c> when the basis matters.
    /// </para>
    /// </remarks>
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

    /// <summary>Parses an integer value.</summary>
    /// <param name="value">Numeric text.</param>
    /// <param name="fallback">Value returned when no number can be read.</param>
    /// <returns>The number rounded to the nearest integer with banker's rounding.</returns>
    /// <remarks>
    /// Rounding uses <see cref="MathF.Round(float)"/>, so exact halves go to
    /// the even integer.
    /// </remarks>
    public static int ParseInt(string? value, int fallback = 0)
    {
        return (int)MathF.Round(ParseNumber(value, fallback));
    }

    /// <summary>Parses a CSS boolean-like keyword.</summary>
    /// <param name="value">Text to parse; trimmed and matched case-insensitively.</param>
    /// <param name="fallback">Value returned for null, whitespace or an unrecognised keyword.</param>
    /// <returns>
    /// <see langword="true"/> for <c>true</c>, <c>1</c> or <c>yes</c>;
    /// <see langword="false"/> for <c>false</c>, <c>0</c> or <c>no</c>.
    /// </returns>
    /// <remarks>
    /// This covers the engine's internal property grammar; CSS itself has no
    /// generic boolean type.
    /// </remarks>
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

    /// <summary>
    /// Tests whether a value is exactly one keyword, ignoring surrounding
    /// whitespace and case.
    /// </summary>
    /// <param name="value">Value text; <see langword="null"/> never matches.</param>
    /// <param name="keyword">Keyword to compare against.</param>
    /// <returns><see langword="true"/> when the trimmed value equals <paramref name="keyword"/>.</returns>
    public static bool IsKeyword(string? value, ReadOnlySpan<char> keyword)
    {
        return value is not null
            && value.AsSpan().Trim().Equals(keyword, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Gets the first identifier in a value, if the value starts with one.</summary>
    /// <param name="value">Value text.</param>
    /// <returns>
    /// The first word token's text, or <see langword="null"/> when the value is
    /// empty or starts with a number, function or punctuation.
    /// </returns>
    /// <remarks>
    /// Useful for dispatching shorthands on their leading keyword without
    /// splitting the whole value. The returned text keeps its original casing.
    /// </remarks>
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
    /// <param name="value">Value text to split.</param>
    /// <param name="separator">
    /// Separator character. A space splits on any whitespace run and collapses
    /// it; any other character splits on exact matches.
    /// </param>
    /// <returns>
    /// The trimmed, non-empty parts in source order; empty when the input is
    /// null or empty.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Tracks parenthesis depth, square-bracket depth and single or double
    /// quotes, so separators inside <c>calc()</c>, <c>rgb()</c>, <c>url()</c>,
    /// media-like functions or quoted strings are ignored. A backslash inside a
    /// quote escapes the following character.
    /// </para>
    /// <para>
    /// Empty parts are dropped rather than preserved, so leading, trailing and
    /// repeated separators do not produce empty entries. CSS comments are not
    /// recognised here; callers normally pass values that the tokenizer or
    /// stylesheet parser has already cleaned. The method allocates a list plus
    /// one substring per part.
    /// </para>
    /// </remarks>
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

    /// <summary>Removes one pair of matching surrounding quotes.</summary>
    /// <param name="value">Text that may be quoted.</param>
    /// <returns>
    /// The text without its outer single or double quotes; the input unchanged
    /// when it is shorter than two characters or the quotes do not match.
    /// </returns>
    /// <remarks>
    /// This is a shallow unquote: backslash escapes are not processed, so
    /// <c>"a\"b"</c> loses only its outer quotes. Values returned by
    /// <see cref="SplitTopLevel(string, char)"/> are already trimmed.
    /// </remarks>
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

    /// <summary>Converts one token into a literal length.</summary>
    /// <param name="source">Source text the token indexes into.</param>
    /// <param name="tokens">Full token array.</param>
    /// <param name="index">Index of the token to convert.</param>
    /// <returns>The converted length; <see cref="CssLength.Auto"/> for unsupported tokens.</returns>
    /// <remarks>
    /// Numbers stay unitless (<see cref="LengthUnit.Number"/>) rather than
    /// becoming pixels, percentages keep their numeric value, and dimensions
    /// are mapped through <see cref="UnitFromName(ReadOnlySpan{char})"/>.
    /// Keywords map as follows: <c>auto</c> and <c>none</c> to auto,
    /// <c>min-content</c>, <c>max-content</c> and <c>fit-content</c> to their
    /// intrinsic kinds, and <c>stretch</c> or <c>-webkit-fill-available</c> to
    /// <see cref="CssLength.Stretch"/>. Unknown words fall back to auto, and a
    /// function token is parsed into an expression tree.
    /// </remarks>
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

    /// <summary>Maps a CSS unit name to a <see cref="LengthUnit"/>.</summary>
    /// <param name="unit">Unit text without the numeric prefix, compared case-insensitively.</param>
    /// <returns>
    /// The matching unit, <see cref="LengthUnit.Percent"/> for <c>%</c>, or
    /// <see cref="LengthUnit.Number"/> for an unknown unit.
    /// </returns>
    /// <remarks>
    /// Only the units understood by the engine are listed. Unknown units
    /// silently become unitless numbers rather than failing the declaration.
    /// </remarks>
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

    /// <summary>Parses the additive level of a <c>calc()</c>-like expression.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="tokens">Token array.</param>
    /// <param name="index">Current position; advanced past the parsed terms.</param>
    /// <returns>The expression tree, left-associative.</returns>
    /// <remarks>
    /// Handles <c>+</c> and <c>-</c> only; multiplicative operators are
    /// consumed by <see cref="ParseTerm"/>, which gives them higher
    /// precedence. No dimensional analysis is performed, so
    /// <c>1px + 1s</c> is accepted and simply adds the numeric values.
    /// </remarks>
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

    /// <summary>Parses the multiplicative level of a <c>calc()</c>-like expression.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="tokens">Token array.</param>
    /// <param name="index">Current position; advanced past the parsed factors.</param>
    /// <returns>The expression tree, left-associative.</returns>
    /// <remarks>
    /// Accepts <c>*</c> as an operator token and <c>/</c> as a slash token, so
    /// the two syntaxes that the tokenizer produces for division both work.
    /// </remarks>
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

    /// <summary>Parses a unary sign, parenthesised group, function call or literal.</summary>
    /// <param name="source">Source text.</param>
    /// <param name="tokens">Token array.</param>
    /// <param name="index">Current position; advanced past the factor.</param>
    /// <returns>The factor's expression node.</returns>
    /// <remarks>
    /// <para>
    /// A leading <c>+</c> or <c>-</c> becomes a
    /// <see cref="CssUnaryExpression"/>. Parentheses are recursed into and
    /// their grouping node is discarded. <c>calc()</c> is unwrapped to its
    /// inner expression, while <c>min()</c>, <c>max()</c> and <c>clamp()</c>
    /// become <see cref="CssFunctionExpression"/> nodes whose arguments are
    /// comma-separated.
    /// </para>
    /// <para>
    /// An unknown function is skipped entirely and yields a zero literal, so a
    /// declaration using an unsupported function fails closed to zero rather
    /// than producing a bogus size. Running out of tokens yields a zero
    /// literal as well.
    /// </para>
    /// </remarks>
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

    /// <summary>Advances past the remainder of an unsupported function call.</summary>
    /// <param name="tokens">Token array.</param>
    /// <param name="index">
    /// Position just after the function token; advanced past its closing
    /// parenthesis.
    /// </param>
    /// <remarks>
    /// Tracks nesting of function and open-parenthesis tokens so a balanced but
    /// unknown function such as <c>env(safe-area-inset-top)</c> is skipped
    /// correctly. An unbalanced input simply consumes the rest of the tokens.
    /// </remarks>
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
