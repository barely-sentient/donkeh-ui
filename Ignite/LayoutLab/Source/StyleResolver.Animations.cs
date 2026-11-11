using System.Globalization;

namespace Ignite.LayoutLab;

public sealed partial class StyleResolver
{
    /// <summary>
    /// Returns the style's animation list, allocating it on first use.
    /// </summary>
    /// <param name="style">The style whose animation list is needed.</param>
    /// <returns>The existing list or a new empty <see cref="List{T}"/>.</returns>
    /// <remarks>
    /// The list is mutable and lives on <see cref="ComputedStyle.Animations"/>.
    /// Longhands append to it rather than replacing it, so the order of animation
    /// layers is preserved.
    /// </remarks>
    private static List<AnimationSpec> EnsureAnimationList(ComputedStyle style)
    {
        return style.Animations ??= [];
    }

    /// <summary>
    /// Applies one comma-separated animation longhand across the animation list.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The longhand value, possibly a comma-separated list.</param>
    /// <param name="apply">
    /// Sets one field on the spec at the matching index. It is only called for
    /// non-empty parts.
    /// </param>
    /// <remarks>
    /// <para>
    /// Entries are aligned by list position, and the list grows with default
    /// specs as needed. Empty parts still consume an index, so
    /// <c>a, , c</c> leaves the middle animation at its defaults.
    /// </para>
    /// <para>
    /// This overlays the existing list instead of resetting it, which lets
    /// longhands refine an earlier shorthand. A longhand with fewer entries than
    /// the current list leaves the trailing animations unchanged.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Applies <c>animation-name</c>, stripping optional quotes from each name.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A name or comma-separated name list.</param>
    private static void ApplyAnimationName(ComputedStyle style, string value)
    {
        ApplyAnimationLonghand(style, value, static (spec, part) =>
        {
            spec.Name = StripQuotes(part);
        });
    }

    /// <summary>
    /// Applies a time-valued animation longhand, such as duration or delay.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A time or comma-separated time list.</param>
    /// <param name="apply">Sets the field on the matching spec, in seconds.</param>
    /// <remarks>
    /// Parts that are not valid CSS times are skipped, leaving the existing
    /// value. See <see cref="TryParseTime"/> for the accepted forms.
    /// </remarks>
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

    /// <summary>
    /// Applies <c>animation-iteration-count</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A number or <c>infinite</c>, or a comma-separated list.</param>
    /// <remarks>
    /// <c>infinite</c> is stored as -1, the sentinel understood by
    /// <see cref="AnimationRuntime"/>. Numeric counts are clamped to zero and
    /// negative values are therefore not supported.
    /// </remarks>
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

    /// <summary>
    /// Applies <c>animation-direction</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A direction keyword or comma-separated list.</param>
    /// <remarks>Unrecognised keywords leave the existing direction unchanged.</remarks>
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

    /// <summary>
    /// Applies <c>animation-fill-mode</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A fill-mode keyword or comma-separated list.</param>
    /// <remarks>Unrecognised keywords leave the existing fill mode unchanged.</remarks>
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

    /// <summary>
    /// Applies <c>animation-play-state</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A play-state keyword or comma-separated list.</param>
    /// <remarks>
    /// Only <c>running</c> and <c>paused</c> are recognised; other text is
    /// ignored.
    /// </remarks>
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

    /// <summary>
    /// Applies <c>animation-timing-function</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">A timing function or comma-separated list.</param>
    /// <remarks>
    /// The function text is stored verbatim (not parsed) so the animation runtime
    /// can evaluate keywords, <c>cubic-bezier()</c>, <c>steps()</c> and
    /// <c>linear()</c> itself.
    /// </remarks>
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

