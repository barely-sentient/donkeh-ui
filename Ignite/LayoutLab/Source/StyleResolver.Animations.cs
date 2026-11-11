using System.Globalization;

namespace Ignite.LayoutLab;

public sealed partial class StyleResolver
{
    private static List<AnimationSpec> EnsureAnimationList(ComputedStyle style)
    {
        return style.Animations ??= [];
    }

    private static void ApplyAnimationLonghand(
        ComputedStyle style,
        string value,
        Action<AnimationSpec, string> apply)
    {
        var parts = CssValueParser.SplitTopLevel(value, ',');
        var list = EnsureAnimationList(style);

        for (var i = 0; i < parts.Count; i++)
        {
            while (list.Count <= i)
            {
                list.Add(new AnimationSpec());
            }

            var part = parts[i].Trim();

            if (part.Length > 0)
            {
                apply(list[i], part);
            }
        }
    }

    private static void ApplyAnimationName(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            spec.Name = StripQuotes(part);
        });
    }

    private static void ApplyAnimationTime(
        ComputedStyle style,
        string value,
        Action<AnimationSpec, float> apply)
    {
        ApplyAnimationLonghand(style, value, (spec, part) =>
        {
            if (TryParseTime(part, out var seconds))
            {
                apply(spec, seconds);
            }
        });
    }

    private static void ApplyAnimationIterationCount(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            if (part.Equals("infinite", StringComparison.OrdinalIgnoreCase))
            {
                spec.IterationCount = -1;
            }
            else if (float.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var count))
            {
                spec.IterationCount = MathF.Max(0, count);
            }
        });
    }

    private static void ApplyAnimationDirection(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            if (TryParseDirection(part, out var direction))
            {
                spec.Direction = direction;
            }
        });
    }

    private static void ApplyAnimationFillMode(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            if (TryParseFillMode(part, out var fill))
            {
                spec.FillMode = fill;
            }
        });
    }

    private static void ApplyAnimationPlayState(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            if (part.Equals("paused", StringComparison.OrdinalIgnoreCase))
            {
                spec.PlayState = AnimationPlayState.Paused;
            }
            else if (part.Equals("running", StringComparison.OrdinalIgnoreCase))
            {
                spec.PlayState = AnimationPlayState.Running;
            }
        });
    }

    private static void ApplyAnimationTimingFunction(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            if (part.Length > 0)
            {
                spec.TimingFunction = part;
            }
        });
    }

    private static List<AnimationSpec>? ParseAnimationShorthand(string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ',');
        var list = new List<AnimationSpec>(parts.Count);

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();

            if (part.Length == 0)
            {
                continue;
            }

            var spec = new AnimationSpec { Name = "none" };
            var tokens = SplitWhitespaceTopLevel(part);
            var timeCount = 0;
            var hasName = false;

            for (var t = 0; t < tokens.Count; t++)
            {
                var token = tokens[t];
                var lower = token.ToLowerInvariant();

                if (TryParseTime(lower, out var seconds))
                {
                    if (timeCount == 0)
                    {
                        spec.Duration = seconds;
                    }
                    else if (timeCount == 1)
                    {
                        spec.Delay = seconds;
                    }

                    timeCount++;
                    continue;
                }

                if (lower == "infinite")
                {
                    spec.IterationCount = -1;
                    continue;
                }

                if (float.TryParse(lower, NumberStyles.Float, CultureInfo.InvariantCulture, out var iterations))
                {
                    spec.IterationCount = MathF.Max(0, iterations);
                    continue;
                }

                if (TryParseDirection(lower, out var direction))
                {
                    spec.Direction = direction;
                    continue;
                }

                if (TryParseFillMode(lower, out var fill))
                {
                    spec.FillMode = fill;
                    continue;
                }

                if (lower is "running" or "paused")
                {
                    spec.PlayState = lower == "paused"
                        ? AnimationPlayState.Paused
                        : AnimationPlayState.Running;
                    continue;
                }

                if (IsTimingFunction(lower))
                {
                    spec.TimingFunction = lower;
                    continue;
                }

                spec.Name = StripQuotes(token);
                hasName = true;
            }

            if (!hasName)
            {
                spec.Name = "none";
            }

            list.Add(spec);
        }

        return list.Count == 0 ? null : list;
    }

    private static bool TryParseTime(string text, out float seconds)
    {
        seconds = 0;
        text = text.Trim().ToLowerInvariant();

        if (text.EndsWith("ms", StringComparison.Ordinal))
        {
            if (float.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ms))
            {
                seconds = ms / 1000f;
                return true;
            }

            return false;
        }

        if (text.EndsWith("s", StringComparison.Ordinal))
        {
            if (float.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var s))
            {
                seconds = s;
                return true;
            }

            return false;
        }

        // CSS requires a unit for non-zero times. A bare zero is common and
        // harmless, but a bare 1 must stay an iteration count.
        if (text == "0")
        {
            seconds = 0;
            return true;
        }

        return false;
    }

    private static bool TryParseDirection(string text, out AnimationDirection direction)
    {
        switch (text.ToLowerInvariant())
        {
            case "normal":
                direction = AnimationDirection.Normal;
                return true;
            case "reverse":
                direction = AnimationDirection.Reverse;
                return true;
            case "alternate":
                direction = AnimationDirection.Alternate;
                return true;
            case "alternate-reverse":
                direction = AnimationDirection.AlternateReverse;
                return true;
            default:
                direction = AnimationDirection.Normal;
                return false;
        }
    }

    private static bool TryParseFillMode(string text, out AnimationFillMode fill)
    {
        switch (text.ToLowerInvariant())
        {
            case "none":
                fill = AnimationFillMode.None;
                return true;
            case "forwards":
                fill = AnimationFillMode.Forwards;
                return true;
            case "backwards":
                fill = AnimationFillMode.Backwards;
                return true;
            case "both":
                fill = AnimationFillMode.Both;
                return true;
            default:
                fill = AnimationFillMode.None;
                return false;
        }
    }

    private static bool IsTimingFunction(string text)
    {
        return text is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out"
            or "step-start" or "step-end"
            || text.StartsWith("cubic-bezier(", StringComparison.Ordinal)
            || text.StartsWith("steps(", StringComparison.Ordinal)
            || text.StartsWith("linear(", StringComparison.Ordinal);
    }

    private static string StripQuotes(string text)
    {
        text = text.Trim();

        if (text.Length >= 2
            && (text[0] == '"' && text[^1] == '"' || text[0] == '\'' && text[^1] == '\''))
        {
            return text[1..^1];
        }

        return text;
    }

    /// <summary>Splits on whitespace while keeping parenthesised groups intact.</summary>
    private static List<string> SplitWhitespaceTopLevel(string value)
    {
        var tokens = new List<string>();
        var start = -1;
        var depth = 0;

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c == '(')
            {
                depth++;
            }
            else if (c == ')' && depth > 0)
            {
                depth--;
            }

            if (char.IsWhiteSpace(c) && depth == 0)
            {
                if (start >= 0)
                {
                    tokens.Add(value[start..i]);
                    start = -1;
                }
            }
            else if (start < 0)
            {
                start = i;
            }
        }

        if (start >= 0)
        {
            tokens.Add(value[start..]);
        }

        return tokens;
    }
}
