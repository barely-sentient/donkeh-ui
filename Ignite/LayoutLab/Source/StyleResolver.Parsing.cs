using System.Globalization;
using System.Numerics;
using System.Text;
using Ignite.Parser;

namespace Ignite.LayoutLab;

public sealed partial class StyleResolver
{
    internal readonly record struct ParsedBorder(CssLength Width, BorderStyle Style, uint Color);

    internal readonly record struct BorderStyle4(
        BorderStyle Top,
        BorderStyle Right,
        BorderStyle Bottom,
        BorderStyle Left);

    internal readonly record struct Color4(uint Top, uint Right, uint Bottom, uint Left);

    private string CleanValue(int valueId, string rawValue)
    {
        if (_cache.CleanValues.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var value = rawValue.Trim();
        var bang = value.LastIndexOf('!');

        if (bang >= 0
            && value[(bang + 1)..].Trim().Equals("important", StringComparison.OrdinalIgnoreCase))
        {
            value = value[..bang].TrimEnd();
        }

        _cache.CleanValues[valueId] = value;
        return value;
    }

    private bool IsImportant(int valueId, string rawValue)
    {
        if (_cache.Important.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var value = rawValue.TrimEnd();
        var important = value.EndsWith("!important", StringComparison.OrdinalIgnoreCase);
        _cache.Important[valueId] = important;
        return important;
    }

    private string ResolveVariables(string value, ComputedStyle style, int valueId)
    {
        var hasVariables = _cache.HasVariables.TryGetValue(valueId, out var known)
            ? known
            : value.Contains("var(", StringComparison.OrdinalIgnoreCase);

        _cache.HasVariables[valueId] = hasVariables;

        if (!hasVariables)
        {
            return value;
        }

        return ResolveVariablesCore(value, style.CustomProperties, 0);
    }

    private static string ResolveVariablesCore(
        string value,
        Dictionary<string, string>? customProperties,
        int depth)
    {
        if (depth > 16)
        {
            return value;
        }

        var start = value.IndexOf("var(", StringComparison.OrdinalIgnoreCase);

        if (start < 0)
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 16);
        var cursor = 0;

        while (start >= 0)
        {
            builder.Append(value, cursor, start - cursor);

            var open = start + 4;
            var close = FindMatchingParenthesis(value, open - 1);

            if (close < 0)
            {
                builder.Append(value, start, value.Length - start);
                return builder.ToString();
            }

            var content = value[open..close];
            var comma = FindTopLevelComma(content);
            var name = (comma < 0 ? content : content[..comma]).Trim();
            var fallback = comma < 0 ? null : content[(comma + 1)..].Trim();

            string? replacement = null;

            if (name.StartsWith("--", StringComparison.Ordinal)
                && customProperties is not null
                && customProperties.TryGetValue(name, out var custom))
            {
                replacement = custom;
            }

            replacement ??= fallback ?? string.Empty;

            if (replacement.Contains("var(", StringComparison.OrdinalIgnoreCase))
            {
                replacement = ResolveVariablesCore(replacement, customProperties, depth + 1);
            }

            builder.Append(replacement);
            cursor = close + 1;
            start = value.IndexOf("var(", cursor, StringComparison.OrdinalIgnoreCase);
        }

        builder.Append(value, cursor, value.Length - cursor);
        return builder.ToString();
    }

    private static int FindMatchingParenthesis(string value, int openIndex)
    {
        var depth = 0;

        for (var i = openIndex; i < value.Length; i++)
        {
            if (value[i] == '(')
            {
                depth++;
            }
            else if (value[i] == ')')
            {
                depth--;

                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static int FindTopLevelComma(string value)
    {
        var depth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '(') depth++;
            else if (value[i] == ')') depth--;
            else if (value[i] == ',' && depth == 0) return i;
        }

        return -1;
    }

    private bool HasVariables(int valueId)
    {
        return _cache.HasVariables.TryGetValue(valueId, out var has) && has;
    }

    private bool HasCurrentColor(string value)
    {
        return value.Contains("currentcolor", StringComparison.OrdinalIgnoreCase)
            || value.Contains("current-color", StringComparison.OrdinalIgnoreCase);
    }

    private CssLength ParseLengthCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Lengths.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssValueParser.ParseLength(value);

        if (!HasVariables(valueId))
        {
            _cache.Lengths[valueId] = parsed;
        }

        return parsed;
    }

    private CssLength4 ParseLength4Cached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Length4.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssLengths.ParseShorthand(value);

        if (!HasVariables(valueId))
        {
            _cache.Length4[valueId] = parsed;
        }

        return parsed;
    }

