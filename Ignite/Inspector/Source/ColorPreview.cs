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
