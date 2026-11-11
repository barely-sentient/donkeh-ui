using System.Globalization;

namespace Donkeh.LayoutLab;

/// <summary>Packed ARGB colour helpers and CSS colour parsing.</summary>
/// <remarks>
/// <para>
/// Colours are stored in a single <see cref="uint"/> as <c>0xAARRGGBB</c>:
/// alpha in bits 24–31 and red, green and blue in bits 16–23, 8–15 and 0–7
/// respectively. Channels are straight (non-premultiplied) sRGB bytes in the
/// range 0–255.
/// </para>
/// <para>
/// The type is stateless apart from the immutable named-colour table, so all
/// members are thread safe.
/// </para>
/// </remarks>
/// <seealso cref="CssColor.TryParse(string?, uint, out uint)"/>
public static class CssColor
{
    /// <summary>Fully transparent black, <c>0x00000000</c>.</summary>
    public const uint Transparent = 0x00000000;

    /// <summary>Opaque black, <c>0xFF000000</c>.</summary>
    public const uint Black = 0xFF000000;

    /// <summary>Opaque white, <c>0xFFFFFFFF</c>.</summary>
    public const uint White = 0xFFFFFFFF;

    /// <summary>Packs sRGB channels and alpha into a single ARGB value.</summary>
    /// <param name="r">Red channel, 0–255.</param>
    /// <param name="g">Green channel, 0–255.</param>
    /// <param name="b">Blue channel, 0–255.</param>
    /// <param name="a">Alpha channel, 0–255; defaults to 255 (opaque).</param>
    /// <returns>The packed <c>0xAARRGGBB</c> value.</returns>
    /// <remarks>
    /// The parameters are bytes, so callers must clamp wider channel values
    /// before calling.
    /// </remarks>
    public static uint Pack(byte r, byte g, byte b, byte a = 255)
        => ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    /// <summary>Extracts the alpha channel.</summary>
    /// <param name="color">Packed ARGB colour.</param>
    /// <returns>Alpha in the range 0–255, where 255 is opaque.</returns>
    public static byte Alpha(uint color) => (byte)(color >> 24);

    /// <summary>Extracts the red channel.</summary>
    /// <param name="color">Packed ARGB colour.</param>
    /// <returns>Red in the range 0–255.</returns>
    public static byte Red(uint color) => (byte)(color >> 16);

    /// <summary>Extracts the green channel.</summary>
    /// <param name="color">Packed ARGB colour.</param>
    /// <returns>Green in the range 0–255.</returns>
    public static byte Green(uint color) => (byte)(color >> 8);

    /// <summary>Extracts the blue channel.</summary>
    /// <param name="color">Packed ARGB colour.</param>
    /// <returns>Blue in the range 0–255.</returns>
    public static byte Blue(uint color) => (byte)color;

    /// <summary>Returns a copy of a colour with the alpha channel replaced.</summary>
    /// <param name="color">Packed ARGB colour whose RGB channels are preserved.</param>
    /// <param name="alpha">New alpha, 0–255.</param>
    /// <returns>The repacked colour.</returns>
    public static uint WithAlpha(uint color, byte alpha) => (color & 0x00FFFFFFu) | ((uint)alpha << 24);

    /// <summary>Multiplies a colour's alpha by a factor and clamps the result.</summary>
    /// <param name="color">Packed ARGB colour; RGB channels are unchanged.</param>
    /// <param name="factor">
    /// Multiplier applied to the existing alpha. 0 clears the colour, 1 leaves
    /// it unchanged.
    /// </param>
    /// <returns>The colour with its scaled, rounded alpha.</returns>
    /// <remarks>
    /// Used for opacity inheritance and group alpha. The result is rounded to
    /// the nearest integer rather than truncated so repeated multiplication
    /// does not drift towards transparent.
    /// </remarks>
    public static uint MultiplyAlpha(uint color, float factor)
    {
        var alpha = (byte)Math.Clamp((int)MathF.Round(Alpha(color) * factor), 0, 255);
        return WithAlpha(color, alpha);
    }

