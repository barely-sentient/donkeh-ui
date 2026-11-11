using System.Globalization;

namespace Ignite.Inspector;

/// <summary>
/// Parses the colour formats the inspector can preview next to a declaration.
/// The result is packed ARGB, matching the layout engine's colour convention.
/// </summary>
public static class ColorPreview
{
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

    public static bool IsColor(string? text) => TryParse(text, out _);

    /// <summary>Formats packed ARGB as a CSS hex colour.</summary>
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

    /// <summary>Converts packed ARGB to hue, saturation and value in 0..1.</summary>
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

    /// <summary>Converts hue, saturation and value in 0..1 to packed ARGB.</summary>
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

    private static bool TryHexByte(ReadOnlySpan<char> text, out int value) =>
        int.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);

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