    /// <summary>
    /// Parses the comma-separated <c>animation</c> shorthand into specs.
    /// </summary>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <returns>
    /// One spec per non-empty layer, or <see langword="null"/> when the value has
    /// no layers.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Tokens are classified by shape rather than position, which mirrors the
    /// unordered CSS grammar. The first time is the duration and the second is
    /// the delay. <c>infinite</c> sets the iteration count to -1; a number sets
    /// it directly. Direction, fill mode, play state and timing function are
    /// recognised by <see cref="TryParseDirection"/>, <see cref="TryParseFillMode"/>,
    /// <see cref="IsTimingFunction"/> and the running/paused keywords.
    /// </para>
    /// <para>
    /// Any token that is not recognised becomes the animation name, so an
    /// identifier after a keyword works in any order. If several such tokens
    /// appear, the last one wins. A layer with no name keeps the initial
    /// <c>none</c> name.
    /// </para>
    /// <para>
    /// The result replaces <see cref="ComputedStyle.Animations"/> wholesale;
    /// subsequent longhands then overlay it by index.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Parses a CSS time value into seconds.
    /// </summary>
    /// <param name="text">The token to parse.</param>
    /// <param name="seconds">Receives the time in seconds on success.</param>
    /// <returns><see langword="true"/> for a valid time token.</returns>
    /// <remarks>
    /// Accepts <c>ms</c> and <c>s</c> suffixes and a bare <c>0</c>. A bare
    /// non-zero number is rejected so it can be interpreted as an iteration
    /// count. Negative times are accepted by the numeric parser even though CSS
    /// forbids them.
    /// </remarks>
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

    /// <summary>
    /// Recognises an <c>animation-direction</c> keyword.
    /// </summary>
    /// <param name="text">The keyword to test.</param>
    /// <param name="direction">
    /// Receives the matching direction, or <see cref="AnimationDirection.Normal"/>
    /// on failure.
    /// </param>
    /// <returns><see langword="true"/> when the keyword is recognised.</returns>
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

    /// <summary>
    /// Recognises an <c>animation-fill-mode</c> keyword.
    /// </summary>
    /// <param name="text">The keyword to test.</param>
    /// <param name="fill">
    /// Receives the matching mode, or <see cref="AnimationFillMode.None"/> on
    /// failure.
    /// </param>
    /// <returns><see langword="true"/> when the keyword is recognised.</returns>
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

    /// <summary>
    /// Determines whether a token is an animation timing function.
    /// </summary>
    /// <param name="text">The lower-case token.</param>
    /// <returns><see langword="true"/> for a keyword or supported function.</returns>
    /// <remarks>
    /// Recognises <c>linear</c>, <c>ease</c>, <c>ease-in</c>, <c>ease-out</c>,
    /// <c>ease-in-out</c>, <c>step-start</c>, <c>step-end</c>, and the
    /// <c>cubic-bezier(</c>, <c>steps(</c> and <c>linear(</c> function prefixes.
    /// </remarks>
    private static bool IsTimingFunction(string text)
    {
        return text is "linear" or "ease" or "ease-in" or "ease-out" or "ease-in-out"
            or "step-start" or "step-end"
            || text.StartsWith("cubic-bezier(", StringComparison.Ordinal)
            || text.StartsWith("steps(", StringComparison.Ordinal)
            || text.StartsWith("linear(", StringComparison.Ordinal);
    }

    /// <summary>
    /// Removes one pair of matching surrounding quotes from a token.
    /// </summary>
    /// <param name="text">The token, possibly quoted.</param>
    /// <returns>The unquoted text, trimmed; unchanged when not fully quoted.</returns>
    /// <remarks>
    /// Only the first and last characters are checked, so escaped quotes inside
    /// the token are preserved.
    /// </remarks>
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
    /// <param name="value">The text to split.</param>
    /// <returns>The non-empty tokens in source order.</returns>
    /// <remarks>
    /// Parentheses increase nesting depth and whitespace at depth zero separates
    /// tokens, so <c>cubic-bezier(0.1, 0.2, 0.3, 1)</c> stays one token. Quotes
    /// are not tracked, unlike <see cref="CssValueParser.SplitTopLevel"/>.
    /// </remarks>
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
