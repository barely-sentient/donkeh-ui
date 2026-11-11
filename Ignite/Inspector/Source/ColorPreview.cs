using System.Globalization;

namespace Ignite.Inspector;

/// <summary>
/// Parses and formats the colour values the inspector previews next to a
/// declaration, using packed ARGB (<c>0xAARRGGBB</c>) to match the layout
/// engine's convention.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TryParse"/> accepts the subset of CSS colour syntax the inspector
/// needs: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>,
/// <c>rgb(...)</c>/<c>rgba(...)</c> with comma, space or slash separators, and a
/// small set of named colours. Numeric components are clamped rather than
/// rejected, so malformed-but-parseable input still produces a colour.
/// </para>
/// <para>
/// Hex input is written in CSS order (RGB then alpha), while the packed result is
/// ARGB; <see cref="Format"/> reverses that conversion. Alpha is never
/// composited, so <c>transparent</c> parses to <c>0x00000000</c> rather than to
/// the colour behind it.
/// </para>
/// <para>
/// The type is stateless apart from a read-only lookup table, so every member is
/// safe to call from any thread.
/// </para>
/// </remarks>
public static class ColorPreview
{
    /// <summary>
    /// Read-only lookup of the CSS colour keywords understood by
    /// <see cref="TryParse"/>, keyed case-insensitively.
    /// </summary>
    /// <remarks>
    /// This is intentionally a small subset of the CSS named colours, not the
    /// full list. Duplicate spellings (<c>gray</c>/<c>grey</c>,
    /// <c>aqua</c>/<c>cyan</c>, <c>fuchsia</c>/<c>magenta</c>) share the same
    /// packed value.
    /// </remarks>
    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = 0xFF000000,
        ["white"] = 0xFFFFFFFF,
        ["red"] = 0xFFFF0000,
        ["green"] = 0xFF008000,
        ["lime"] = 0xFF00FF00,
        ["blue"] = 0xFF0000FF,
        ["yellow"] = 0xFFFFFF00,
        ["orange"] = 0xFFFFA500,
        ["purple"] = 0xFF800080,
        ["fuchsia"] = 0xFFFF00FF,
        ["magenta"] = 0xFFFF00FF,
        ["aqua"] = 0xFF00FFFF,
        ["cyan"] = 0xFF00FFFF,
        ["teal"] = 0xFF008080,
        ["navy"] = 0xFF000080,
        ["maroon"] = 0xFF800000,
        ["silver"] = 0xFFC0C0C0,
        ["gray"] = 0xFF808080,
        ["grey"] = 0xFF808080,
        ["transparent"] = 0x00000000
    };

    /// <summary>
    /// Attempts to parse a CSS colour string into a packed ARGB value.
    /// </summary>
    /// <param name="text">
    /// The colour text to parse. May be <see langword="null"/>, empty or
    /// surrounded by whitespace.
    /// </param>
    /// <param name="argb">
    /// When this method returns <see langword="true"/>, receives the packed
    /// colour as <c>0xAARRGGBB</c>; otherwise receives <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the text is a recognised colour; otherwise
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The format is chosen by prefix: text starting with <c>#</c> is parsed as
    /// hex, text starting with <c>rgb(</c> or <c>rgba(</c> (case-insensitive) as
    /// a functional colour, and anything else is looked up in
    /// <see cref="Named"/>.
    /// </para>
    /// <para>
    /// This method never throws for invalid input; unsupported syntax such as
    /// <c>hsl()</c>, <c>lab()</c> or the CSS-wide keywords simply returns
    /// <see langword="false"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// ColorPreview.TryParse("rgba(255, 0, 0, 0.5)", out var red);  // true
    /// var css = ColorPreview.Format(red);                          // "#FF000080"
    /// </code>
    /// </example>
    public static bool TryParse(string? text, out uint argb)
    {
        argb = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();

        if (value[0] == '#')
        {
            return TryParseHex(value, out argb);
        }

        if (value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase))
        {
            return TryParseRgb(value, out argb);
        }

        return Named.TryGetValue(value, out argb);
    }

    /// <summary>
    /// Parses the hex digits of a hash-prefixed colour.
    /// </summary>
    /// <param name="value">The colour text including the leading <c>#</c>.</param>
    /// <param name="argb">
    /// Receives the packed colour on success; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the digit count and digits are valid.
    /// </returns>
    /// <remarks>
    /// Only lengths 3, 4, 6 and 8 are accepted. Missing alpha defaults to
    /// <c>255</c>; shorthand digits are expanded by repeating them
    /// (<c>#f00</c> becomes <c>#ff0000</c>). Digits are in CSS order, RGB first.
    /// </remarks>
    private static bool TryParseHex(string value, out uint argb)
    {
        argb = 0;
        var hex = value.AsSpan(1);

        if (!TryNibble(hex, out var r, out var g, out var b, out var a))
        {
            return false;
        }

        argb = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        return true;
    }

    /// <summary>
    /// Determines whether the text can be parsed as a colour.
    /// </summary>
    /// <param name="text">
    /// The colour text to test; may be <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <see cref="TryParse"/> would succeed.
    /// </returns>
    /// <remarks>
    /// Convenience wrapper used to decide whether a declaration value gets a
    /// swatch. It discards the parsed value and never throws.
    /// </remarks>
    public static bool IsColor(string? text) => TryParse(text, out _);

    /// <summary>Formats packed ARGB as a CSS hex colour.</summary>
    /// <param name="argb">The packed colour, <c>0xAARRGGBB</c>.</param>
    /// <returns>
    /// <c>#RRGGBB</c> when the colour is fully opaque; otherwise
    /// <c>#RRGGBBAA</c>.
    /// </returns>
    /// <remarks>
    /// Digits are uppercase and zero-padded. The alpha byte is only emitted when
    /// it is not <c>0xFF</c>, matching the shorter notation the inspector shows
    /// for ordinary colours. Alpha is not premultiplied into the channels.
    /// </remarks>
    public static string Format(uint argb)
    {
        var red = (argb >> 16) & 0xFF;
        var green = (argb >> 8) & 0xFF;
        var blue = argb & 0xFF;
        var alpha = (argb >> 24) & 0xFF;

        return alpha == 0xFF
            ? $"#{red:X2}{green:X2}{blue:X2}"
            : $"#{red:X2}{green:X2}{blue:X2}{alpha:X2}";
    }

    /// <summary>
    /// Converts a packed ARGB colour to hue, saturation and value.
    /// </summary>
    /// <param name="argb">The packed colour, <c>0xAARRGGBB</c>.</param>
    /// <returns>
    /// A tuple with <c>Hue</c>, <c>Saturation</c> and <c>Value</c>, each in the
    /// range 0..1.
    /// </returns>
    /// <remarks>
    /// Alpha is ignored. Achromatic colours (including black and white) report a
    /// hue of 0, and a delta below <c>0.0001</c> is treated as achromatic to
    /// avoid amplifying rounding noise.
    /// </remarks>
    public static (float Hue, float Saturation, float Value) ToHsv(uint argb)
    {
        var r = ((argb >> 16) & 0xFF) / 255f;
        var g = ((argb >> 8) & 0xFF) / 255f;
        var b = (argb & 0xFF) / 255f;
        var max = MathF.Max(r, MathF.Max(g, b));
        var min = MathF.Min(r, MathF.Min(g, b));
        var delta = max - min;
        var hue = 0f;

        if (delta > 0.0001f)
        {
            if (max == r)
            {
                hue = ((g - b) / delta) % 6f;
            }
            else if (max == g)
            {
                hue = (b - r) / delta + 2f;
            }
            else
            {
                hue = (r - g) / delta + 4f;
            }

            hue /= 6f;

            if (hue < 0)
            {
                hue += 1;
            }
        }

        return (hue, max <= 0.0001f ? 0 : delta / max, max);
    }

    /// <summary>
    /// Converts hue, saturation and value to a packed, fully opaque ARGB colour.
    /// </summary>
    /// <param name="hue">Hue in 0..1; values outside the range wrap around.</param>
    /// <param name="saturation">
    /// Saturation in 0..1; out-of-range values are effectively clamped per
    /// channel by the final conversion.
    /// </param>
    /// <param name="value">
    /// Value (brightness) in 0..1; out-of-range values are effectively clamped
    /// per channel by the final conversion.
    /// </param>
    /// <returns>
    /// The packed colour, always with an alpha of <c>0xFF</c>.
    /// </returns>
    /// <remarks>
    /// The sector is chosen with <c>hue * 6</c>, and each channel is rounded to
    /// the nearest 8-bit value after clamping. Round-tripping through
    /// <see cref="ToHsv"/> can therefore differ from the original by one unit per
    /// channel.
    /// </remarks>
    public static uint FromHsv(float hue, float saturation, float value)
    {
        hue = (hue % 1f + 1f) % 1f;
        var sector = hue * 6f;
        var index = (int)sector;
        var fraction = sector - index;
        var p = value * (1f - saturation);
        var q = value * (1f - fraction * saturation);
        var t = value * (1f - (1f - fraction) * saturation);

        var (r, g, b) = (index % 6) switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };

        return 0xFF000000
            | ((uint)MathF.Round(Math.Clamp(r, 0, 1) * 255f) << 16)
            | ((uint)MathF.Round(Math.Clamp(g, 0, 1) * 255f) << 8)
            | (uint)MathF.Round(Math.Clamp(b, 0, 1) * 255f);
    }

    /// <summary>
    /// Decodes 3, 4, 6 or 8 hex digits into 8-bit channels.
    /// </summary>
    /// <param name="hex">The digits after the leading <c>#</c>.</param>
    /// <param name="r">Receives the red channel, 0..255.</param>
    /// <param name="g">Receives the green channel, 0..255.</param>
    /// <param name="b">Receives the blue channel, 0..255.</param>
    /// <param name="a">
    /// Receives the alpha channel, 0..255; 255 when the input omits alpha.
    /// </param>
    /// <returns>
    /// <see langword="true"/> for a supported length with valid digits.
    /// </returns>
    /// <remarks>
    /// Despite the name, this handles both the shorthand and the full forms.
    /// Input is in CSS order (RGB, then optional alpha), not ARGB.
    /// </remarks>
    private static bool TryNibble(
        ReadOnlySpan<char> hex,
        out int r,
        out int g,
        out int b,
        out int a)
    {
        r = g = b = 0;
        a = 255;

        if (hex.Length is 3 or 4)
        {
            if (!TryHexByte(hex[..1], out var rh)
                || !TryHexByte(hex.Slice(1, 1), out var gh)
                || !TryHexByte(hex.Slice(2, 1), out var bh))
            {
                return false;
            }

            r = rh * 17;
            g = gh * 17;
            b = bh * 17;

            if (hex.Length == 4)
            {
                if (!TryHexByte(hex.Slice(3, 1), out var ah))
                {
                    return false;
                }

                a = ah * 17;
            }

            return true;
        }

        if (hex.Length is 6 or 8)
        {
            if (!TryHexByte(hex[..2], out r)
                || !TryHexByte(hex.Slice(2, 2), out g)
                || !TryHexByte(hex.Slice(4, 2), out b))
            {
                return false;
            }

            if (hex.Length == 8 && !TryHexByte(hex.Slice(6, 2), out a))
            {
                return false;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses one or two hexadecimal digits into an integer.
    /// </summary>
    /// <param name="text">The digits to parse.</param>
    /// <param name="value">Receives the parsed value on success.</param>
    /// <returns>
    /// <see langword="true"/> if the span is valid hexadecimal.
    /// </returns>
    /// <remarks>
    /// Uses invariant culture and <see cref="NumberStyles.HexNumber"/>, so signs
    /// and <c>0x</c> prefixes are rejected. An empty span fails.
    /// </remarks>
    private static bool TryHexByte(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// Parses an <c>rgb(...)</c> or <c>rgba(...)</c> functional colour.
    /// </summary>
    /// <param name="value">
    /// The full function text, including the name and parentheses.
    /// </param>
    /// <param name="argb">
    /// Receives the packed colour on success; otherwise <c>0</c>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when three components and an optional alpha parse.
    /// </returns>
    /// <remarks>
    /// Components are split on commas, spaces and slashes, so both legacy and
    /// modern CSS syntax work. A fourth component is treated as alpha; any
    /// further components are ignored. Each channel is rounded and clamped by
    /// <see cref="TryComponent"/>.
    /// </remarks>
    private static bool TryParseRgb(string value, out uint argb)
    {
        argb = 0;
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return false;
        }

        var parts = value[(open + 1)..close]
            .Split([',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 3)
        {
            return false;
        }

        if (!TryComponent(parts[0], 255, out var r)
            || !TryComponent(parts[1], 255, out var g)
            || !TryComponent(parts[2], 255, out var b))
        {
            return false;
        }

        var a = 255;

        if (parts.Length >= 4 && !TryAlpha(parts[3], out a))
        {
            return false;
        }

        argb = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
        return true;
    }

    /// <summary>
    /// Parses one RGB channel, either as a number in 0..<paramref name="max"/> or
    /// as a percentage.
    /// </summary>
    /// <param name="text">The component text, optionally ending in <c>%</c>.</param>
    /// <param name="max">The channel maximum (255 for RGB).</param>
    /// <param name="value">Receives the rounded, clamped channel value.</param>
    /// <returns><see langword="true"/> if the component parsed.</returns>
    /// <remarks>
    /// Negative and oversized values are clamped rather than rejected, matching
    /// the forgiving behaviour of the preview. Fractional values are rounded to
    /// the nearest integer.
    /// </remarks>
    private static bool TryComponent(string text, int max, out int value)
    {
        value = 0;

        if (text.EndsWith('%'))
        {
            if (!float.TryParse(
                    text[..^1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percent))
            {
                return false;
            }

            value = (int)MathF.Round(Math.Clamp(percent, 0, 100) * max / 100f);
            return true;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        value = (int)MathF.Round(Math.Clamp(number, 0, max));
        return true;
    }

    /// <summary>
    /// Parses an alpha component given as a percentage, a 0..1 fraction or a
    /// 0..255 byte.
    /// </summary>
    /// <param name="text">The component text, optionally ending in <c>%</c>.</param>
    /// <param name="value">Receives the alpha channel, 0..255.</param>
    /// <returns><see langword="true"/> if the component parsed.</returns>
    /// <remarks>
    /// Values greater than 1 are assumed to be in the 0..255 range and divided
    /// by 255; values up to 1 are treated as fractions. Consequently <c>1</c>
    /// means fully opaque, not one 255th. Out-of-range results are clamped.
    /// </remarks>
    private static bool TryAlpha(string text, out int value)
    {
        value = 255;

        if (text.EndsWith('%'))
        {
            if (!float.TryParse(
                    text[..^1],
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out var percent))
            {
                return false;
            }

            value = (int)MathF.Round(Math.Clamp(percent, 0, 100) * 255 / 100f);
            return true;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return false;
        }

        if (number > 1)
        {
            number /= 255f;
        }

        value = (int)MathF.Round(Math.Clamp(number, 0, 1) * 255);
        return true;
    }
}
