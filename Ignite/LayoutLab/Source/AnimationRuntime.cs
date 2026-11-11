using System.Globalization;
using System.Numerics;
using System.Text;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.Parser;

namespace Ignite.LayoutLab;

/// <summary>
/// Evaluates CSS keyframe animations during layout. The engine keeps one
/// runtime per <see cref="LayoutEngine"/> so animation start times survive
/// frames. Only properties that the engine can resolve are animated; other
/// properties step between keyframes.
/// </summary>
internal sealed class AnimationRuntime
{
    private readonly DataTable _table;
    private readonly Dictionary<int, KeyframeTrack?> _tracks = [];
    private readonly Dictionary<IgniteSupportedDomNode, NodeAnimationState> _states = [];
    private readonly List<IgniteSupportedDomNode> _staleStates = [];
    private int _frame;

    public AnimationRuntime(DataTable table)
    {
        _table = table;
    }

    /// <summary>True when at least one animation was active in the current frame.</summary>
    public bool HasRunningAnimations { get; private set; }

    /// <summary>
    /// True when an active animation changes geometry. Transform and opacity
    /// animations are paint-only and can refresh the display list without a
    /// full layout pass.
    /// </summary>
    public bool HasLayoutAnimations { get; private set; }

    /// <summary>Number of nodes with retained animation state.</summary>
    public int StateCount => _states.Count;

    public void BeginFrame()
    {
        HasRunningAnimations = false;
        HasLayoutAnimations = false;
        _frame++;
    }

    /// <summary>
    /// Drops animation state for nodes that were not part of this layout pass.
    /// Without this, an animated node that unmounts (a faded panel, a closed
    /// modal, a removed carousel slide) stays reachable forever through the
    /// state dictionary and its cached animated style.
    /// </summary>
    public void Prune()
    {
        if (_states.Count == 0)
        {
            return;
        }

        _staleStates.Clear();

        foreach (var pair in _states)
        {
            if (pair.Value.Frame != _frame)
            {
                _staleStates.Add(pair.Key);
            }
        }

        for (var i = 0; i < _staleStates.Count; i++)
        {
            _states.Remove(_staleStates[i]);
        }
    }

    public ComputedStyle Apply(
        IgniteSupportedDomNode node,
        ComputedStyle baseStyle,
        ComputedStyle? parent,
        double now)
    {
        var animations = baseStyle.Animations;

        if (animations is null || animations.Count == 0)
        {
            _states.Remove(node);
            return baseStyle;
        }

        var signature = Signature(animations);

        if (!_states.TryGetValue(node, out var state) || state.Signature != signature)
        {
            state = new NodeAnimationState
            {
                Signature = signature,
                StartTime = now
            };

            _states[node] = state;
        }

        state.Frame = _frame;

        ComputedStyle? animated = null;

        for (var i = 0; i < animations.Count; i++)
        {
            var spec = animations[i];

            if (string.IsNullOrWhiteSpace(spec.Name)
                || spec.Name.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var track = GetTrack(spec.Name);

            if (track is null)
            {
                continue;
            }

            var active = TryEvaluate(spec, state.StartTime, now, out var progress, out var running);

            if (running)
            {
                HasRunningAnimations = true;
            }

            if (!active)
            {
                continue;
            }

            if (animated is null)
            {
                animated = state.Animated ??= baseStyle.CloneForAnimation();
                animated.CopyAllFrom(baseStyle);
            }

            ApplyTrack(animated, baseStyle, parent, track, progress);
        }

        if (animated is null)
        {
            return baseStyle;
        }

        animated.IsAnimated = true;
        return animated;
    }

    // ------------------------------------------------------------- keyframes

    private KeyframeTrack? GetTrack(string name)
    {
        if (!InternTable.TryGetId(name, out var nameId))
        {
            return null;
        }

        if (_tracks.TryGetValue(nameId, out var cached))
        {
            return cached;
        }

        var track = BuildTrack(nameId);
        _tracks[nameId] = track;
        return track;
    }

    private KeyframeTrack? BuildTrack(int nameId)
    {
        var stops = new Dictionary<float, List<KeyframeDeclaration>>();
        var frames = _table.Keyframes;

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];

            if (frame.NameId != nameId)
            {
                continue;
            }

            var property = CssPropertyIds.Get(frame.PropertyId);
            var value = InternTable.GetString(frame.ValueId);

            if (property == CssProperty.Unknown || value is null)
            {
                continue;
            }

            var offsets = new List<float>(2);
            CollectOffsets(frame.KeyId, offsets);

            for (var o = 0; o < offsets.Count; o++)
            {
                var offset = offsets[o];

                if (!stops.TryGetValue(offset, out var declarations))
                {
                    declarations = [];
                    stops[offset] = declarations;
                }

                declarations.Add(new KeyframeDeclaration(property, value));
            }
        }