    /// <summary>Composites one colour over another using source-over alpha blending.</summary>
    /// <param name="top">Foreground colour.</param>
    /// <param name="bottom">Background colour.</param>
    /// <returns>The resulting straight-alpha colour.</returns>
    /// <remarks>
    /// Implements the Porter–Duff source-over operator per sRGB channel:
    /// <c>out = (top·αt + bottom·αb·(1−αt)) / αout</c>. The fast paths return
    /// <paramref name="top"/> when it is opaque and <paramref name="bottom"/>
    /// when it is fully transparent. Both fully transparent inputs produce
    /// <see cref="Transparent"/>.
    /// </remarks>
    public static uint Blend(uint top, uint bottom)
    {
        var topAlpha = Alpha(top) / 255f;

        if (topAlpha >= 1f)
        {
            return top;
        }

        if (topAlpha <= 0f)
        {
            return bottom;
        }

        var bottomAlpha = Alpha(bottom) / 255f;
        var outAlpha = topAlpha + bottomAlpha * (1 - topAlpha);

        if (outAlpha <= 0)
        {
            return Transparent;
        }

        byte Channel(byte t, byte b)
            => (byte)Math.Clamp((int)MathF.Round((t * topAlpha + b * bottomAlpha * (1 - topAlpha)) / outAlpha), 0, 255);

        return Pack(
            Channel(Red(top), Red(bottom)),
            Channel(Green(top), Green(bottom)),
            Channel(Blue(top), Blue(bottom)),
            (byte)Math.Clamp((int)MathF.Round(outAlpha * 255), 0, 255));
    }

