using System.Globalization;
using System.Xml.Linq;

namespace Donkeh.Windowing;

/// <summary>
/// A small SVG rasterizer for the icon-sized documents used by stylesheets.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately focused on the subset Bootstrap and similar component
/// libraries use for icons: paths, rectangles, circles, ellipses, polygons,
/// polylines and lines with fill or stroke. It is not a general SVG renderer and
/// does not implement filters, masks, text, gradients or embedded images.
/// </para>
/// <para>
/// Geometry is flattened into polygons in SVG user units and rendered with a
/// four-times supersampled scanline fill, then box-filtered down to the requested
/// pixel size. The result is tightly packed, top-down RGBA suitable for
/// <see cref="ImageCache"/>.
/// </para>
/// </remarks>
internal static class SvgRasterizer
{
    /// <summary>Renders an SVG data URI to tightly packed RGBA pixels.</summary>
    /// <param name="source">The complete <c>data:image/svg+xml</c> source.</param>
    /// <param name="pixels">Receives the RGBA buffer.</param>
    /// <param name="width">Receives the bitmap width.</param>
    /// <param name="height">Receives the bitmap height.</param>
    /// <returns><see langword="true"/> when the document could be rendered.</returns>
    public static bool TryRender(string source, out byte[] pixels, out int width, out int height)
    {
        pixels = [];
        width = 0;
        height = 0;

        if (!TryGetSvgText(source, out var text))
        {
            return false;
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            var root = document.Root;

            if (root is null || !root.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!TryReadViewBox(root, out var viewWidth, out var viewHeight, out var viewX, out var viewY))
            {
                return false;
            }

            const int target = 64;
            const int supersample = 4;
            var scale = target / MathF.Max(viewWidth, viewHeight);
            var pixelWidth = Math.Max(1, (int) MathF.Round(viewWidth * scale));
            var pixelHeight = Math.Max(1, (int) MathF.Round(viewHeight * scale));
            var sampleWidth = pixelWidth * supersample;
            var sampleHeight = pixelHeight * supersample;
            var sampleScale = sampleWidth / viewWidth;

            var canvas = new Canvas(sampleWidth, sampleHeight);

            foreach (var child in root.Elements())
            {
                DrawElement(canvas, child, sampleScale, viewX, viewY, root);
            }

            pixels = canvas.Downsample(supersample, pixelWidth, pixelHeight);
            width = pixelWidth;
            height = pixelHeight;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetSvgText(string source, out string text)
    {
        text = string.Empty;

        if (!source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var comma = source.IndexOf(',');

        if (comma < 0)
        {
            return false;
        }

        var header = source[..comma];

        if (!header.Contains("svg", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var payload = source[(comma + 1)..];

        try
        {
            text = header.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload))
                : Uri.UnescapeDataString(payload);
            return text.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryReadViewBox(
        XElement root,
        out float width,
        out float height,
        out float x,
        out float y)
    {
        width = 0;
        height = 0;
        x = 0;
        y = 0;

        var viewBox = (string?) root.Attribute("viewBox");

        if (!string.IsNullOrWhiteSpace(viewBox))
        {
            var parts = SplitNumbers(viewBox!);

            if (parts.Count == 4)
            {
                x = parts[0];
                y = parts[1];
                width = MathF.Abs(parts[2]);
                height = MathF.Abs(parts[3]);
            }
        }

        if (width <= 0 || height <= 0)
        {
            width = ParseLength(root.Attribute("width")?.Value, 16);
            height = ParseLength(root.Attribute("height")?.Value, 16);
        }

        return width > 0 && height > 0;
    }

    private static float ParseLength(string? value, float fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        var end = 0;

        while (end < value!.Length
            && (char.IsDigit(value[end]) || value[end] is '.' or '-' or '+' or 'e' or 'E'))
        {
            end++;
        }

        return end > 0 && float.TryParse(
            value.AsSpan(0, end),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out var parsed)
            ? parsed
            : fallback;
    }

    private static void DrawElement(
        Canvas canvas,
        XElement element,
        float scale,
        float viewX,
        float viewY,
        XElement inherited)
    {
        var fill = (string?) element.Attribute("fill") ?? (string?) inherited.Attribute("fill") ?? "black";
        var stroke = (string?) element.Attribute("stroke") ?? (string?) inherited.Attribute("stroke") ?? "none";
        var fillRule = (string?) element.Attribute("fill-rule") ?? "nonzero";
        var strokeWidth = ParseLength(
            (string?) element.Attribute("stroke-width") ?? (string?) inherited.Attribute("stroke-width"),
            1);
        var fillColor = ParseColor(fill);
        var strokeColor = ParseColor(stroke);

        switch (element.Name.LocalName.ToLowerInvariant())
        {
            case "g":
            case "svg":
                foreach (var child in element.Elements())
                {
                    DrawElement(canvas, child, scale, viewX, viewY, element);
                }

                break;

            case "path":
            {
                var data = (string?) element.Attribute("d");

                if (string.IsNullOrWhiteSpace(data))
                {
                    break;
                }

                var polygons = PathParser.Parse(data!, viewX, viewY);

                if (fillColor.A > 0)
                {
                    foreach (var polygon in polygons)
                    {
                        canvas.FillPolygon(polygon, scale, fillColor, fillRule == "evenodd");
                    }
                }

                if (strokeColor.A > 0)
                {
                    foreach (var polygon in polygons)
                    {
                        canvas.StrokePolyline(polygon, scale, strokeWidth, strokeColor);
                    }
                }

                break;
            }

            case "rect":
            {
                var x = ParseLength((string?) element.Attribute("x"), 0);
                var y = ParseLength((string?) element.Attribute("y"), 0);
                var w = ParseLength((string?) element.Attribute("width"), 0);
                var h = ParseLength((string?) element.Attribute("height"), 0);
                var points = new List<PointF>
                {
                    new(x - viewX, y - viewY),
                    new(x + w - viewX, y - viewY),
                    new(x + w - viewX, y + h - viewY),
                    new(x - viewX, y + h - viewY)
                };

                if (fillColor.A > 0)
                {
                    canvas.FillPolygon(points, scale, fillColor, false);
                }

                if (strokeColor.A > 0)
                {
                    points.Add(points[0]);
                    canvas.StrokePolyline(points, scale, strokeWidth, strokeColor);
                }

                break;
            }

            case "circle":
            {
                var cx = ParseLength((string?) element.Attribute("cx"), 0) - viewX;
                var cy = ParseLength((string?) element.Attribute("cy"), 0) - viewY;
                var r = ParseLength((string?) element.Attribute("r"), 0);
                var points = Ellipse(cx, cy, r, r);

                if (fillColor.A > 0)
                {
                    canvas.FillPolygon(points, scale, fillColor, false);
                }

                if (strokeColor.A > 0)
                {
                    points.Add(points[0]);
                    canvas.StrokePolyline(points, scale, strokeWidth, strokeColor);
                }

                break;
            }

            case "ellipse":
            {
                var cx = ParseLength((string?) element.Attribute("cx"), 0) - viewX;
                var cy = ParseLength((string?) element.Attribute("cy"), 0) - viewY;
                var rx = ParseLength((string?) element.Attribute("rx"), 0);
                var ry = ParseLength((string?) element.Attribute("ry"), 0);
                var points = Ellipse(cx, cy, rx, ry);

                if (fillColor.A > 0)
                {
                    canvas.FillPolygon(points, scale, fillColor, false);
                }

                if (strokeColor.A > 0)
                {
                    points.Add(points[0]);
                    canvas.StrokePolyline(points, scale, strokeWidth, strokeColor);
                }

                break;
            }

            case "polygon":
            case "polyline":
            {
                var raw = (string?) element.Attribute("points");

                if (string.IsNullOrWhiteSpace(raw))
                {
                    break;
                }

                var numbers = SplitNumbers(raw!);
                var points = new List<PointF>(numbers.Count / 2);

                for (var i = 0; i + 1 < numbers.Count; i += 2)
                {
                    points.Add(new PointF(numbers[i] - viewX, numbers[i + 1] - viewY));
                }

                if (element.Name.LocalName.Equals("polygon", StringComparison.OrdinalIgnoreCase))
                {
                    if (fillColor.A > 0)
                    {
                        canvas.FillPolygon(points, scale, fillColor, false);
                    }
                }

                if (strokeColor.A > 0)
                {
                    canvas.StrokePolyline(points, scale, strokeWidth, strokeColor);
                }

                break;
            }

            case "line":
            {
                if (strokeColor.A == 0)
                {
                    break;
                }

                var x1 = ParseLength((string?) element.Attribute("x1"), 0) - viewX;
                var y1 = ParseLength((string?) element.Attribute("y1"), 0) - viewY;
                var x2 = ParseLength((string?) element.Attribute("x2"), 0) - viewX;
                var y2 = ParseLength((string?) element.Attribute("y2"), 0) - viewY;
                canvas.StrokePolyline([new PointF(x1, y1), new PointF(x2, y2)], scale, strokeWidth, strokeColor);
                break;
            }
        }
    }

    private static List<PointF> Ellipse(float cx, float cy, float rx, float ry)
    {
        var points = new List<PointF>(33);

        for (var i = 0; i < 32; i++)
        {
            var angle = MathF.PI * 2 * i / 32;
            points.Add(new PointF(cx + MathF.Cos(angle) * rx, cy + MathF.Sin(angle) * ry));
        }

        return points;
    }

    private static List<float> SplitNumbers(string text)
    {
        var values = new List<float>();
        var index = 0;

        while (index < text.Length)
        {
            while (index < text.Length && (char.IsWhiteSpace(text[index]) || text[index] == ','))
            {
                index++;
            }

            if (index >= text.Length)
            {
                break;
            }

            var next = ReadNumber(text, index, out var value);

            if (next > index)
            {
                values.Add(value);
                index = next;
            }
            else
            {
                index++;
            }
        }

        return values;
    }

    /// <summary>Reads one SVG number, stopping before a second decimal point.</summary>
    /// <param name="text">Source text.</param>
    /// <param name="start">First character to inspect.</param>
    /// <param name="value">Receives the parsed value.</param>
    /// <returns>The index one past the number, or <paramref name="start"/> on failure.</returns>
    private static int ReadNumber(string text, int start, out float value)
    {
        value = 0;
        var index = start;

        if (index < text.Length && text[index] is '-' or '+')
        {
            index++;
        }

        var hasDigits = false;
        var hasDot = false;
        var hasExponent = false;

        while (index < text.Length)
        {
            var c = text[index];

            if (char.IsDigit(c))
            {
                hasDigits = true;
                index++;
                continue;
            }

            if (c == '.' && !hasDot && !hasExponent)
            {
                hasDot = true;
                index++;
                continue;
            }

            if ((c == 'e' || c == 'E') && hasDigits && !hasExponent)
            {
                hasExponent = true;
                index++;

                if (index < text.Length && text[index] is '-' or '+')
                {
                    index++;
                }

                continue;
            }

            break;
        }

        return hasDigits
            && float.TryParse(text.AsSpan(start, index - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value)
            ? index
            : start;
    }

    private static ColorF ParseColor(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)
            || text.Equals("none", StringComparison.OrdinalIgnoreCase)
            || text.Equals("transparent", StringComparison.OrdinalIgnoreCase))
        {
            return default;
        }

        text = text.Trim();

        if (text.StartsWith('#'))
        {
            var hex = text[1..];

            if (hex.Length is 3 or 4)
            {
                var r = Convert.ToInt32(new string(hex[0], 2), 16);
                var g = Convert.ToInt32(new string(hex[1], 2), 16);
                var b = Convert.ToInt32(new string(hex[2], 2), 16);
                var a = hex.Length == 4 ? Convert.ToInt32(new string(hex[3], 2), 16) : 255;
                return new ColorF(r, g, b, a);
            }

            if (hex.Length is 6 or 8)
            {
                var r = Convert.ToInt32(hex[..2], 16);
                var g = Convert.ToInt32(hex.Substring(2, 2), 16);
                var b = Convert.ToInt32(hex.Substring(4, 2), 16);
                var a = hex.Length == 8 ? Convert.ToInt32(hex.Substring(6, 2), 16) : 255;
                return new ColorF(r, g, b, a);
            }
        }

        if (text.StartsWith("rgb", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("rgba", StringComparison.OrdinalIgnoreCase))
        {
            var open = text.IndexOf('(');
            var close = text.LastIndexOf(')');

            if (open >= 0 && close > open)
            {
                var parts = SplitNumbers(text[(open + 1)..close]);

                if (parts.Count >= 3)
                {
                    var alpha = parts.Count > 3 ? Math.Clamp(parts[3], 0, 1) : 1;
                    return new ColorF(parts[0], parts[1], parts[2], alpha * 255);
                }
            }
        }

        return text.ToLowerInvariant() switch
        {
            "white" => new ColorF(255, 255, 255, 255),
            "black" => new ColorF(0, 0, 0, 255),
            "red" => new ColorF(255, 0, 0, 255),
            "green" => new ColorF(0, 128, 0, 255),
            "blue" => new ColorF(0, 0, 255, 255),
            "gray" or "grey" => new ColorF(128, 128, 128, 255),
            "currentcolor" => new ColorF(33, 37, 41, 255),
            _ => default
        };
    }

    private readonly record struct PointF(float X, float Y);

    private readonly record struct ColorF(float R, float G, float B, float A);

    /// <summary>Simple RGBA sample buffer with polygon fill and stroke helpers.</summary>
    private sealed class Canvas(int width, int height)
    {
        private readonly float[] _r = new float[width * height];
        private readonly float[] _g = new float[width * height];
        private readonly float[] _b = new float[width * height];
        private readonly float[] _a = new float[width * height];

        public void FillPolygon(List<PointF> points, float scale, ColorF color, bool evenOdd)
        {
            if (points.Count < 3)
            {
                return;
            }

            var minY = float.MaxValue;
            var maxY = float.MinValue;
            var mapped = new (float X, float Y)[points.Count];

            for (var i = 0; i < points.Count; i++)
            {
                mapped[i] = (points[i].X * scale, points[i].Y * scale);
                minY = MathF.Min(minY, mapped[i].Y);
                maxY = MathF.Max(maxY, mapped[i].Y);
            }

            var yStart = Math.Clamp((int) MathF.Floor(minY), 0, height - 1);
            var yEnd = Math.Clamp((int) MathF.Ceiling(maxY), 0, height - 1);
            var intersections = new List<float>(points.Count);

            for (var y = yStart; y <= yEnd; y++)
            {
                var scanY = y + 0.5f;
                intersections.Clear();

                for (var i = 0; i < mapped.Length; i++)
                {
                    var a = mapped[i];
                    var b = mapped[(i + 1) % mapped.Length];

                    if ((a.Y <= scanY && b.Y > scanY) || (b.Y <= scanY && a.Y > scanY))
                    {
                        var t = (scanY - a.Y) / (b.Y - a.Y);
                        intersections.Add(a.X + t * (b.X - a.X));
                    }
                }

                if (intersections.Count < 2)
                {
                    continue;
                }

                intersections.Sort();

                for (var i = 0; i + 1 < intersections.Count; i += 2)
                {
                    var x0 = Math.Clamp((int) MathF.Ceiling(intersections[i] - 0.5f), 0, width - 1);
                    var x1 = Math.Clamp((int) MathF.Floor(intersections[i + 1] - 0.5f), 0, width - 1);

                    for (var x = x0; x <= x1; x++)
                    {
                        Blend(y * width + x, color);
                    }
                }
            }
        }

        public void StrokePolyline(List<PointF> points, float scale, float strokeWidth, ColorF color)
        {
            if (points.Count == 0 || color.A <= 0)
            {
                return;
            }

            var radius = MathF.Max(0.35f, strokeWidth * scale * 0.5f);

            for (var i = 0; i + 1 < points.Count; i++)
            {
                var ax = points[i].X * scale;
                var ay = points[i].Y * scale;
                var bx = points[i + 1].X * scale;
                var by = points[i + 1].Y * scale;
                var minX = Math.Clamp((int) MathF.Floor(MathF.Min(ax, bx) - radius), 0, width - 1);
                var maxX = Math.Clamp((int) MathF.Ceiling(MathF.Max(ax, bx) + radius), 0, width - 1);
                var minY = Math.Clamp((int) MathF.Floor(MathF.Min(ay, by) - radius), 0, height - 1);
                var maxY = Math.Clamp((int) MathF.Ceiling(MathF.Max(ay, by) + radius), 0, height - 1);
                var dx = bx - ax;
                var dy = by - ay;
                var lengthSquared = dx * dx + dy * dy;

                for (var y = minY; y <= maxY; y++)
                {
                    for (var x = minX; x <= maxX; x++)
                    {
                        var px = x + 0.5f;
                        var py = y + 0.5f;
                        var t = lengthSquared > 0
                            ? Math.Clamp(((px - ax) * dx + (py - ay) * dy) / lengthSquared, 0, 1)
                            : 0;
                        var cx = ax + t * dx;
                        var cy = ay + t * dy;
                        var distanceSquared = (px - cx) * (px - cx) + (py - cy) * (py - cy);

                        if (distanceSquared <= radius * radius)
                        {
                            Blend(y * width + x, color);
                        }
                    }
                }
            }
        }

        private void Blend(int index, ColorF color)
        {
            var srcA = color.A / 255f;

            if (srcA <= 0)
            {
                return;
            }

            var dstA = _a[index];
            var outA = srcA + dstA * (1 - srcA);

            if (outA <= 0)
            {
                return;
            }

            _r[index] = (color.R * srcA + _r[index] * dstA * (1 - srcA)) / outA;
            _g[index] = (color.G * srcA + _g[index] * dstA * (1 - srcA)) / outA;
            _b[index] = (color.B * srcA + _b[index] * dstA * (1 - srcA)) / outA;
            _a[index] = outA;
        }

        public byte[] Downsample(int factor, int targetWidth, int targetHeight)
        {
            var output = new byte[targetWidth * targetHeight * 4];

            for (var y = 0; y < targetHeight; y++)
            {
                for (var x = 0; x < targetWidth; x++)
                {
                    var r = 0f;
                    var g = 0f;
                    var b = 0f;
                    var a = 0f;

                    for (var sy = 0; sy < factor; sy++)
                    {
                        var sourceY = y * factor + sy;

                        for (var sx = 0; sx < factor; sx++)
                        {
                            var index = sourceY * width + x * factor + sx;
                            var alpha = _a[index];
                            r += _r[index] * alpha;
                            g += _g[index] * alpha;
                            b += _b[index] * alpha;
                            a += alpha;
                        }
                    }

                    var target = (y * targetWidth + x) * 4;

                    if (a > 0)
                    {
                        output[target] = (byte) Math.Clamp(r / a, 0, 255);
                        output[target + 1] = (byte) Math.Clamp(g / a, 0, 255);
                        output[target + 2] = (byte) Math.Clamp(b / a, 0, 255);
                        output[target + 3] = (byte) Math.Clamp(a * 255 / (factor * factor), 0, 255);
                    }
                }
            }

            return output;
        }
    }

    /// <summary>Flattens SVG path data into one polyline per subpath.</summary>
    private static class PathParser
    {
        public static List<List<PointF>> Parse(string data, float viewX, float viewY)
        {
            var tokens = Tokenize(data);
            var polygons = new List<List<PointF>>();
            List<PointF>? current = null;
            PointF point = default;
            PointF start = default;
            PointF lastControl = default;
            var command = '\0';
            var index = 0;

            while (index < tokens.Count)
            {
                if (tokens[index].IsCommand)
                {
                    command = tokens[index++].Command;
                }

                if (command == '\0')
                {
                    break;
                }

                var relative = char.IsLower(command);
                var upper = char.ToUpperInvariant(command);

                switch (upper)
                {
                    case 'M':
                    {
                        if (index + 1 >= tokens.Count)
                        {
                            return polygons;
                        }

                        var x = tokens[index++].Number;
                        var y = tokens[index++].Number;
                        point = relative
                            ? new PointF(point.X + x, point.Y + y)
                            : new PointF(x, y);
                        start = point;
                        current = [new PointF(point.X - viewX, point.Y - viewY)];
                        polygons.Add(current);
                        command = relative ? 'l' : 'L';
                        break;
                    }

                    case 'L':
                    case 'H':
                    case 'V':
                    {
                        if (index >= tokens.Count)
                        {
                            return polygons;
                        }

                        if (upper == 'L')
                        {
                            if (index + 1 >= tokens.Count)
                            {
                                return polygons;
                            }

                            var x = tokens[index++].Number;
                            var y = tokens[index++].Number;
                            point = relative
                                ? new PointF(point.X + x, point.Y + y)
                                : new PointF(x, y);
                        }
                        else if (upper == 'H')
                        {
                            var x = tokens[index++].Number;
                            point = new PointF(relative ? point.X + x : x, point.Y);
                        }
                        else
                        {
                            var y = tokens[index++].Number;
                            point = new PointF(point.X, relative ? point.Y + y : y);
                        }

                        current?.Add(new PointF(point.X - viewX, point.Y - viewY));
                        break;
                    }

                    case 'C':
                    case 'S':
                    {
                        if (index + (upper == 'C' ? 5 : 3) >= tokens.Count)
                        {
                            return polygons;
                        }

                        PointF c1;

                        if (upper == 'C')
                        {
                            var x1 = tokens[index++].Number;
                            var y1 = tokens[index++].Number;
                            c1 = relative ? new PointF(point.X + x1, point.Y + y1) : new PointF(x1, y1);
                        }
                        else
                        {
                            c1 = new PointF(2 * point.X - lastControl.X, 2 * point.Y - lastControl.Y);
                        }

                        var x2 = tokens[index++].Number;
                        var y2 = tokens[index++].Number;
                        var ex = tokens[index++].Number;
                        var ey = tokens[index++].Number;
                        var c2 = relative ? new PointF(point.X + x2, point.Y + y2) : new PointF(x2, y2);
                        var end = relative ? new PointF(point.X + ex, point.Y + ey) : new PointF(ex, ey);
                        FlattenCubic(current, point, c1, c2, end, viewX, viewY);
                        lastControl = c2;
                        point = end;
                        break;
                    }

                    case 'Q':
                    case 'T':
                    {
                        if (index + (upper == 'Q' ? 3 : 1) >= tokens.Count)
                        {
                            return polygons;
                        }

                        PointF control;

                        if (upper == 'Q')
                        {
                            var cx = tokens[index++].Number;
                            var cy = tokens[index++].Number;
                            control = relative ? new PointF(point.X + cx, point.Y + cy) : new PointF(cx, cy);
                        }
                        else
                        {
                            control = new PointF(2 * point.X - lastControl.X, 2 * point.Y - lastControl.Y);
                        }

                        var ex = tokens[index++].Number;
                        var ey = tokens[index++].Number;
                        var end = relative ? new PointF(point.X + ex, point.Y + ey) : new PointF(ex, ey);
                        FlattenQuadratic(current, point, control, end, viewX, viewY);
                        lastControl = control;
                        point = end;
                        break;
                    }

                    case 'A':
                    {
                        if (index + 6 >= tokens.Count)
                        {
                            return polygons;
                        }

                        var rx = tokens[index++].Number;
                        var ry = tokens[index++].Number;
                        var rotation = tokens[index++].Number;
                        var largeArc = tokens[index++].Number >= 0.5f;
                        var sweep = tokens[index++].Number >= 0.5f;
                        var ex = tokens[index++].Number;
                        var ey = tokens[index++].Number;
                        var end = relative ? new PointF(point.X + ex, point.Y + ey) : new PointF(ex, ey);
                        FlattenArc(current, point, rx, ry, rotation, largeArc, sweep, end, viewX, viewY);
                        point = end;
                        break;
                    }

                    case 'Z':
                        if (current is not null && current.Count > 0)
                        {
                            current.Add(current[0]);
                        }

                        point = start;
                        command = '\0';
                        break;

                    default:
                        return polygons;
                }
            }

            return polygons;
        }

        private static void FlattenCubic(
            List<PointF>? output,
            PointF p0,
            PointF p1,
            PointF p2,
            PointF p3,
            float viewX,
            float viewY)
        {
            if (output is null)
            {
                return;
            }

            const int steps = 16;

            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float) steps;
                var u = 1 - t;
                var x = u * u * u * p0.X + 3 * u * u * t * p1.X + 3 * u * t * t * p2.X + t * t * t * p3.X;
                var y = u * u * u * p0.Y + 3 * u * u * t * p1.Y + 3 * u * t * t * p2.Y + t * t * t * p3.Y;
                output.Add(new PointF(x - viewX, y - viewY));
            }
        }

        private static void FlattenQuadratic(
            List<PointF>? output,
            PointF p0,
            PointF p1,
            PointF p2,
            float viewX,
            float viewY)
        {
            if (output is null)
            {
                return;
            }

            const int steps = 12;

            for (var i = 1; i <= steps; i++)
            {
                var t = i / (float) steps;
                var u = 1 - t;
                var x = u * u * p0.X + 2 * u * t * p1.X + t * t * p2.X;
                var y = u * u * p0.Y + 2 * u * t * p1.Y + t * t * p2.Y;
                output.Add(new PointF(x - viewX, y - viewY));
            }
        }

        private static void FlattenArc(
            List<PointF>? output,
            PointF start,
            float rx,
            float ry,
            float rotation,
            bool largeArc,
            bool sweep,
            PointF end,
            float viewX,
            float viewY)
        {
            if (output is null)
            {
                return;
            }

            rx = MathF.Abs(rx);
            ry = MathF.Abs(ry);

            if (rx <= 0 || ry <= 0)
            {
                output.Add(new PointF(end.X - viewX, end.Y - viewY));
                return;
            }

            var phi = rotation * MathF.PI / 180f;
            var cosPhi = MathF.Cos(phi);
            var sinPhi = MathF.Sin(phi);
            var dx = (start.X - end.X) / 2;
            var dy = (start.Y - end.Y) / 2;
            var x1 = cosPhi * dx + sinPhi * dy;
            var y1 = -sinPhi * dx + cosPhi * dy;
            var lambda = (x1 * x1) / (rx * rx) + (y1 * y1) / (ry * ry);

            if (lambda > 1)
            {
                var factor = MathF.Sqrt(lambda);
                rx *= factor;
                ry *= factor;
            }

            var sign = largeArc == sweep ? -1 : 1;
            var numerator = rx * rx * ry * ry - rx * rx * y1 * y1 - ry * ry * x1 * x1;
            var denominator = rx * rx * y1 * y1 + ry * ry * x1 * x1;
            var coefficient = denominator <= 0 ? 0 : sign * MathF.Sqrt(MathF.Max(0, numerator / denominator));
            var cx1 = coefficient * rx * y1 / ry;
            var cy1 = -coefficient * ry * x1 / rx;
            var cx = cosPhi * cx1 - sinPhi * cy1 + (start.X + end.X) / 2;
            var cy = sinPhi * cx1 + cosPhi * cy1 + (start.Y + end.Y) / 2;

            var startAngle = MathF.Atan2((y1 - cy1) / ry, (x1 - cx1) / rx);
            var endAngle = MathF.Atan2((-y1 - cy1) / ry, (-x1 - cx1) / rx);
            var delta = endAngle - startAngle;

            if (!sweep && delta > 0)
            {
                delta -= MathF.PI * 2;
            }
            else if (sweep && delta < 0)
            {
                delta += MathF.PI * 2;
            }

            var steps = Math.Max(4, (int) (MathF.Abs(delta) * 12));

            for (var i = 1; i <= steps; i++)
            {
                var angle = startAngle + delta * i / steps;
                var x = cx + rx * MathF.Cos(angle) * cosPhi - ry * MathF.Sin(angle) * sinPhi;
                var y = cy + rx * MathF.Cos(angle) * sinPhi + ry * MathF.Sin(angle) * cosPhi;
                output.Add(new PointF(x - viewX, y - viewY));
            }
        }

        private static List<Token> Tokenize(string data)
        {
            var tokens = new List<Token>(data.Length / 2);
            var index = 0;

            while (index < data.Length)
            {
                var c = data[index];

                if (char.IsWhiteSpace(c) || c == ',')
                {
                    index++;
                    continue;
                }

                if (char.IsLetter(c))
                {
                    tokens.Add(new Token(c, 0, true));
                    index++;
                    continue;
                }

                var start = index;
                index = ReadNumber(data, index, out var number);

                if (index > start)
                {
                    tokens.Add(new Token('\0', number, false));
                }
                else
                {
                    index = start + 1;
                }
            }

            return tokens;
        }

        private readonly record struct Token(char Command, float Number, bool IsCommand);
    }
}