    private float ParseNumberCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Numbers.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssValueParser.ParseNumber(value);

        if (!HasVariables(valueId))
        {
            _cache.Numbers[valueId] = parsed;
        }

        return parsed;
    }

    private uint ParseColorCached(int valueId, string value, uint currentColor)
    {
        var cacheable = !HasVariables(valueId) && !HasCurrentColor(value);

        if (cacheable && _cache.Colors.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssColor.ParseOr(value, currentColor, currentColor);

        if (cacheable)
        {
            _cache.Colors[valueId] = parsed;
        }

        return parsed;
    }

    private uint ParseColorSentinel(int valueId, string value)
    {
        var cacheable = !HasVariables(valueId) && !HasCurrentColor(value);

        if (cacheable && _cache.Colors.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = CssColor.ParseOr(value, ComputedStyle.CurrentColor, ComputedStyle.CurrentColor);

        if (cacheable)
        {
            _cache.Colors[valueId] = parsed;
        }

        return parsed;
    }

    private Color4 ParseColor4Cached(int valueId, string value, uint currentColor)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        if (parts.Count == 0)
        {
            return new Color4(currentColor, currentColor, currentColor, currentColor);
        }

        var first = ParseColorSentinel(valueId, parts[0]);

        static uint ParsePart(string part)
        {
            return CssColor.ParseOr(part, ComputedStyle.CurrentColor, ComputedStyle.CurrentColor);
        }

        if (parts.Count == 1)
        {
            return new Color4(first, first, first, first);
        }

        if (parts.Count == 2)
        {
            var second = ParsePart(parts[1]);
            return new Color4(first, second, first, second);
        }

        if (parts.Count == 3)
        {
            var second = ParsePart(parts[1]);
            return new Color4(first, second, ParsePart(parts[2]), second);
        }

        return new Color4(first, ParsePart(parts[1]), ParsePart(parts[2]), ParsePart(parts[3]));
    }

    private ParsedBorder ParseBorderCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Borders.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var width = CssLength.Px(3);
        var style = BorderStyle.None;
        var color = ComputedStyle.CurrentColor;
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        foreach (var part in parts)
        {
            if (TryParseBorderStyle(part, out var parsedStyle))
            {
                style = parsedStyle;
                continue;
            }

            if (part.Equals("thin", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(1);
                continue;
            }

            if (part.Equals("medium", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(3);
                continue;
            }

            if (part.Equals("thick", StringComparison.OrdinalIgnoreCase))
            {
                width = CssLength.Px(5);
                continue;
            }

            if (CssColor.TryParse(part, ComputedStyle.CurrentColor, out var parsedColor))
            {
                color = parsedColor;
                continue;
            }

            var length = CssValueParser.ParseLength(part);

            if (!length.IsAuto)
            {
                width = length;
            }
        }

        var border = new ParsedBorder(width, style, color);

        if (!HasVariables(valueId))
        {
            _cache.Borders[valueId] = border;
        }

        return border;
    }

    private CssCornerRadius4 ParseRadiusCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Radii.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        // The slash form separates horizontal and vertical radii. This cut keeps
        // the horizontal radii, which is exact for the common circular case.
        var slash = value.IndexOf('/');

        if (slash >= 0)
        {
            value = value[..slash].Trim();
        }

        var lengths = CssLengths.ParseShorthand(value);
        var radius = new CssCornerRadius4(lengths.Top, lengths.Right, lengths.Bottom, lengths.Left);

        if (!HasVariables(valueId))
        {
            _cache.Radii[valueId] = radius;
        }

        return radius;
    }

    private GridTrackList ParseTracksCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Tracks.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridTrackList.Parse(value);

        if (!HasVariables(valueId))
        {
            _cache.Tracks[valueId] = parsed;
        }

        return parsed;
    }

    private GridPlacement ParsePlacementCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Placements.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridPlacementParser.Parse(value);

        if (!HasVariables(valueId))
        {
            _cache.Placements[valueId] = parsed;
        }

        return parsed;
    }

    private GridTemplateAreas? ParseGridAreasCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.GridAreas.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = GridTemplateAreas.Parse(value);

        if (parsed is not null && !HasVariables(valueId))
        {
            _cache.GridAreas[valueId] = parsed;
        }

        return parsed;
    }

    private FilterSpec ParseFilterCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Filters.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseFilter(value, currentColor);

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Filters[valueId] = parsed;
        }

        return parsed;
    }

    private ClipPathSpec ParseClipPathCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.ClipPaths.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseClipPath(value);

        if (!HasVariables(valueId))
        {
            _cache.ClipPaths[valueId] = parsed;
        }

        return parsed;
    }

    private MaskSpec ParseMaskCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Masks.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var parsed = ParseMask(value);

        if (!HasVariables(valueId))
        {
            _cache.Masks[valueId] = parsed;
        }

        return parsed;
    }

    private static FilterSpec ParseFilter(string value, uint currentColor)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return FilterSpec.None;
        }

        var parts = CssValueParser.SplitTopLevel(text, ' ');
        var functions = new List<FilterFunction>(parts.Count);

        foreach (var raw in parts)
        {
            var part = raw.Trim();

            if (part.Length == 0)
            {
                continue;
            }

            var open = part.IndexOf('(');

            if (open <= 0 || !part.EndsWith(')'))
            {
                continue;
            }

            var name = part[..open].Trim().ToLowerInvariant();
            var args = part[(open + 1)..^1].Trim();
            var kind = name switch
            {
                "blur" => FilterFunctionKind.Blur,
                "brightness" => FilterFunctionKind.Brightness,
                "contrast" => FilterFunctionKind.Contrast,
                "grayscale" => FilterFunctionKind.Grayscale,
                "invert" => FilterFunctionKind.Invert,
                "opacity" => FilterFunctionKind.Opacity,
                "saturate" => FilterFunctionKind.Saturate,
                "sepia" => FilterFunctionKind.Sepia,
                "hue-rotate" => FilterFunctionKind.HueRotate,
                "drop-shadow" => FilterFunctionKind.DropShadow,
                "url" => FilterFunctionKind.Url,
                _ => (FilterFunctionKind)255
            };

            switch (kind)
            {
                case FilterFunctionKind.Blur:
                {
                    var length = CssValueParser.ParseLength(args);
                    functions.Add(new FilterFunction(kind, MathF.Max(0, length.Resolve(new LengthContext()))));
                    break;
                }

                case FilterFunctionKind.HueRotate:
                    functions.Add(new FilterFunction(kind, ParseAngle(args)));
                    break;

                case FilterFunctionKind.Brightness:
                case FilterFunctionKind.Contrast:
                case FilterFunctionKind.Grayscale:
                case FilterFunctionKind.Invert:
                case FilterFunctionKind.Opacity:
                case FilterFunctionKind.Saturate:
                case FilterFunctionKind.Sepia:
                    functions.Add(new FilterFunction(kind, ParseFilterAmount(args, 1)));
                    break;

                case FilterFunctionKind.DropShadow:
                    functions.Add(new FilterFunction(kind, 0, ParseShadow(args, currentColor)));
                    break;

                case FilterFunctionKind.Url:
                    functions.Add(new FilterFunction(kind, 0, default, args));
                    break;
            }
        }

        return functions.Count == 0 ? FilterSpec.None : new FilterSpec(functions.ToArray());
    }

    private static float ParseFilterAmount(string text, float fallback)
    {
        text = text.Trim();

        if (text.Length == 0)
        {
            return fallback;
        }

        if (text.EndsWith('%'))
        {
            return CssValueParser.ParseNumber(text[..^1], fallback * 100) * 0.01f;
        }

        return CssValueParser.ParseNumber(text, fallback);
    }

    private static ClipPathSpec ParseClipPath(string value)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return ClipPathSpec.None;
        }

        var open = text.IndexOf('(');

        if (open <= 0 || !text.EndsWith(')'))
        {
            // margin-box / border-box / padding-box / content-box all clip to
            // the box itself, which is the identity for our border-box scope.
            return new ClipPathSpec { Kind = ClipPathKind.Inset };
        }

        var name = text[..open].Trim().ToLowerInvariant();
        var args = text[(open + 1)..^1].Trim();

        switch (name)
        {
            case "inset":
            {
                var lengths = new List<CssLength>(4);
                var tokens = CssValueParser.SplitTopLevel(args, ' ');

                foreach (var token in tokens)
                {
                    if (token.Equals("round", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    lengths.Add(CssValueParser.ParseLength(token));
                }

                var top = lengths.Count > 0 ? lengths[0] : CssLength.Zero;
                var right = lengths.Count > 1 ? lengths[1] : top;
                var bottom = lengths.Count > 2 ? lengths[2] : top;
                var left = lengths.Count > 3 ? lengths[3] : right;

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Inset,
                    InsetTop = top,
                    InsetRight = right,
                    InsetBottom = bottom,
                    InsetLeft = left
                };
            }

            case "circle":
            {
                var (radius, center) = SplitAt(args);
                var radiusX = radius.Length > 0 && !radius.Equals("closest-side", StringComparison.OrdinalIgnoreCase)
                    ? CssValueParser.ParseLength(radius)
                    : CssLength.Percent(50);
                var centerX = CssLength.Percent(50);
                var centerY = CssLength.Percent(50);

                if (center.Length > 0)
                {
                    var centerParts = CssValueParser.SplitTopLevel(center, ' ');
                    centerX = centerParts.Count > 0 ? CssValueParser.ParseLength(centerParts[0]) : centerX;
                    centerY = centerParts.Count > 1 ? CssValueParser.ParseLength(centerParts[1]) : centerY;
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Circle,
                    RadiusX = radiusX,
                    RadiusY = radiusX,
                    CenterX = centerX,
                    CenterY = centerY
                };
            }

            case "ellipse":
            {
                var (radius, center) = SplitAt(args);
                var radiusParts = CssValueParser.SplitTopLevel(radius, ' ');
                var radiusX = radiusParts.Count > 0 ? CssValueParser.ParseLength(radiusParts[0]) : CssLength.Percent(50);
                var radiusY = radiusParts.Count > 1 ? CssValueParser.ParseLength(radiusParts[1]) : radiusX;
                var centerX = CssLength.Percent(50);
                var centerY = CssLength.Percent(50);

                if (center.Length > 0)
                {
                    var centerParts = CssValueParser.SplitTopLevel(center, ' ');
                    centerX = centerParts.Count > 0 ? CssValueParser.ParseLength(centerParts[0]) : centerX;
                    centerY = centerParts.Count > 1 ? CssValueParser.ParseLength(centerParts[1]) : centerY;
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Ellipse,
                    RadiusX = radiusX,
                    RadiusY = radiusY,
                    CenterX = centerX,
                    CenterY = centerY
                };
            }

            case "polygon":
            {
                var parts = CssValueParser.SplitTopLevel(args, ',');
                var points = new List<ClipPathPoint>(parts.Count);

                foreach (var raw in parts)
                {
                    var point = raw.Trim();

                    if (point.Length == 0)
                    {
                        continue;
                    }

                    if (point.Equals("nonzero", StringComparison.OrdinalIgnoreCase)
                        || point.Equals("evenodd", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var coords = CssValueParser.SplitTopLevel(point, ' ');

                    if (coords.Count >= 2)
                    {
                        points.Add(new ClipPathPoint(
                            CssValueParser.ParseLength(coords[0]),
                            CssValueParser.ParseLength(coords[1])));
                    }
                }

                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Polygon,
                    Points = points.ToArray()
                };
            }

            default:
                return new ClipPathSpec
                {
                    Kind = ClipPathKind.Url,
                    Url = args
                };
        }
    }

    private static (string Radius, string Center) SplitAt(string text)
    {
        var index = text.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);

        return index < 0
            ? (text.Trim(), string.Empty)
            : (text[..index].Trim(), text[(index + 4)..].Trim());
    }

    private static MaskSpec ParseMask(string value)
    {
        var text = value.Trim();

        if (text.Length == 0 || text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return MaskSpec.None;
        }

        var source = text;
        var kind = ImageKind.None;
        var mode = MaskMode.MatchSource;
        var repeat = BackgroundRepeat.Repeat;

        var tokens = CssValueParser.SplitTopLevel(text, ' ');

        foreach (var raw in tokens)
        {
            var token = raw.Trim();
            var lower = token.ToLowerInvariant();

            if (token.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
            {
                source = token.Length > 5 && token.EndsWith(')') ? token[4..^1] : token;
                kind = ImageKind.Url;
            }
            else if (lower.Contains("gradient(", StringComparison.Ordinal))
            {
                source = token;
                kind = lower.Contains("radial", StringComparison.Ordinal)
                    ? ImageKind.RadialGradient
                    : lower.Contains("conic", StringComparison.Ordinal)
                        ? ImageKind.ConicGradient
                        : ImageKind.LinearGradient;
            }
            else if (lower is "alpha" or "luminance" or "match-source")
            {
                mode = lower switch
                {
                    "alpha" => MaskMode.Alpha,
                    "luminance" => MaskMode.Luminance,
                    _ => MaskMode.MatchSource
                };
            }
            else if (lower is "repeat-x")
            {
                repeat = BackgroundRepeat.RepeatX;
            }
            else if (lower is "repeat-y")
            {
                repeat = BackgroundRepeat.RepeatY;
            }
            else if (lower is "no-repeat")
            {
                repeat = BackgroundRepeat.NoRepeat;
            }
        }

        return kind == ImageKind.None
            ? MaskSpec.None
            : new MaskSpec
            {
                Kind = kind,
                Source = source,
                Mode = mode,
                Repeat = repeat
            };
    }

    private ShadowSpec[] ParseShadowsCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Shadows.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var layers = CssValueParser.SplitTopLevel(value, ',');
        var shadows = new ShadowSpec[layers.Count];

        for (var i = 0; i < layers.Count; i++)
        {
            shadows[i] = ParseShadow(layers[i], currentColor);
        }

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Shadows[valueId] = shadows;
        }

        return shadows;
    }

    private static ShadowSpec ParseShadow(string value, uint currentColor)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');
        var inset = false;
        var color = currentColor;
        var lengths = new List<CssLength>(4);

        foreach (var part in parts)
        {
            if (part.Equals("inset", StringComparison.OrdinalIgnoreCase))
            {
                inset = true;
                continue;
            }

            if (CssColor.TryParse(part, currentColor, out var parsedColor))
            {
                color = parsedColor;
                continue;
            }

            lengths.Add(CssValueParser.ParseLength(part));
        }

        var context = new LengthContext();

        return new ShadowSpec(
            lengths.Count > 0 ? lengths[0].Resolve(in context) : 0,
            lengths.Count > 1 ? lengths[1].Resolve(in context) : 0,
            lengths.Count > 2 ? lengths[2].Resolve(in context) : 0,
            lengths.Count > 3 ? lengths[3].Resolve(in context) : 0,
            color,
            inset);
    }

    private BackgroundImageSpec[] ParseBackgroundsCached(int valueId, string value, uint currentColor)
    {
        if (!HasVariables(valueId) && !HasCurrentColor(value)
            && _cache.Backgrounds.TryGetValue(valueId, out var cached))
        {
            return cached;
        }

        var result = ParseBackgroundLayers(value, currentColor);

        if (!HasVariables(valueId) && !HasCurrentColor(value))
        {
            _cache.Backgrounds[valueId] = result;
        }

        return result;
    }

    private static BackgroundImageSpec[] ParseBackgroundLayers(string value, uint currentColor)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var layers = CssValueParser.SplitTopLevel(value, ',');
        var backgrounds = new List<BackgroundImageSpec>(layers.Count);

        foreach (var layer in layers)
        {
            var parsed = ParseBackgroundLayer(layer.Trim(), currentColor);

            if (parsed is not null)
            {
                backgrounds.Add(parsed);
            }
        }

        return backgrounds.Count == 0 ? [] : backgrounds.ToArray();
    }

    private static BackgroundImageSpec? ParseBackgroundLayer(string value, uint currentColor)
    {
        if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
        {
            var close = FindMatchingParenthesis(value, 3);

            if (close < 0)
            {
                return null;
            }

            var source = CssValueParser.Unquote(value[4..close].Trim());
            return new BackgroundImageSpec { Kind = ImageKind.Url, Url = source };
        }

        if (value.Contains("linear-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Linear, currentColor);
        }

        if (value.Contains("radial-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Radial, currentColor);
        }

        if (value.Contains("conic-gradient(", StringComparison.OrdinalIgnoreCase))
        {
            return ParseGradient(value, GradientKind.Conic, currentColor);
        }

        return null;
    }

    private static BackgroundImageSpec? ParseGradient(string value, GradientKind kind, uint currentColor)
    {
        var open = value.IndexOf('(');
        var close = value.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return null;
        }

        var inner = value[(open + 1)..close];
        var parts = CssValueParser.SplitTopLevel(inner, ',');
        var stops = new List<GradientStop>(parts.Count);
        var angle = kind == GradientKind.Linear ? 180f : 0f;
        var center = new Point(0.5f, 0.5f);
        var radius = new Size(0.5f, 0.5f);
        var startIndex = 0;

        if (parts.Count > 0)
        {
            var first = parts[0].Trim();

            if (kind == GradientKind.Linear && !LooksLikeColorStop(first))
            {
                angle = ParseGradientAngle(first, angle);
                startIndex = 1;
            }
            else if (kind is GradientKind.Radial or GradientKind.Conic && !LooksLikeColorStop(first))
            {
                ParseGradientPosition(first, kind, ref center, ref radius);
                startIndex = 1;
            }
        }

        for (var i = startIndex; i < parts.Count; i++)
        {
            if (TryParseGradientStop(parts[i], currentColor, out var stop))
            {
                stops.Add(stop);
            }
        }

        AssignStopOffsets(stops);

        return new BackgroundImageSpec
        {
            Kind = kind switch
            {
                GradientKind.Linear => ImageKind.LinearGradient,
                GradientKind.Radial => ImageKind.RadialGradient,
                _ => ImageKind.ConicGradient
            },
            GradientKind = kind,
            Angle = angle,
            Center = center,
            Radius = radius,
            Stops = stops.ToArray()
        };
    }

    private static bool LooksLikeColorStop(string value)
    {
        if (CssColor.TryParse(value, ComputedStyle.CurrentColor, out _))
        {
            return true;
        }

        if (value.StartsWith('#')
            || value.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("rgba(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsl(", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A stop may carry a position: "red 10%". Check just the first token.
        var tokens = CssValueTokenizer.Tokenize(value);

        if (tokens.Length == 0)
        {
            return false;
        }

        var first = tokens[0];

        if (first.Kind == CssValueTokenKind.Function)
        {
            var name = CssValueTokenizer.GetSpan(value, in first);
            return name.Equals("rgb", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rgba", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsl", StringComparison.OrdinalIgnoreCase)
                || name.Equals("hsla", StringComparison.OrdinalIgnoreCase);
        }

        return first.Kind == CssValueTokenKind.Word
            && CssColor.TryParse(CssValueTokenizer.GetText(value, first), ComputedStyle.CurrentColor, out _);
    }

    private static bool TryParseGradientStop(string value, uint currentColor, out GradientStop stop)
    {
        stop = default;
        var text = value.Trim();

        if (CssColor.TryParse(text, currentColor, out var color))
        {
            stop = new GradientStop(float.NaN, color);
            return true;
        }

        // Split the position from the colour. A colour may itself contain spaces
        // (hsl(0 100% 50%)), so try progressively shorter prefixes.
        var space = text.LastIndexOf(' ');

        if (space > 0)
        {
            var colorPart = text[..space].Trim();
            var positionPart = text[(space + 1)..].Trim();

            if (CssColor.TryParse(colorPart, currentColor, out color))
            {
                var position = ParseStopOffset(positionPart);
                stop = new GradientStop(position, color);
                return true;
            }
        }

        return false;
    }

    private static float ParseStopOffset(string value)
    {
        var text = value.Trim();

        if (text.EndsWith('%'))
        {
            return Math.Clamp(CssValueParser.ParseNumber(text[..^1]) / 100f, 0, 1);
        }

        if (text.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Clamp(CssValueParser.ParseNumber(text[..^3]) / 360f, 0, 1);
        }

        // Absolute stop positions need the gradient box; keep them unresolved
        // as a NaN so the painter can distribute them proportionally.
        return float.NaN;
    }

    private static void AssignStopOffsets(List<GradientStop> stops)
    {
        if (stops.Count == 0)
        {
            return;
        }

        if (float.IsNaN(stops[0].Offset))
        {
            stops[0] = stops[0] with { Offset = 0 };
        }

        if (float.IsNaN(stops[^1].Offset))
        {
            stops[^1] = stops[^1] with { Offset = 1 };
        }

        var lastKnown = 0;

        for (var i = 1; i < stops.Count; i++)
        {
            if (!float.IsNaN(stops[i].Offset))
            {
                if (i - lastKnown > 1)
                {
                    var start = stops[lastKnown].Offset;
                    var end = stops[i].Offset;
                    var step = (end - start) / (i - lastKnown);

                    for (var j = lastKnown + 1; j < i; j++)
                    {
                        stops[j] = stops[j] with { Offset = start + step * (j - lastKnown) };
                    }
                }

                lastKnown = i;
            }
        }
    }

    private static float ParseGradientAngle(string value, float fallback)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text.EndsWith("deg", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]);
        }

        if (text.EndsWith("turn", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^4]) * 360f;
        }

        if (text.EndsWith("rad", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]) * (180f / MathF.PI);
        }

        if (text.StartsWith("to ", StringComparison.Ordinal))
        {
            return text switch
            {
                "to top" => 0,
                "to top right" or "to right top" => 45,
                "to right" => 90,
                "to bottom right" or "to right bottom" => 135,
                "to bottom" => 180,
                "to bottom left" or "to left bottom" => 225,
                "to left" => 270,
                "to top left" or "to left top" => 315,
                _ => fallback
            };
        }

        return CssValueParser.ParseNumber(text, fallback);
    }

    private static void ParseGradientPosition(
        string value,
        GradientKind kind,
        ref Point center,
        ref Size radius)
    {
        var text = value.Trim().ToLowerInvariant();
        var at = text.IndexOf(" at ", StringComparison.Ordinal);

        if (at >= 0)
        {
            var centerText = text[(at + 4)..].Trim();
            center = ParsePositionPair(centerText, center);
            text = text[..at].Trim();
        }

        if (kind == GradientKind.Radial && text.StartsWith("circle", StringComparison.Ordinal))
        {
            var parts = CssValueParser.SplitTopLevel(text, ' ');
            var diameter = parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Percent(50);
            var amount = diameter.IsPercent ? diameter.Value / 100f : 0.5f;
            radius = new Size(amount, amount);
        }
    }

    private static Point ParsePositionPair(string value, Point fallback)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        if (parts.Count == 0)
        {
            return fallback;
        }

        var x = ParsePositionComponent(parts[0], horizontal: true, fallback.X);
        var y = parts.Count > 1 ? ParsePositionComponent(parts[1], horizontal: false, fallback.Y) : fallback.Y;
        return new Point(x, y);
    }

    private static float ParsePositionComponent(string value, bool horizontal, float fallback)
    {
        var text = value.Trim().ToLowerInvariant();

        return text switch
        {
            "left" or "top" => 0,
            "center" => 0.5f,
            "right" or "bottom" => 1,
            _ => text.EndsWith('%')
                ? Math.Clamp(CssValueParser.ParseNumber(text[..^1]) / 100f, 0, 1)
                : fallback
        };
    }

    private TransformSpec? ParseTransformCached(int valueId, string value)
    {
        if (!HasVariables(valueId) && _cache.Transforms.TryGetValue(valueId, out var cached))
        {
            return cached.Transform;
        }

        var parsed = ParseTransform(value);

        if (!HasVariables(valueId))
        {
            _cache.Transforms[valueId] = (parsed is not null, parsed);
        }

        return parsed;
    }

    internal static TransformSpec? ParseTransform(string value)
    {
        if (value.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var source = value;
        var tokens = CssValueTokenizer.Tokenize(source);

        if (tokens.Length == 0)
        {
            return null;
        }

        var operations = new List<TransformOperation>(tokens.Length);
        var index = 0;

        while (index < tokens.Length)
        {
            var token = tokens[index];

            if (token.Kind != CssValueTokenKind.Function)
            {
                index++;
                continue;
            }

            var name = CssValueTokenizer.GetText(source, token);
            var open = token.Start + token.Length;
            var close = FindMatchingParenthesis(source, open);
            var arguments = close < 0 ? string.Empty : source[(open + 1)..close];
            index++;

            while (index < tokens.Length && tokens[index].Start <= close)
            {
                index++;
            }

            var parts = CssValueParser.SplitTopLevel(arguments, ',');

            if (parts.Count == 1)
            {
                parts = CssValueParser.SplitTopLevel(arguments, ' ');
            }

            switch (name.ToLowerInvariant())
            {
                case "translate":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssValueParser.ParseLength(parts[0]),
                        parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Zero,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "translatex":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssValueParser.ParseLength(parts[0]),
                        CssLength.Zero,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "translatey":
                    operations.Add(new TransformOperation(
                        TransformKind.Translate,
                        CssLength.Zero,
                        CssValueParser.ParseLength(parts[0]),
                        0,
                        Matrix3x2.Identity));
                    break;

                case "scale":
                {
                    var x = CssValueParser.ParseLength(parts[0]);

                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        x,
                        parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Auto,
                        0,
                        Matrix3x2.Identity));
                    break;
                }

                case "scalex":
                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        CssValueParser.ParseLength(parts[0]),
                        CssLength.Auto,
                        0,
                        Matrix3x2.Identity));
                    break;

                case "scaley":
                    operations.Add(new TransformOperation(
                        TransformKind.Scale,
                        CssLength.Number(1),
                        CssValueParser.ParseLength(parts[0]),
                        0,
                        Matrix3x2.Identity));
                    break;

                case "rotate":
                case "rotatez":
                    operations.Add(new TransformOperation(
                        TransformKind.Rotate,
                        CssLength.Zero,
                        CssLength.Zero,
                        ParseAngle(parts[0]),
                        Matrix3x2.Identity));
                    break;

                case "skew":
                    operations.Add(new TransformOperation(
                        TransformKind.Skew,
                        CssLength.Zero,
                        parts.Count > 1 ? CssLength.Number(ParseAngle(parts[1])) : CssLength.Zero,
                        ParseAngle(parts[0]),
                        Matrix3x2.Identity));
                    break;

                case "matrix":
                    if (parts.Count >= 6)
                    {
                        operations.Add(new TransformOperation(
                            TransformKind.Matrix,
                            CssLength.Zero,
                            CssLength.Zero,
                            0,
                            new Matrix3x2(
                                CssValueParser.ParseNumber(parts[0]),
                                CssValueParser.ParseNumber(parts[1]),
                                CssValueParser.ParseNumber(parts[2]),
                                CssValueParser.ParseNumber(parts[3]),
                                CssValueParser.ParseNumber(parts[4]),
                                CssValueParser.ParseNumber(parts[5]))));
                    }

                    break;
            }
        }

        return operations.Count == 0 ? null : new TransformSpec(operations.ToArray());
    }

    private static float ParseAngle(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text.EndsWith("deg", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]);
        }

        if (text.EndsWith("rad", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^3]) * (180f / MathF.PI);
        }

        if (text.EndsWith("turn", StringComparison.Ordinal))
        {
            return CssValueParser.ParseNumber(text[..^4]) * 360f;
        }

        return CssValueParser.ParseNumber(text);
    }

    private static float? ParseAspectRatio(string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, '/');

        if (parts.Count >= 2)
        {
            var width = CssValueParser.ParseNumber(parts[0]);
            var height = CssValueParser.ParseNumber(parts[1]);
            return height == 0 ? null : width / height;
        }

        var ratio = CssValueParser.ParseNumber(value, float.NaN);
        return float.IsNaN(ratio) || ratio <= 0 ? null : ratio;
    }
}