    /// <summary>Attempts to parse a CSS colour value.</summary>
    /// <param name="value">
    /// Colour text: a hex literal such as <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>
    /// or <c>#rrggbbaa</c>; an <c>rgb()</c>/<c>rgba()</c> or
    /// <c>hsl()</c>/<c>hsla()</c> function; a named colour; or the keywords
    /// <c>transparent</c> and <c>currentcolor</c>.
    /// </param>
    /// <param name="currentColor">
    /// Colour substituted for <c>currentcolor</c>, normally the computed
    /// value of the <c>color</c> property.
    /// </param>
    /// <param name="color">
    /// Receives the packed ARGB colour on success, otherwise
    /// <see cref="Transparent"/>.
    /// </param>
    /// <returns><see langword="true"/> when the value was recognised.</returns>
    /// <remarks>
    /// <para>
    /// Parsing is trimmed and case-insensitive. Function arguments accept both
    /// legacy comma syntax and modern space/slash syntax, and channel
    /// percentages are scaled to 0–255.
    /// </para>
    /// <para>
    /// <c>hsl()</c> accepts a <c>deg</c> suffix on the hue; a bare number is
    /// treated as degrees, and the hue wraps modulo 360. Saturation and
    /// lightness accept <c>%</c> or a bare number, both interpreted as
    /// percentages. Hue interpolation is circular.
    /// </para>
    /// <para>
    /// <c>transparent</c> succeeds and yields <see cref="Transparent"/>. This
    /// method does not support <c>color()</c>, <c>lab()</c>, <c>lch()</c>,
    /// <c>oklab()</c>, <c>oklch()</c> or relative colour syntax, and does not
    /// resolve system colours. Allocations are limited to the argument split
    /// for functional forms.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// if (CssColor.TryParse("#ff8800", CssColor.Black, out var colour))
    /// {
    ///     // colour == 0xFFFF8800
    /// }
    /// </code>
    /// </example>
    /// <seealso cref="ParseOr(string?, uint, uint)"/>
    public static bool TryParse(string? value, uint currentColor, out uint color)
    {
        color = Transparent;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();

        if (text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Equals("currentcolor", StringComparison.OrdinalIgnoreCase)
            || text.Equals("current-color", StringComparison.OrdinalIgnoreCase))
        {
            color = currentColor;
            return true;
        }

        if (text[0] == '#')
        {
            return TryParseHex(text.AsSpan(1), out color);
        }

        var open = text.IndexOf('(');

        if (open > 0 && text.EndsWith(')'))
        {
            var name = text[..open].Trim();
            var arguments = text[(open + 1)..^1];

            if (name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseRgb(arguments, out color);
            }

            if (name.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsla", StringComparison.OrdinalIgnoreCase))
            {
                return TryParseHsl(arguments, out color);
            }
        }

        return NamedColors.TryGetValue(text, out color);
    }

    /// <summary>Parses a colour, returning a fallback when the value is invalid.</summary>
    /// <param name="value">
    /// Colour text in any form accepted by
    /// <see cref="TryParse(string?, uint, out uint)"/>.
    /// </param>
    /// <param name="fallback">Colour to return when parsing fails.</param>
    /// <param name="currentColor">
    /// Colour substituted for <c>currentcolor</c>; defaults to
    /// <see cref="Black"/>.
    /// </param>
    /// <returns>The parsed colour, or <paramref name="fallback"/>.</returns>
    /// <remarks>
    /// Convenience wrapper for declaration parsing, where an invalid colour
    /// must not abort the rule.
    /// </remarks>
    public static uint ParseOr(string? value, uint fallback, uint currentColor = Black)
    {
        return TryParse(value, currentColor, out var color) ? color : fallback;
    }

    /// <summary>Parses the digits of a hex colour, without the leading <c>#</c>.</summary>
    /// <param name="hex">Hex digits: 3, 4, 6 or 8 characters.</param>
    /// <param name="color">Receives the packed colour on success.</param>
    /// <returns><see langword="true"/> for a valid length with only hex digits.</returns>
    /// <remarks>
    /// Short forms expand each nibble by 17 (for example <c>#f00</c> becomes
    /// <c>#ff0000</c>) and a missing alpha defaults to 255. Any other length,
    /// or a non-hex character, fails the parse.
    /// </remarks>
    private static bool TryParseHex(ReadOnlySpan<char> hex, out uint color)
    {
        color = Transparent;

        static int Hex(char c)
        {
            if (c is >= '0' and <= '9') return c - '0';
            if (c is >= 'a' and <= 'f') return c - 'a' + 10;
            if (c is >= 'A' and <= 'F') return c - 'A' + 10;
            return -1;
        }

        if (hex.Length is 3 or 4)
        {
            var r = Hex(hex[0]);
            var g = Hex(hex[1]);
            var b = Hex(hex[2]);
            var a = hex.Length == 4 ? Hex(hex[3]) : 15;

            if (r < 0 || g < 0 || b < 0 || a < 0)
            {
                return false;
            }

            color = Pack((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), (byte)(a * 17));
            return true;
        }

        if (hex.Length is 6 or 8)
        {
            Span<int> values = stackalloc int[4];

            for (var i = 0; i < hex.Length / 2; i++)
            {
                var high = Hex(hex[i * 2]);
                var low = Hex(hex[i * 2 + 1]);

                if (high < 0 || low < 0)
                {
                    return false;
                }

                values[i] = high * 16 + low;
            }

            color = Pack(
                (byte)values[0],
                (byte)values[1],
                (byte)values[2],
                hex.Length == 8 ? (byte)values[3] : (byte)255);
            return true;
        }

        return false;
    }

    /// <summary>Parses the argument list of <c>rgb()</c> or <c>rgba()</c>.</summary>
    /// <param name="arguments">Text between the parentheses.</param>
    /// <param name="color">Receives the packed colour on success.</param>
    /// <returns><see langword="true"/> when at least three valid channels were found.</returns>
    /// <remarks>
    /// Commas, slashes and whitespace are all treated as separators, so legacy
    /// and modern syntaxes are accepted interchangeably. Percentage channels
    /// are scaled by 2.55; numeric channels are clamped to 0–255. The optional
    /// fourth component is alpha. Extra components beyond the fourth are
    /// ignored rather than rejected.
    /// </remarks>
    private static bool TryParseRgb(string arguments, out uint color)
    {
        color = Transparent;

        var pieces = arguments
            .Replace('/', ' ')
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pieces.Length < 3)
        {
            return false;
        }

        if (!TryComponent(pieces[0], out var r)
            || !TryComponent(pieces[1], out var g)
            || !TryComponent(pieces[2], out var b))
        {
            return false;
        }

        var a = 255f;

        if (pieces.Length >= 4 && !TryAlpha(pieces[3], out a))
        {
            return false;
        }

        color = Pack((byte)r, (byte)g, (byte)b, (byte)a);
        return true;
    }