        if (stops.Count == 0)
        {
            return null;
        }

        if (!stops.ContainsKey(0))
        {
            stops[0] = [];
        }

        if (!stops.ContainsKey(1))
        {
            stops[1] = [];
        }

        var ordered = stops
            .OrderBy(pair => pair.Key)
            .Select(pair => new KeyframeStop(pair.Key, Deduplicate(pair.Value)))
            .ToArray();

        return new KeyframeTrack(ordered);
    }

    private static KeyframeDeclaration[] Deduplicate(List<KeyframeDeclaration> declarations)
    {
        if (declarations.Count <= 1)
        {
            return declarations.ToArray();
        }

        var byProperty = new Dictionary<CssProperty, string>();

        for (var i = 0; i < declarations.Count; i++)
        {
            byProperty[declarations[i].Property] = declarations[i].Value;
        }

        var result = new KeyframeDeclaration[byProperty.Count];
        var index = 0;

        foreach (var pair in byProperty)
        {
            result[index++] = new KeyframeDeclaration(pair.Key, pair.Value);
        }

        return result;
    }

    private void CollectOffsets(int keyId, List<float> offsets)
    {
        var tokens = _table.GetSelectorSpan(keyId);
        var builder = new StringBuilder();

        for (var i = 0; i < tokens.Length; i++)
        {
            builder.Append(InternTable.GetString(tokens[i].InternedId));
        }

        var parts = builder.ToString().Split(',');

        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i].Trim().ToLowerInvariant();

            if (part == "from")
            {
                offsets.Add(0);
            }
            else if (part == "to")
            {
                offsets.Add(1);
            }
            else if (part.EndsWith('%')
                && float.TryParse(part[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var percent))
            {
                offsets.Add(Math.Clamp(percent / 100f, 0, 1));
            }
            else if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                offsets.Add(Math.Clamp(number, 0, 1));
            }
        }
    }

    // -------------------------------------------------------------- timeline

    private static bool TryEvaluate(
        AnimationSpec spec,
        double start,
        double now,
        out float progress,
        out bool running)
    {
        progress = 0;
        running = false;

        if (spec.Duration <= 0 || spec.IterationCount == 0)
        {
            return false;
        }

        var local = now - start - spec.Delay;

        if (local < 0)
        {
            // The animation is still waiting for its delay. Keep the frame
            // loop alive so it can start, and apply the backwards fill if any.
            running = true;

            if (spec.FillMode is AnimationFillMode.Backwards or AnimationFillMode.Both)
            {
                progress = DirectionalProgress(spec, 0, 0);
                return true;
            }

            return false;
        }

        var iterations = local / spec.Duration;
        var iteration = (int)Math.Floor(iterations);
        var fraction = (float)(iterations - iteration);

        if (spec.IterationCount > 0 && iteration >= spec.IterationCount)
        {
            if (spec.FillMode is AnimationFillMode.Forwards or AnimationFillMode.Both)
            {
                var last = Math.Max(0, (int)Math.Ceiling(spec.IterationCount) - 1);
                progress = DirectionalProgress(spec, last, 1);
                return true;
            }

            return false;
        }

        progress = DirectionalProgress(spec, iteration, fraction);
        running = true;
        return true;
    }

    private static float DirectionalProgress(AnimationSpec spec, int iteration, float fraction)
    {
        var reverse = spec.Direction switch
        {
            AnimationDirection.Reverse => true,
            AnimationDirection.Alternate => (iteration & 1) == 1,
            AnimationDirection.AlternateReverse => (iteration & 1) == 0,
            _ => false
        };

        var progress = reverse ? 1 - fraction : fraction;
        return ApplyTimingFunction(spec.TimingFunction, progress);
    }

    private static float ApplyTimingFunction(string timing, float t)
    {
        t = Math.Clamp(t, 0, 1);

        switch (timing.Trim().ToLowerInvariant())
        {
            case "linear":
                return t;

            case "ease":
                return CubicBezier(0.25f, 0.1f, 0.25f, 1f, t);

            case "ease-in":
                return CubicBezier(0.42f, 0f, 1f, 1f, t);

            case "ease-out":
                return CubicBezier(0f, 0f, 0.58f, 1f, t);

            case "ease-in-out":
                return CubicBezier(0.42f, 0f, 0.58f, 1f, t);

            case "step-start":
                return t >= 1 ? 1 : 1;

            case "step-end":
                return t >= 1 ? 1 : 0;
        }

        var text = timing.Trim();

        if (text.StartsWith("cubic-bezier(", StringComparison.OrdinalIgnoreCase))
        {
            var values = ParseNumbers(text, 4);

            if (values is { Length: 4 })
            {
                return CubicBezier(values[0], values[1], values[2], values[3], t);
            }
        }
        else if (text.StartsWith("steps(", StringComparison.OrdinalIgnoreCase))
        {
            var values = ParseNumbers(text, 2);
            var steps = values is { Length: >= 1 } ? Math.Max(1, (int)values[0]) : 1;
            var position = text.Contains("start", StringComparison.OrdinalIgnoreCase)
                ? "start"
                : "end";

            return position == "start"
                ? MathF.Ceiling(t * steps) / steps
                : MathF.Floor(t * steps) / steps;
        }

        return t;
    }

    private static float[]? ParseNumbers(string text, int max)
    {
        var open = text.IndexOf('(');
        var close = text.LastIndexOf(')');

        if (open < 0 || close <= open)
        {
            return null;
        }

        var parts = text[(open + 1)..close].Split(',');
        var values = new float[Math.Min(max, parts.Length)];

        for (var i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        return values;
    }

    private static float CubicBezier(float x1, float y1, float x2, float y2, float x)
    {
        if (x <= 0) return 0;
        if (x >= 1) return 1;

        var t = x;

        for (var i = 0; i < 8; i++)
        {
            var estimate = Bezier(t, x1, x2);
            var derivative = BezierDerivative(t, x1, x2);

            if (MathF.Abs(derivative) < 1e-5f)
            {
                break;
            }

            t -= (estimate - x) / derivative;
            t = Math.Clamp(t, 0, 1);
        }

        return Bezier(t, y1, y2);
    }

    private static float Bezier(float t, float p1, float p2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * t * p1 + 3 * inverse * t * t * p2 + t * t * t;
    }

    private static float BezierDerivative(float t, float p1, float p2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * p1 + 6 * inverse * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    // ----------------------------------------------------------- application

    private void ApplyTrack(
        ComputedStyle style,
        ComputedStyle baseStyle,
        ComputedStyle? parent,
        KeyframeTrack track,
        float progress)
    {
        if (!FindSegment(track, progress, out var from, out var to, out var t))
        {
            return;
        }

        for (var i = 0; i < from.Declarations.Length; i++)
        {
            var declaration = from.Declarations[i];
            var toValue = FindDeclaration(to, declaration.Property)
                ?? GetBaseValue(baseStyle, declaration.Property)
                ?? declaration.Value;

            ApplyAnimated(style, parent, declaration.Property, declaration.Value, toValue, t);
        }

        for (var i = 0; i < to.Declarations.Length; i++)
        {
            var declaration = to.Declarations[i];

            if (FindDeclaration(from, declaration.Property) is not null)
            {
                continue;
            }

            var fromValue = GetBaseValue(baseStyle, declaration.Property) ?? declaration.Value;
            ApplyAnimated(style, parent, declaration.Property, fromValue, declaration.Value, t);
        }
    }

    private static bool FindSegment(
        KeyframeTrack track,
        float progress,
        out KeyframeStop from,
        out KeyframeStop to,
        out float t)
    {
        var stops = track.Stops;
        from = stops[0];
        to = stops[0];
        t = 0;

        if (stops.Length == 1 || progress <= stops[0].Offset)
        {
            return true;
        }

        if (progress >= stops[^1].Offset)
        {
            from = stops[^1];
            to = stops[^1];
            return true;
        }

        for (var i = 0; i < stops.Length - 1; i++)
        {
            var a = stops[i];
            var b = stops[i + 1];

            if (progress > b.Offset)
            {
                continue;
            }

            from = a;
            to = b;
            var span = b.Offset - a.Offset;
            t = span <= 0 ? 0 : (progress - a.Offset) / span;
            return true;
        }

        return true;
    }

    private static string? FindDeclaration(KeyframeStop stop, CssProperty property)
    {
        for (var i = 0; i < stop.Declarations.Length; i++)
        {
            if (stop.Declarations[i].Property == property)
            {
                return stop.Declarations[i].Value;
            }
        }

        return null;
    }

    private static bool IsPaintOnly(CssProperty property) => property switch
    {
        CssProperty.Opacity
            or CssProperty.Transform
            or CssProperty.Background
            or CssProperty.BackgroundColor
            or CssProperty.BorderColor
            or CssProperty.BorderTopColor
            or CssProperty.BorderRightColor
            or CssProperty.BorderBottomColor
            or CssProperty.BorderLeftColor
            or CssProperty.OutlineColor
            or CssProperty.BoxShadow
            or CssProperty.Filter
            or CssProperty.BackdropFilter
            or CssProperty.ClipPath
            or CssProperty.Mask => true,
        _ => false
    };

    private void ApplyAnimated(
        ComputedStyle style,
        ComputedStyle? parent,
        CssProperty property,
        string from,
        string to,
        float t)
    {
        if (!IsPaintOnly(property))
        {
            HasLayoutAnimations = true;
        }

        from = Clean(from);
        to = Clean(to);

        if (from.Length == 0 || to.Length == 0
            || from.Contains("var(", StringComparison.OrdinalIgnoreCase)
            || to.Contains("var(", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        switch (property)
        {
            case CssProperty.Opacity:
                style.Opacity = Math.Clamp(Lerp(ParseNumber(from), ParseNumber(to), t), 0, 1);
                break;

            case CssProperty.Transform:
                style.Transform = InterpolateTransform(
                    StyleResolver.ParseTransform(from),
                    StyleResolver.ParseTransform(to),
                    t);
                break;

            case CssProperty.Color:
                style.Color = LerpColor(from, to, t, style.Color);
                break;

            case CssProperty.BackgroundColor:
                style.BackgroundColor = LerpColor(from, to, t, style.BackgroundColor);
                break;

            case CssProperty.BorderColor:
            {
                var color = LerpColor(from, to, t, style.BorderColorTop);
                style.BorderColorTop = color;
                style.BorderColorRight = color;
                style.BorderColorBottom = color;
                style.BorderColorLeft = color;
                break;
            }

            case CssProperty.BorderTopColor:
                style.BorderColorTop = LerpColor(from, to, t, style.BorderColorTop);
                break;

            case CssProperty.BorderRightColor:
                style.BorderColorRight = LerpColor(from, to, t, style.BorderColorRight);
                break;

            case CssProperty.BorderBottomColor:
                style.BorderColorBottom = LerpColor(from, to, t, style.BorderColorBottom);
                break;

            case CssProperty.BorderLeftColor:
                style.BorderColorLeft = LerpColor(from, to, t, style.BorderColorLeft);
                break;

            case CssProperty.OutlineColor:
                style.OutlineColor = LerpColor(from, to, t, style.OutlineColor);
                break;

            case CssProperty.Width:
                style.Width = LerpLength(from, to, t, style.Width);
                break;

            case CssProperty.Height:
                style.Height = LerpLength(from, to, t, style.Height);
                break;

            case CssProperty.MinWidth:
                style.MinWidth = LerpLength(from, to, t, style.MinWidth);
                break;

            case CssProperty.MinHeight:
                style.MinHeight = LerpLength(from, to, t, style.MinHeight);
                break;

            case CssProperty.MaxWidth:
                style.MaxWidth = LerpLength(from, to, t, style.MaxWidth);
                break;

            case CssProperty.MaxHeight:
                style.MaxHeight = LerpLength(from, to, t, style.MaxHeight);
                break;

            case CssProperty.Top:
                style.Top = LerpLength(from, to, t, style.Top);
                break;

            case CssProperty.Right:
                style.Right = LerpLength(from, to, t, style.Right);
                break;

            case CssProperty.Bottom:
                style.Bottom = LerpLength(from, to, t, style.Bottom);
                break;

            case CssProperty.Left:
                style.Left = LerpLength(from, to, t, style.Left);
                break;

            case CssProperty.FontSize:
                style.FontSize = LerpLength(from, to, t, style.FontSize);
                break;

            case CssProperty.LineHeight:
                style.LineHeight = LerpLength(from, to, t, style.LineHeight);
                break;

            case CssProperty.LetterSpacing:
                style.LetterSpacing = LerpLength(from, to, t, style.LetterSpacing);
                break;

            case CssProperty.RowGap:
                style.RowGap = LerpLength(from, to, t, style.RowGap);
                break;

            case CssProperty.ColumnGap:
                style.ColumnGap = LerpLength(from, to, t, style.ColumnGap);
                break;

            case CssProperty.OutlineWidth:
                style.OutlineWidth = LerpLength(from, to, t, style.OutlineWidth);
                break;

            case CssProperty.BorderSpacing:
                style.BorderSpacing = LerpLength(from, to, t, style.BorderSpacing);
                break;

            case CssProperty.Margin:
                style.Margin = LerpLength4(from, to, t, style.Margin);
                break;

            case CssProperty.Padding:
                style.Padding = LerpLength4(from, to, t, style.Padding);
                break;

            case CssProperty.BorderWidth:
                style.BorderWidth = LerpLength4(from, to, t, style.BorderWidth);
                break;

            case CssProperty.MarginTop:
                style.Margin = style.Margin.WithTop(LerpLength(from, to, t, style.Margin.Top));
                break;

            case CssProperty.MarginRight:
                style.Margin = style.Margin.WithRight(LerpLength(from, to, t, style.Margin.Right));
                break;

            case CssProperty.MarginBottom:
                style.Margin = style.Margin.WithBottom(LerpLength(from, to, t, style.Margin.Bottom));
                break;

            case CssProperty.MarginLeft:
                style.Margin = style.Margin.WithLeft(LerpLength(from, to, t, style.Margin.Left));
                break;

            case CssProperty.PaddingTop:
                style.Padding = style.Padding.WithTop(LerpLength(from, to, t, style.Padding.Top));
                break;

            case CssProperty.PaddingRight:
                style.Padding = style.Padding.WithRight(LerpLength(from, to, t, style.Padding.Right));
                break;

            case CssProperty.PaddingBottom:
                style.Padding = style.Padding.WithBottom(LerpLength(from, to, t, style.Padding.Bottom));
                break;

            case CssProperty.PaddingLeft:
                style.Padding = style.Padding.WithLeft(LerpLength(from, to, t, style.Padding.Left));
                break;

            case CssProperty.BorderTopWidth:
                style.BorderWidth = style.BorderWidth.WithTop(LerpLength(from, to, t, style.BorderWidth.Top));
                break;

            case CssProperty.BorderRightWidth:
                style.BorderWidth = style.BorderWidth.WithRight(LerpLength(from, to, t, style.BorderWidth.Right));
                break;

            case CssProperty.BorderBottomWidth:
                style.BorderWidth = style.BorderWidth.WithBottom(LerpLength(from, to, t, style.BorderWidth.Bottom));
                break;

            case CssProperty.BorderLeftWidth:
                style.BorderWidth = style.BorderWidth.WithLeft(LerpLength(from, to, t, style.BorderWidth.Left));
                break;

            case CssProperty.FontWeight:
                style.FontWeight = (int)MathF.Round(Lerp(ParseNumber(from), ParseNumber(to), t));
                break;

            case CssProperty.ZIndex:
                style.ZIndex = (int)MathF.Round(Lerp(ParseNumber(from), ParseNumber(to), t));
                style.HasZIndex = true;
                break;

            case CssProperty.Visibility:
                style.Visibility = ParseVisibility(t < 0.5f ? from : to, style.Visibility);
                break;

            case CssProperty.Display:
                style.Display = ParseDisplay(t < 0.5f ? from : to, style.Display);
                break;

            case CssProperty.TransformOrigin:
            {
                var values = SplitValues(from);
                var target = SplitValues(to);

                if (values.Count > 0 && target.Count > 0)
                {
                    style.TransformOriginX = LerpLength(values[0], target[0], t, style.TransformOriginX);
                    style.TransformOriginY = LerpLength(
                        values.Count > 1 ? values[1] : values[0],
                        target.Count > 1 ? target[1] : target[0],
                        t,
                        style.TransformOriginY);
                }

                break;
            }
        }
    }

    // -------------------------------------------------------------- helpers

    private static string Clean(string value)
    {
        var important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);

        if (important >= 0)
        {
            value = value[..important];
        }

        return value.Trim();
    }

    private static float ParseNumber(string value)
    {
        return CssValueParser.ParseNumber(Clean(value));
    }

    private static float Lerp(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    private static CssLength LerpLength(string from, string to, float t, CssLength fallback)
    {
        return LerpLength(CssValueParser.ParseLength(Clean(from)), CssValueParser.ParseLength(Clean(to)), t, fallback);
    }

    private static CssLength LerpLength(CssLength from, CssLength to, float t, CssLength fallback)
    {
        if (from.Unit != to.Unit
            || from.Unit is LengthUnit.Auto or LengthUnit.MinContent or LengthUnit.MaxContent
                or LengthUnit.FitContent or LengthUnit.Stretch or LengthUnit.Fr)
        {
            return t < 0.5f ? from : to;
        }

        return MakeLength(from.Unit, Lerp(from.Value, to.Value, t));
    }

    private static CssLength MakeLength(LengthUnit unit, float value)
    {
        return unit switch
        {
            LengthUnit.Px => CssLength.Px(value),
            LengthUnit.Percent => CssLength.Percent(value),
            LengthUnit.Em => CssLength.Em(value),
            LengthUnit.Rem => CssLength.Rem(value),
            LengthUnit.Number => CssLength.Number(value),
            _ => CssLength.Px(value)
        };
    }

    private static CssLength4 LerpLength4(string from, string to, float t, CssLength4 fallback)
    {
        var a = ParseLength4(from, fallback);
        var b = ParseLength4(to, fallback);
        return new CssLength4(
            LerpLength(a.Top, b.Top, t, fallback.Top),
            LerpLength(a.Right, b.Right, t, fallback.Right),
            LerpLength(a.Bottom, b.Bottom, t, fallback.Bottom),
            LerpLength(a.Left, b.Left, t, fallback.Left));
    }

    private static CssLength4 ParseLength4(string value, CssLength4 fallback)
    {
        var parts = SplitValues(value);

        if (parts.Count == 0)
        {
            return fallback;
        }

        return parts.Count switch
        {
            1 => new CssLength4(parts[0], parts[0], parts[0], parts[0]),
            2 => new CssLength4(parts[0], parts[1], parts[0], parts[1]),
            3 => new CssLength4(parts[0], parts[1], parts[2], parts[1]),
            _ => new CssLength4(parts[0], parts[1], parts[2], parts[3])
        };
    }

    private static List<CssLength> SplitValues(string value)
    {
        var parts = new List<CssLength>();
        var start = -1;

        for (var i = 0; i <= value.Length; i++)
        {
            if (i == value.Length || char.IsWhiteSpace(value[i]))
            {
                if (start >= 0)
                {
                    parts.Add(CssValueParser.ParseLength(value[start..i]));
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        return parts;
    }

    private static uint LerpColor(string from, string to, float t, uint fallback)
    {
        if (!CssColor.TryParse(Clean(from), CssColor.Black, out var a)
            || !CssColor.TryParse(Clean(to), CssColor.Black, out var b))
        {
            return fallback;
        }

        return CssColor.Pack(
            (byte)Math.Clamp(Lerp(CssColor.Red(a), CssColor.Red(b), t), 0, 255),
            (byte)Math.Clamp(Lerp(CssColor.Green(a), CssColor.Green(b), t), 0, 255),
            (byte)Math.Clamp(Lerp(CssColor.Blue(a), CssColor.Blue(b), t), 0, 255),
            (byte)Math.Clamp(Lerp(CssColor.Alpha(a), CssColor.Alpha(b), t), 0, 255));
    }

    private static Visibility ParseVisibility(string value, Visibility fallback)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "visible" => Visibility.Visible,
            "hidden" => Visibility.Hidden,
            "collapse" => Visibility.Collapse,
            _ => fallback
        };
    }

    private static Display ParseDisplay(string value, Display fallback)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "none" => Display.None,
            "block" => Display.Block,
            "inline" => Display.Inline,
            "inline-block" => Display.InlineBlock,
            "flex" => Display.Flex,
            "inline-flex" => Display.InlineFlex,
            "grid" => Display.Grid,
            "inline-grid" => Display.InlineGrid,
            "contents" => Display.Contents,
            "table" => Display.Table,
            "inline-table" => Display.InlineTable,
            "list-item" => Display.ListItem,
            _ => fallback
        };
    }

    private static string? GetBaseValue(ComputedStyle style, CssProperty property)
    {
        return property switch
        {
            CssProperty.Opacity => style.Opacity.ToString("0.###", CultureInfo.InvariantCulture),
            CssProperty.Color => FormatColor(style.Color),
            CssProperty.BackgroundColor => FormatColor(style.BackgroundColor),
            CssProperty.BorderColor => FormatColor(style.BorderColorTop),
            CssProperty.BorderTopColor => FormatColor(style.BorderColorTop),
            CssProperty.BorderRightColor => FormatColor(style.BorderColorRight),
            CssProperty.BorderBottomColor => FormatColor(style.BorderColorBottom),
            CssProperty.BorderLeftColor => FormatColor(style.BorderColorLeft),
            CssProperty.OutlineColor => FormatColor(style.OutlineColor),
            CssProperty.Width => style.Width.ToString(),
            CssProperty.Height => style.Height.ToString(),
            CssProperty.MinWidth => style.MinWidth.ToString(),
            CssProperty.MinHeight => style.MinHeight.ToString(),
            CssProperty.MaxWidth => style.MaxWidth.ToString(),
            CssProperty.MaxHeight => style.MaxHeight.ToString(),
            CssProperty.Top => style.Top.ToString(),
            CssProperty.Right => style.Right.ToString(),
            CssProperty.Bottom => style.Bottom.ToString(),
            CssProperty.Left => style.Left.ToString(),
            CssProperty.FontSize => style.FontSize.ToString(),
            CssProperty.LineHeight => style.LineHeight.ToString(),
            CssProperty.LetterSpacing => style.LetterSpacing.ToString(),
            CssProperty.RowGap => style.RowGap.ToString(),
            CssProperty.ColumnGap => style.ColumnGap.ToString(),
            CssProperty.OutlineWidth => style.OutlineWidth.ToString(),
            CssProperty.BorderSpacing => style.BorderSpacing.ToString(),
            CssProperty.MarginTop => style.Margin.Top.ToString(),
            CssProperty.MarginRight => style.Margin.Right.ToString(),
            CssProperty.MarginBottom => style.Margin.Bottom.ToString(),
            CssProperty.MarginLeft => style.Margin.Left.ToString(),
            CssProperty.PaddingTop => style.Padding.Top.ToString(),
            CssProperty.PaddingRight => style.Padding.Right.ToString(),
            CssProperty.PaddingBottom => style.Padding.Bottom.ToString(),
            CssProperty.PaddingLeft => style.Padding.Left.ToString(),
            CssProperty.BorderTopWidth => style.BorderWidth.Top.ToString(),
            CssProperty.BorderRightWidth => style.BorderWidth.Right.ToString(),
            CssProperty.BorderBottomWidth => style.BorderWidth.Bottom.ToString(),
            CssProperty.BorderLeftWidth => style.BorderWidth.Left.ToString(),
            CssProperty.FontWeight => style.FontWeight.ToString(CultureInfo.InvariantCulture),
            CssProperty.ZIndex => style.ZIndex.ToString(CultureInfo.InvariantCulture),
            CssProperty.Visibility => style.Visibility.ToString().ToLowerInvariant(),
            CssProperty.Display => style.Display.ToString().ToLowerInvariant(),
            CssProperty.Transform => style.Transform is null ? "none" : FormatTransform(style.Transform),
            CssProperty.TransformOrigin => style.TransformOriginX + " " + style.TransformOriginY,
            _ => null
        };
    }

    private static string FormatColor(uint color)
    {
        return $"#{CssColor.Red(color):X2}{CssColor.Green(color):X2}{CssColor.Blue(color):X2}{CssColor.Alpha(color):X2}";
    }

    private static string FormatTransform(TransformSpec spec)
    {
        if (spec.Operations.Length == 0)
        {
            return "none";
        }

        var builder = new StringBuilder();

        for (var i = 0; i < spec.Operations.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            var operation = spec.Operations[i];

            switch (operation.Kind)
            {
                case TransformKind.Translate:
                    builder.Append("translate(")
                        .Append(operation.X.ToString())
                        .Append(", ")
                        .Append(operation.Y.IsAuto ? operation.X.ToString() : operation.Y.ToString())
                        .Append(')');
                    break;

                case TransformKind.Scale:
                    builder.Append("scale(")
                        .Append(operation.X.ToString());

                    if (!operation.Y.IsAuto)
                    {
                        builder.Append(", ").Append(operation.Y.ToString());
                    }

                    builder.Append(')');
                    break;

                case TransformKind.Rotate:
                    builder.Append("rotate(")
                        .Append(operation.AngleDegrees.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append("deg)");
                    break;

                case TransformKind.Skew:
                    builder.Append("skew(")
                        .Append(operation.AngleDegrees.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append("deg, ")
                        .Append(operation.Y.Value.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append("deg)");
                    break;

                case TransformKind.Matrix:
                    builder.Append("matrix(")
                        .Append(operation.Matrix.M11.ToString("0.######", CultureInfo.InvariantCulture)).Append(", ")
                        .Append(operation.Matrix.M12.ToString("0.######", CultureInfo.InvariantCulture)).Append(", ")
                        .Append(operation.Matrix.M21.ToString("0.######", CultureInfo.InvariantCulture)).Append(", ")
                        .Append(operation.Matrix.M22.ToString("0.######", CultureInfo.InvariantCulture)).Append(", ")
                        .Append(operation.Matrix.M31.ToString("0.######", CultureInfo.InvariantCulture)).Append(", ")
                        .Append(operation.Matrix.M32.ToString("0.######", CultureInfo.InvariantCulture)).Append(')');
                    break;
            }
        }

        return builder.ToString();
    }

    private static TransformSpec? InterpolateTransform(TransformSpec? from, TransformSpec? to, float t)
    {
        if (from is null && to is null)
        {
            return null;
        }

        from ??= IdentityFor(to!);
        to ??= IdentityFor(from);

        if (from.Operations.Length != to.Operations.Length)
        {
            return t < 0.5f ? from : to;
        }

        var operations = new TransformOperation[from.Operations.Length];

        for (var i = 0; i < operations.Length; i++)
        {
            var a = from.Operations[i];
            var b = to.Operations[i];

            if (a.Kind != b.Kind)
            {
                return t < 0.5f ? from : to;
            }

            operations[i] = a.Kind switch
            {
                TransformKind.Rotate => a with
                {
                    AngleDegrees = Lerp(a.AngleDegrees, b.AngleDegrees, t)
                },
                TransformKind.Translate or TransformKind.Scale => a with
                {
                    X = LerpLength(a.X, b.X, t, a.X),
                    Y = LerpLength(a.Y, b.Y, t, a.Y)
                },
                TransformKind.Skew => a with
                {
                    Y = LerpLength(a.Y, b.Y, t, a.Y),
                    AngleDegrees = Lerp(a.AngleDegrees, b.AngleDegrees, t)
                },
                TransformKind.Matrix => a with
                {
                    Matrix = LerpMatrix(a.Matrix, b.Matrix, t)
                },
                _ => a
            };
        }

        return new TransformSpec(operations);
    }

    private static TransformSpec IdentityFor(TransformSpec spec)
    {
        var operations = new TransformOperation[spec.Operations.Length];

        for (var i = 0; i < operations.Length; i++)
        {
            var operation = spec.Operations[i];

            operations[i] = operation.Kind switch
            {
                TransformKind.Rotate => operation with { AngleDegrees = 0 },
                TransformKind.Translate => operation with
                {
                    X = CssLength.Px(0),
                    Y = CssLength.Px(0)
                },
                TransformKind.Scale => operation with
                {
                    X = CssLength.Number(1),
                    Y = CssLength.Number(1)
                },
                TransformKind.Skew => operation with
                {
                    AngleDegrees = 0,
                    Y = CssLength.Number(0)
                },
                TransformKind.Matrix => operation with { Matrix = Matrix3x2.Identity },
                _ => operation
            };
        }

        return new TransformSpec(operations);
    }

    private static Matrix3x2 LerpMatrix(Matrix3x2 from, Matrix3x2 to, float t)
    {
        return new Matrix3x2(
            Lerp(from.M11, to.M11, t),
            Lerp(from.M12, to.M12, t),
            Lerp(from.M21, to.M21, t),
            Lerp(from.M22, to.M22, t),
            Lerp(from.M31, to.M31, t),
            Lerp(from.M32, to.M32, t));
    }

    private static int Signature(List<AnimationSpec> animations)
    {
        var hash = new HashCode();

        for (var i = 0; i < animations.Count; i++)
        {
            var spec = animations[i];
            hash.Add(spec.Name);
            hash.Add(spec.Duration);
            hash.Add(spec.Delay);
            hash.Add(spec.IterationCount);
            hash.Add((int)spec.Direction);
            hash.Add((int)spec.FillMode);
            hash.Add((int)spec.PlayState);
            hash.Add(spec.TimingFunction);
        }

        return hash.ToHashCode();
    }

    private sealed class NodeAnimationState
    {
        public int Signature;
        public double StartTime;
        public int Frame;

        /// <summary>
        /// The animated style is reused between frames so descendants keep a
        /// stable parent identity and their cached styles stay valid.
        /// </summary>
        public ComputedStyle? Animated;
    }

    private sealed class KeyframeTrack
    {
        public KeyframeTrack(KeyframeStop[] stops)
        {
            Stops = stops;
        }

        public KeyframeStop[] Stops { get; }
    }

    private sealed class KeyframeStop
    {
        public KeyframeStop(float offset, KeyframeDeclaration[] declarations)
        {
            Offset = offset;
            Declarations = declarations;
        }

        public float Offset { get; }

        public KeyframeDeclaration[] Declarations { get; }
    }

    private readonly record struct KeyframeDeclaration(CssProperty Property, string Value);
}