    /// <summary>Parses the argument list of <c>hsl()</c> or <c>hsla()</c>.</summary>
    /// <param name="arguments">Text between the parentheses.</param>
    /// <param name="color">Receives the packed colour on success.</param>
    /// <returns><see langword="true"/> when the hue, saturation and lightness were valid.</returns>
    /// <remarks>
    /// The hue may carry a <c>deg</c> suffix and wraps modulo 360. Saturation
    /// and lightness are read as percentages (a trailing <c>%</c> is optional)
    /// and clamped to 0–100. Conversion uses the standard HSL-to-RGB algorithm
    /// with <c>q</c>/<c>p</c> helper values; the optional fourth component is
    /// alpha. <c>rad</c>, <c>grad</c> and <c>turn</c> hue units are not
    /// supported and fall back to the numeric prefix.
    /// </remarks>
    private static bool TryParseHsl(string arguments, out uint color)
    {
        color = Transparent;

        var pieces = arguments
            .Replace('/', ' ')
            .Replace(',', ' ')
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (pieces.Length < 3)
        {
            return false;
        }

        var hueText = pieces[0];
        var hue = hueText.EndsWith("deg", StringComparison.OrdinalIgnoreCase)
            ? CssValueParser.ParseNumber(hueText[..^3])
            : CssValueParser.ParseNumber(hueText);

        if (!TryPercent(pieces[1], out var saturation)
            || !TryPercent(pieces[2], out var lightness))
        {
            return false;
        }

        var alpha = 255f;

        if (pieces.Length >= 4 && !TryAlpha(pieces[3], out alpha))
        {
            return false;
        }

        var h = ((hue % 360) + 360) % 360 / 360f;
        var s = Math.Clamp(saturation / 100f, 0, 1);
        var l = Math.Clamp(lightness / 100f, 0, 1);

        var q = l < 0.5f ? l * (1 + s) : l + s - l * s;
        var p = 2 * l - q;

        color = Pack(
            (byte)Math.Clamp((int)MathF.Round(HueToRgb(p, q, h + 1f / 3f) * 255), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(HueToRgb(p, q, h) * 255), 0, 255),
            (byte)Math.Clamp((int)MathF.Round(HueToRgb(p, q, h - 1f / 3f) * 255), 0, 255),
            (byte)alpha);
        return true;
    }

    /// <summary>Converts one HSL hue offset to a normalised RGB channel.</summary>
    /// <param name="p">The smaller intermediate lightness value.</param>
    /// <param name="q">The larger intermediate lightness value.</param>
    /// <param name="t">Hue offset in turns; wrapped into 0–1.</param>
    /// <returns>A channel in the range 0–1.</returns>
    private static float HueToRgb(float p, float q, float t)
    {
        if (t < 0) t += 1;
        if (t > 1) t -= 1;
        if (t < 1f / 6f) return p + (q - p) * 6 * t;
        if (t < 1f / 2f) return q;
        if (t < 2f / 3f) return p + (q - p) * (2f / 3f - t) * 6;
        return p;
    }

    /// <summary>Parses one <c>rgb()</c> channel, accepting a percentage or a number.</summary>
    /// <param name="text">Channel text, optionally suffixed with <c>%</c>.</param>
    /// <param name="value">Receives the channel in the range 0–255.</param>
    /// <returns>Always <see langword="true"/>; invalid text is parsed as zero.</returns>
    /// <remarks>
    /// Percentages are multiplied by 2.55. The method is deliberately lenient
    /// because the surrounding declaration is validated by the CSS parser.
    /// </remarks>
    private static bool TryComponent(string text, out float value)
    {
        if (text.EndsWith('%'))
        {
            value = Math.Clamp(CssValueParser.ParseNumber(text[..^1]) * 2.55f, 0, 255);
            return true;
        }

        value = CssValueParser.ParseNumber(text);
        value = Math.Clamp(value, 0, 255);
        return true;
    }

    /// <summary>Parses an HSL saturation or lightness component.</summary>
    /// <param name="text">Component text, optionally suffixed with <c>%</c>.</param>
    /// <param name="value">Receives the percentage value, not normalised to 0–1.</param>
    /// <returns>Always <see langword="true"/>; invalid text is parsed as zero.</returns>
    /// <remarks>Clamping happens later in <see cref="TryParseHsl(string, out uint)"/>.</remarks>
    private static bool TryPercent(string text, out float value)
    {
        value = text.EndsWith('%')
            ? CssValueParser.ParseNumber(text[..^1])
            : CssValueParser.ParseNumber(text);

        return true;
    }

    /// <summary>Parses an alpha component into the 0–255 range.</summary>
    /// <param name="text">Alpha text, optionally suffixed with <c>%</c>.</param>
    /// <param name="alpha">Receives the alpha scaled to 0–255.</param>
    /// <returns>Always <see langword="true"/>; invalid text is parsed as zero.</returns>
    /// <remarks>
    /// A bare number is treated as a 0-1 fraction and multiplied by 255; a
    /// percentage is scaled by 2.55. Invalid text falls back to an opaque
    /// alpha because <see cref="CssValueParser.ParseNumber(string?, float)"/> is
    /// called with a fallback of 1.
    /// </remarks>
    private static bool TryAlpha(string text, out float alpha)
    {
        alpha = text.EndsWith('%')
            ? CssValueParser.ParseNumber(text[..^1]) * 2.55f
            : CssValueParser.ParseNumber(text, 1) * 255f;

        alpha = Math.Clamp(alpha, 0, 255);
        return true;
    }

    /// <summary>
    /// The CSS named colours, keyed by lower-case name and compared without
    /// case sensitivity.
    /// </summary>
    /// <remarks>
    /// Values are fully opaque <c>0xAARRGGBB</c> literals. The table is
    /// initialised once and never mutated, so concurrent reads are safe. It
    /// contains the CSS Color Level 4 named colours, including
    /// <c>rebeccapurple</c>; <c>transparent</c> and <c>currentcolor</c> are
    /// handled separately by <see cref="TryParse(string?, uint, out uint)"/>
    /// before the lookup.
    /// </remarks>
    private static readonly Dictionary<string, uint> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = 0xFFF0F8FF,
        ["antiquewhite"] = 0xFFFAEBD7,
        ["aqua"] = 0xFF00FFFF,
        ["aquamarine"] = 0xFF7FFFD4,
        ["azure"] = 0xFFF0FFFF,
        ["beige"] = 0xFFF5F5DC,
        ["bisque"] = 0xFFFFE4C4,
        ["black"] = 0xFF000000,
        ["blanchedalmond"] = 0xFFFFEBCD,
        ["blue"] = 0xFF0000FF,
        ["blueviolet"] = 0xFF8A2BE2,
        ["brown"] = 0xFFA52A2A,
        ["burlywood"] = 0xFFDEB887,
        ["cadetblue"] = 0xFF5F9EA0,
        ["chartreuse"] = 0xFF7FFF00,
        ["chocolate"] = 0xFFD2691E,
        ["coral"] = 0xFFFF7F50,
        ["cornflowerblue"] = 0xFF6495ED,
        ["cornsilk"] = 0xFFFFF8DC,
        ["crimson"] = 0xFFDC143C,
        ["cyan"] = 0xFF00FFFF,
        ["darkblue"] = 0xFF00008B,
        ["darkcyan"] = 0xFF008B8B,
        ["darkgoldenrod"] = 0xFFB8860B,
        ["darkgray"] = 0xFFA9A9A9,
        ["darkgreen"] = 0xFF006400,
        ["darkgrey"] = 0xFFA9A9A9,
        ["darkkhaki"] = 0xFFBDB76B,
        ["darkmagenta"] = 0xFF8B008B,
        ["darkolivegreen"] = 0xFF556B2F,
        ["darkorange"] = 0xFFFF8C00,
        ["darkorchid"] = 0xFF9932CC,
        ["darkred"] = 0xFF8B0000,
        ["darksalmon"] = 0xFFE9967A,
        ["darkseagreen"] = 0xFF8FBC8F,
        ["darkslateblue"] = 0xFF483D8B,
        ["darkslategray"] = 0xFF2F4F4F,
        ["darkslategrey"] = 0xFF2F4F4F,
        ["darkturquoise"] = 0xFF00CED1,
        ["darkviolet"] = 0xFF9400D3,
        ["deeppink"] = 0xFFFF1493,
        ["deepskyblue"] = 0xFF00BFFF,
        ["dimgray"] = 0xFF696969,
        ["dimgrey"] = 0xFF696969,
        ["dodgerblue"] = 0xFF1E90FF,
        ["firebrick"] = 0xFFB22222,
        ["floralwhite"] = 0xFFFFFAF0,
        ["forestgreen"] = 0xFF228B22,
        ["fuchsia"] = 0xFFFF00FF,
        ["gainsboro"] = 0xFFDCDCDC,
        ["ghostwhite"] = 0xFFF8F8FF,
        ["gold"] = 0xFFFFD700,
        ["goldenrod"] = 0xFFDAA520,
        ["gray"] = 0xFF808080,
        ["green"] = 0xFF008000,
        ["greenyellow"] = 0xFFADFF2F,
        ["grey"] = 0xFF808080,
        ["honeydew"] = 0xFFF0FFF0,
        ["hotpink"] = 0xFFFF69B4,
        ["indianred"] = 0xFFCD5C5C,
        ["indigo"] = 0xFF4B0082,
        ["ivory"] = 0xFFFFFFF0,
        ["khaki"] = 0xFFF0E68C,
        ["lavender"] = 0xFFE6E6FA,
        ["lavenderblush"] = 0xFFFFF0F5,
        ["lawngreen"] = 0xFF7CFC00,
        ["lemonchiffon"] = 0xFFFFFACD,
        ["lightblue"] = 0xFFADD8E6,
        ["lightcoral"] = 0xFFF08080,
        ["lightcyan"] = 0xFFE0FFFF,
        ["lightgoldenrodyellow"] = 0xFFFAFAD2,
        ["lightgray"] = 0xFFD3D3D3,
        ["lightgreen"] = 0xFF90EE90,
        ["lightgrey"] = 0xFFD3D3D3,
        ["lightpink"] = 0xFFFFB6C1,
        ["lightsalmon"] = 0xFFFFA07A,
        ["lightseagreen"] = 0xFF20B2AA,
        ["lightskyblue"] = 0xFF87CEFA,
        ["lightslategray"] = 0xFF778899,
        ["lightslategrey"] = 0xFF778899,
        ["lightsteelblue"] = 0xFFB0C4DE,
        ["lightyellow"] = 0xFFFFFFE0,
        ["lime"] = 0xFF00FF00,
        ["limegreen"] = 0xFF32CD32,
        ["linen"] = 0xFFFAF0E6,
        ["magenta"] = 0xFFFF00FF,
        ["maroon"] = 0xFF800000,
        ["mediumaquamarine"] = 0xFF66CDAA,
        ["mediumblue"] = 0xFF0000CD,
        ["mediumorchid"] = 0xFFBA55D3,
        ["mediumpurple"] = 0xFF9370DB,
        ["mediumseagreen"] = 0xFF3CB371,
        ["mediumslateblue"] = 0xFF7B68EE,
        ["mediumspringgreen"] = 0xFF00FA9A,
        ["mediumturquoise"] = 0xFF48D1CC,
        ["mediumvioletred"] = 0xFFC71585,
        ["midnightblue"] = 0xFF191970,
        ["mintcream"] = 0xFFF5FFFA,
        ["mistyrose"] = 0xFFFFE4E1,
        ["moccasin"] = 0xFFFFE4B5,
        ["navajowhite"] = 0xFFFFDEAD,
        ["navy"] = 0xFF000080,
        ["oldlace"] = 0xFFFDF5E6,
        ["olive"] = 0xFF808000,
        ["olivedrab"] = 0xFF6B8E23,
        ["orange"] = 0xFFFFA500,
        ["orangered"] = 0xFFFF4500,
        ["orchid"] = 0xFFDA70D6,
        ["palegoldenrod"] = 0xFFEEE8AA,
        ["palegreen"] = 0xFF98FB98,
        ["paleturquoise"] = 0xFFAFEEEE,
        ["palevioletred"] = 0xFFDB7093,
        ["papayawhip"] = 0xFFFFEFD5,
        ["peachpuff"] = 0xFFFFDAB9,
        ["peru"] = 0xFFCD853F,
        ["pink"] = 0xFFFFC0CB,
        ["plum"] = 0xFFDDA0DD,
        ["powderblue"] = 0xFFB0E0E6,
        ["purple"] = 0xFF800080,
        ["rebeccapurple"] = 0xFF663399,
        ["red"] = 0xFFFF0000,
        ["rosybrown"] = 0xFFBC8F8F,
        ["royalblue"] = 0xFF4169E1,
        ["saddlebrown"] = 0xFF8B4513,
        ["salmon"] = 0xFFFA8072,
        ["sandybrown"] = 0xFFF4A460,
        ["seagreen"] = 0xFF2E8B57,
        ["seashell"] = 0xFFFFF5EE,
        ["sienna"] = 0xFFA0522D,
        ["silver"] = 0xFFC0C0C0,
        ["skyblue"] = 0xFF87CEEB,
        ["slateblue"] = 0xFF6A5ACD,
        ["slategray"] = 0xFF708090,
        ["slategrey"] = 0xFF708090,
        ["snow"] = 0xFFFFFAFA,
        ["springgreen"] = 0xFF00FF7F,
        ["steelblue"] = 0xFF4682B4,
        ["tan"] = 0xFFD2B48C,
        ["teal"] = 0xFF008080,
        ["thistle"] = 0xFFD8BFD8,
        ["tomato"] = 0xFFFF6347,
        ["turquoise"] = 0xFF40E0D0,
        ["violet"] = 0xFFEE82EE,
        ["wheat"] = 0xFFF5DEB3,
        ["white"] = 0xFFFFFFFF,
        ["whitesmoke"] = 0xFFF5F5F5,
        ["yellow"] = 0xFFFFFF00,
        ["yellowgreen"] = 0xFF9ACD32
    };
}
