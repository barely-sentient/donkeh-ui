using System.Globalization;
using System.Numerics;
using System.Text;
using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.Parser;

namespace Donkeh.LayoutLab;

/// <summary>
/// Evaluates CSS keyframe animations and transitions during layout. The engine
/// keeps one runtime per <see cref="LayoutEngine"/> so animation start times
/// survive frames. Only properties with a supported interpolator are animated;
/// discrete properties switch at the segment midpoint and everything else
/// keeps its base value.
/// </summary>
/// <remarks>
/// <para>
/// The runtime is frame-driven and owns every piece of per-node animation
/// state. A typical pass calls <see cref="BeginFrame"/> before the box tree is
/// built, calls <see cref="Apply"/> once for each visited node, and calls
/// <see cref="Prune"/> after the tree is complete so state for unmounted nodes
/// is released.
/// </para>
/// <para>
/// Time is supplied by the caller in seconds, normally through
/// <see cref="LayoutOptions.AnimationTimeSeconds"/>; the runtime never reads
/// the wall clock. Start times are retained between frames, so each pass must
/// use the same monotonic clock and a non-decreasing time value.
/// </para>
/// <para>
/// Keyframe tracks are parsed lazily from the <see cref="DataTable"/> and
/// cached by interned animation name. The runtime is not thread-safe and is
/// expected to be used only by its owning <see cref="LayoutEngine"/>.
/// </para>
/// </remarks>
/// <seealso cref="AnimationSpec"/>
/// <seealso cref="ComputedStyle"/>
internal sealed class AnimationRuntime
{
    /// <summary>The rule table that supplies the parsed <c>@keyframes</c> rules.</summary>
    private readonly DataTable _table;
    /// <summary>
    /// Parsed keyframe tracks keyed by the interned animation name id. A
    /// <see langword="null"/> value records a name with no usable keyframes so
    /// the table is not rescanned.
    /// </summary>
    private readonly Dictionary<int, KeyframeTrack?> _tracks = [];
    /// <summary>
    /// Retained per-node timing and style state, keyed by node identity. An
    /// entry survives across frames until <see cref="Prune"/> sees that its
    /// node was not laid out in the current frame.
    /// </summary>
    private readonly Dictionary<IgniteSupportedDomNode, NodeAnimationState> _states = [];
    /// <summary>Scratch list reused by <see cref="Prune"/> to avoid per-frame allocation.</summary>
    private readonly List<IgniteSupportedDomNode> _staleStates = [];
    /// <summary>
    /// Monotonic frame counter advanced by <see cref="BeginFrame"/>. Nodes
    /// stamped with an older value are considered stale.
    /// </summary>
    private int _frame;

    /// <summary>
    /// Initialises a runtime that reads keyframe rules from
    /// <paramref name="table"/>.
    /// </summary>
    /// <param name="table">Rule table containing the parsed <c>@keyframes</c> rules.</param>
    /// <remarks>
    /// The table is retained by reference and must outlive the runtime.
    /// Keyframes are not parsed until <see cref="Apply"/> first requests a
    /// track by name.
    /// </remarks>
    public AnimationRuntime(DataTable table)
    {
        _table = table;
    }

    /// <summary>True when at least one animation was active in the current frame.</summary>
    /// <remarks>
    /// <para>
    /// Reset by <see cref="BeginFrame"/> and set by <see cref="Apply"/> while a
    /// node's animation is inside its active interval, including a pending
    /// delay. The flag is reported through <see cref="LayoutResult"/> so a host
    /// can schedule the next animation frame.
    /// </para>
    /// <para>
    /// A finished animation that is held by a forwards fill is still applied to
    /// the style, but <see cref="TryEvaluate"/> clears the running flag once the
    /// active interval has passed. A backwards fill during a delay, by contrast,
    /// keeps the flag set because the animation has not started yet.
    /// </para>
    /// </remarks>
    public bool HasRunningAnimations { get; private set; }

    /// <summary>
    /// True when an active animation changes geometry. Transform and opacity
    /// animations are paint-only and can refresh the display list without a
    /// full layout pass.
    /// </summary>
    /// <remarks>
    /// Reset by <see cref="BeginFrame"/>. The flag is raised by
    /// <see cref="ApplyAnimated"/> for any property not listed in
    /// <see cref="IsPaintOnly(CssProperty)"/>, even when the property has no
    /// handler and keeps its base value.
    /// </remarks>
    public bool HasLayoutAnimations { get; private set; }

    /// <summary>Number of nodes with retained animation state.</summary>
    /// <remarks>
    /// Exposed through <see cref="LayoutEngine.AnimationStateCount"/> for tests
    /// and diagnostics; a growing count across frames usually means
    /// <see cref="Prune"/> is not being called.
    /// </remarks>
    public int StateCount => _states.Count;

    /// <summary>
    /// Starts a new layout frame by clearing the per-frame animation flags and
    /// advancing the frame counter.
    /// </summary>
    /// <remarks>
    /// Call once before the box tree is built. <see cref="Apply"/> stamps every
    /// visited node with the new frame number, which lets <see cref="Prune"/>
    /// distinguish live state from state left behind by an earlier frame.
    /// Retained state, tracks and start times are not cleared.
    /// </remarks>
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
    /// <remarks>
    /// Call after the box tree has been rebuilt for the current frame. The scan
    /// is linear in the number of retained nodes and reuses an internal scratch
    /// list, so it does not allocate once that list has grown. Tracks cached by
    /// name are deliberately kept, since keyframes are independent of nodes.
    /// </remarks>
    /// <seealso cref="StateCount"/>
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

    /// <summary>
    /// Applies the animations declared by <paramref name="baseStyle"/> to a node
    /// and returns the style layout should use for it.
    /// </summary>
    /// <param name="node">Node being styled; identifies the retained animation state.</param>
    /// <param name="baseStyle">
    /// Fully resolved style before animation, including the
    /// <see cref="ComputedStyle.Animations"/> list.
    /// </param>
    /// <param name="parent">
    /// Parent style. It is threaded through to <see cref="ApplyAnimated"/> for
    /// animations that resolve against inherited values, but the current
    /// property interpolators do not read it.
    /// </param>
    /// <param name="now">Current time on the monotonic animation clock, in seconds.</param>
    /// <returns>
    /// <paramref name="baseStyle"/> when no animation is active, otherwise a
    /// cached animated clone marked with <see cref="ComputedStyle.IsAnimated"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// State is keyed by node. A first visit, or a change to any field captured
    /// by <see cref="Signature"/>, creates a new state whose start time is
    /// <paramref name="now"/>, so editing an animation restarts it. The node is
    /// stamped with the current frame for <see cref="Prune"/>. Multiple
    /// animations are applied in list order, so a later animation overwrites the
    /// properties written by an earlier one.
    /// </para>
    /// <para>
    /// The returned animated style is the same instance on every frame. It is
    /// refreshed from <paramref name="baseStyle"/> with
    /// <see cref="ComputedStyle.CopyAllFrom"/> and then mutated in place by the
    /// individual tracks, which keeps descendant style caches stable. Callers
    /// must treat the result as read-only and must not keep it after the node
    /// stops animating.
    /// </para>
    /// <para>
    /// Animation names that are empty, <c>none</c> or unmatched contribute
    /// nothing. A node whose style has no animations drops its state
    /// immediately; a node with only unusable names keeps an idle state for as
    /// long as it is laid out, and the state disappears when the node does or
    /// when its animation list becomes empty.
    /// </para>
    /// </remarks>
    public ComputedStyle Apply(
        IgniteSupportedDomNode node,
        ComputedStyle baseStyle,
        ComputedStyle? parent,
        double now)
    {
        var animations = baseStyle.Animations;
        var transitions = baseStyle.Transitions;

        if ((animations is null || animations.Count == 0)
            && (transitions is null || transitions.Count == 0))
        {
            _states.Remove(node);
            return baseStyle;
        }

        var signature = Signature(animations, transitions);

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

        if (animations is not null)
        {
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
        }

        animated = ApplyTransitions(baseStyle, parent, transitions, state, now, animated);

        if (animated is null)
        {
            return baseStyle;
        }

        animated.IsAnimated = true;
        return animated;
    }

    /// <summary>
    /// Properties observed when a transition layer targets <c>all</c>. The set
    /// mirrors the properties <see cref="ApplyAnimated"/> can interpolate.
    /// </summary>
    private static readonly CssProperty[] TransitionProperties =
    [
        CssProperty.Opacity,
        CssProperty.Transform,
        CssProperty.Color,
        CssProperty.BackgroundColor,
        CssProperty.BorderColor,
        CssProperty.BorderTopColor,
        CssProperty.BorderRightColor,
        CssProperty.BorderBottomColor,
        CssProperty.BorderLeftColor,
        CssProperty.OutlineColor,
        CssProperty.Width,
        CssProperty.Height,
        CssProperty.MinWidth,
        CssProperty.MinHeight,
        CssProperty.MaxWidth,
        CssProperty.MaxHeight,
        CssProperty.Top,
        CssProperty.Right,
        CssProperty.Bottom,
        CssProperty.Left,
        CssProperty.FontSize,
        CssProperty.LineHeight,
        CssProperty.LetterSpacing,
        CssProperty.RowGap,
        CssProperty.ColumnGap,
        CssProperty.OutlineWidth,
        CssProperty.BorderSpacing,
        CssProperty.Margin,
        CssProperty.Padding,
        CssProperty.BorderWidth,
        CssProperty.MarginTop,
        CssProperty.MarginRight,
        CssProperty.MarginBottom,
        CssProperty.MarginLeft,
        CssProperty.PaddingTop,
        CssProperty.PaddingRight,
        CssProperty.PaddingBottom,
        CssProperty.PaddingLeft,
        CssProperty.BorderTopWidth,
        CssProperty.BorderRightWidth,
        CssProperty.BorderBottomWidth,
        CssProperty.BorderLeftWidth,
        CssProperty.FontWeight,
        CssProperty.ZIndex,
        CssProperty.Visibility,
        CssProperty.Display,
        CssProperty.TransformOrigin
    ];

    /// <summary>
    /// Starts and advances CSS transitions by comparing the current base style
    /// with the values observed on the previous frame.
    /// </summary>
    /// <param name="baseStyle">Unanimated style resolved for the current frame.</param>
    /// <param name="parent">Parent style threaded through to the interpolators.</param>
    /// <param name="transitions">Declared transition layers, or <see langword="null"/>.</param>
    /// <param name="state">Retained per-node state holding the previous base values.</param>
    /// <param name="now">Current monotonic time, in seconds.</param>
    /// <param name="animated">
    /// Animated style already created by keyframes for this frame, or
    /// <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <paramref name="animated"/>, or a style containing the interpolated
    /// transition values.
    /// </returns>
    /// <remarks>
    /// <para>
    /// On the first observation the current values are recorded as the
    /// baseline, matching the CSS rule that transitions do not run on the
    /// initial style. A later change starts a run from the previous value; a
    /// change during a run starts from the currently painted value so retargeting
    /// does not jump.
    /// </para>
    /// <para>
    /// Runs are evaluated with the same timing-function implementation as
    /// keyframes, so <c>cubic-bezier()</c> and the standard keywords behave
    /// identically in both systems. Finished runs are dropped once their final
    /// value has been applied.
    /// </para>
    /// </remarks>
    private ComputedStyle? ApplyTransitions(
        ComputedStyle baseStyle,
        ComputedStyle? parent,
        List<TransitionSpec>? transitions,
        NodeAnimationState state,
        double now,
        ComputedStyle? animated)
    {
        if (transitions is null || transitions.Count == 0)
        {
            return animated;
        }

        var baseValues = state.BaseValues ??= [];
        var runs = state.Runs ??= [];

        if (!ReferenceEquals(state.LastBaseStyle, baseStyle))
        {
            state.LastBaseStyle = baseStyle;

            if (HasAllTransition(transitions))
            {
                for (var i = 0; i < TransitionProperties.Length; i++)
                {
                    CaptureTransitionBase(transitions, baseValues, runs, baseStyle, animated, now, TransitionProperties[i]);
                }
            }
            else
            {
                for (var i = 0; i < transitions.Count; i++)
                {
                    var property = transitions[i].Property;

                    if (property is CssProperty.Unknown or CssProperty.All)
                    {
                        continue;
                    }

                    CaptureTransitionBase(transitions, baseValues, runs, baseStyle, animated, now, property);
                }
            }
        }

        if (runs.Count == 0)
        {
            return animated;
        }

        List<CssProperty>? finished = null;

        foreach (var pair in runs)
        {
            var property = pair.Key;
            var run = pair.Value;
            var spec = FindTransitionSpec(transitions, property);

            if (spec is null || spec.Property == CssProperty.Unknown)
            {
                (finished ??= []).Add(property);
                continue;
            }

            var elapsed = now - run.Start - run.Delay;
            var progress = run.Duration <= 0
                ? 1
                : Math.Clamp((float)(elapsed / run.Duration), 0, 1);
            var eased = ApplyTimingFunction(run.TimingFunction, progress);

            if (animated is null)
            {
                animated = state.Animated ??= baseStyle.CloneForAnimation();
                animated.CopyAllFrom(baseStyle);
            }

            ApplyAnimated(animated, parent, property, run.From, run.To, eased);

            if (progress < 1)
            {
                HasRunningAnimations = true;
            }
            else
            {
                (finished ??= []).Add(property);
            }
        }

        if (finished is not null)
        {
            for (var i = 0; i < finished.Count; i++)
            {
                runs.Remove(finished[i]);
            }
        }

        return animated;
    }

    /// <summary>Returns whether any transition layer targets every property.</summary>
    /// <param name="transitions">Declared transition layers.</param>
    /// <returns><see langword="true"/> when a layer uses <c>all</c>.</returns>
    private static bool HasAllTransition(List<TransitionSpec> transitions)
    {
        for (var i = 0; i < transitions.Count; i++)
        {
            if (transitions[i].Property == CssProperty.All)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records the current base value of one property and starts a transition
    /// when that value changed since the previous frame.
    /// </summary>
    /// <param name="transitions">Declared transition layers.</param>
    /// <param name="baseValues">Previous values keyed by property.</param>
    /// <param name="runs">Active transitions keyed by property.</param>
    /// <param name="baseStyle">Current unanimated style.</param>
    /// <param name="animated">Style already animated this frame, if any.</param>
    /// <param name="now">Current monotonic time, in seconds.</param>
    /// <param name="property">Property to capture.</param>
    /// <remarks>
    /// Properties without a textual base representation are skipped, as are
    /// layers configured with <c>none</c>. A zero duration or a missing layer
    /// cancels any run for the property because the base value should apply
    /// immediately.
    /// </remarks>
    private static void CaptureTransitionBase(
        List<TransitionSpec> transitions,
        Dictionary<CssProperty, string> baseValues,
        Dictionary<CssProperty, TransitionRun> runs,
        ComputedStyle baseStyle,
        ComputedStyle? animated,
        double now,
        CssProperty property)
    {
        var current = GetBaseValue(baseStyle, property);

        if (current is null)
        {
            return;
        }

        var spec = FindTransitionSpec(transitions, property);

        if (spec is null || spec.Property == CssProperty.Unknown)
        {
            runs.Remove(property);
            return;
        }

        if (spec.Duration <= 0)
        {
            runs.Remove(property);
            baseValues[property] = current;
            return;
        }

        if (baseValues.TryGetValue(property, out var previous)
            && !string.Equals(previous, current, StringComparison.Ordinal))
        {
            var from = previous;

            if (runs.ContainsKey(property) && animated is not null)
            {
                from = GetBaseValue(animated, property) ?? previous;
            }

            runs[property] = new TransitionRun
            {
                From = from,
                To = current,
                Start = now,
                Duration = spec.Duration,
                Delay = spec.Delay,
                TimingFunction = spec.TimingFunction
            };
        }

        baseValues[property] = current;
    }

    /// <summary>
    /// Finds the transition layer for a property, preferring an explicit entry
    /// over an <c>all</c> layer.
    /// </summary>
    /// <param name="transitions">Declared transition layers.</param>
    /// <param name="property">Property being animated.</param>
    /// <returns>The matching layer, or <see langword="null"/> when none applies.</returns>
    /// <remarks>
    /// When a property appears more than once, the last explicit entry wins,
    /// matching how later values of <c>transition-property</c> override earlier
    /// ones.
    /// </remarks>
    private static TransitionSpec? FindTransitionSpec(
        List<TransitionSpec> transitions,
        CssProperty property)
    {
        TransitionSpec? explicitMatch = null;
        TransitionSpec? allMatch = null;

        for (var i = 0; i < transitions.Count; i++)
        {
            var spec = transitions[i];

            if (spec.Property == property)
            {
                explicitMatch = spec;
            }
            else if (spec.Property == CssProperty.All)
            {
                allMatch = spec;
            }
        }

        return explicitMatch ?? allMatch;
    }

    // ------------------------------------------------------------- keyframes

    /// <summary>
    /// Gets the cached keyframe track for an animation name, parsing it on first
    /// use.
    /// </summary>
    /// <param name="name">Animation name taken from an <see cref="AnimationSpec"/>.</param>
    /// <returns>
    /// The track, or <see langword="null"/> when the name is not interned or no
    /// <c>@keyframes</c> rule uses it.
    /// </returns>
    /// <remarks>
    /// Both hits and misses are cached, so a miss costs one table scan per
    /// runtime rather than one per frame. The cache is never invalidated, so
    /// changing the rule table after construction requires a new runtime.
    /// </remarks>
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

    /// <summary>
    /// Builds a track by grouping every declaration of one keyframe name into
    /// offset buckets.
    /// </summary>
    /// <param name="nameId">Interned id of the animation name to collect.</param>
    /// <returns>
    /// The ordered track, or <see langword="null"/> when no usable declaration
    /// exists.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Rules are read in table order. Each keyframe selector can list several
    /// offsets (<c>from, 50%, to</c>), and one declaration is emitted for every
    /// offset so the same property animates at each of them. A keyframe may
    /// repeat an existing offset across rules; <see cref="Deduplicate"/> keeps
    /// the last value seen for each property.
    /// </para>
    /// <para>
    /// Offsets 0 and 1 are always inserted, with empty declaration lists when
    /// the author did not provide them, so the timeline is anchored at both
    /// ends. Empty stops only contribute base values to adjacent segments.
    /// </para>
    /// <para>
    /// Declarations with an unknown property or an unresolvable value id are
    /// skipped. This method runs once per animation name and is not on the
    /// per-frame path.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Reduces the declarations at one offset to a single value per property.
    /// </summary>
    /// <param name="declarations">Declarations collected in table order; may be empty.</param>
    /// <returns>One declaration per property, in unspecified order.</returns>
    /// <remarks>
    /// Later declarations overwrite earlier ones, matching the cascade order of
    /// repeated rules at the same keyframe offset. Callers must not rely on the
    /// element order of the result, because it follows dictionary enumeration.
    /// </remarks>
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

    /// <summary>
    /// Parses the selector list of one <c>@keyframes</c> step into normalised
    /// offsets.
    /// </summary>
    /// <param name="keyId">Selector id whose interned tokens spell the offset list.</param>
    /// <param name="offsets">
    /// List that receives each offset in the range 0 to 1, in source order.
    /// </param>
    /// <remarks>
    /// <para>
    /// The selector tokens are concatenated without separators, then split on
    /// commas. Accepted forms are the <c>from</c> and <c>to</c> keywords,
    /// percentages such as <c>50%</c>, and unitless numbers. Percentages and
    /// numbers are clamped to 0 to 1, so out-of-range stops are pulled onto the
    /// endpoints.
    /// </para>
    /// <para>
    /// Unrecognised selector parts are ignored rather than rejected, which lets
    /// a malformed keyframe still contribute its valid offsets.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Resolves an animation's position on its timeline and reports whether it
    /// should be applied this frame.
    /// </summary>
    /// <param name="spec">Animation specification whose timing fields drive the evaluation.</param>
    /// <param name="start">Clock time at which the animation was created, in seconds.</param>
    /// <param name="now">Current clock time, in seconds.</param>
    /// <param name="progress">
    /// Receives the eased progress in the range 0 to 1, after direction and
    /// timing-function handling.
    /// </param>
    /// <param name="running">
    /// Receives <see langword="true"/> while the animation is still advancing,
    /// including during a pending delay.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a style should be applied, which includes a
    /// backwards fill during the delay and a forwards fill after the end.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Local time is <c>now - start - Delay</c>, so a negative delay starts the
    /// animation part-way through. The iteration index is
    /// <c>floor(local / Duration)</c> and the fraction is the remaining
    /// fractional part; both feed <see cref="DirectionalProgress"/>.
    /// </para>
    /// <para>
    /// A duration of zero or less, or an iteration count of exactly zero,
    /// disables the animation entirely, including fills. A negative iteration
    /// count means infinite because only a positive count is treated as a
    /// finite limit.
    /// </para>
    /// <para>
    /// Finite counts stop once the iteration index reaches the count. With
    /// <see cref="AnimationFillMode.Forwards"/> or
    /// <see cref="AnimationFillMode.Both"/> the held value is the end of the
    /// last whole iteration. Fractional counts therefore hold at a whole
    /// iteration boundary rather than partway through the final iteration.
    /// </para>
    /// <para>
    /// <see cref="AnimationSpec.PlayState"/> is not inspected; paused
    /// animations continue to advance unless the caller stops updating the
    /// clock or removes the animation.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Converts an iteration fraction into eased progress, honouring the
    /// animation direction.
    /// </summary>
    /// <param name="spec">Specification supplying the direction and timing function.</param>
    /// <param name="iteration">Zero-based iteration index used by alternating directions.</param>
    /// <param name="fraction">
    /// Raw fraction through the current iteration, in the range 0 to 1.
    /// </param>
    /// <returns>Eased progress in the range 0 to 1.</returns>
    /// <remarks>
    /// <see cref="AnimationDirection.Reverse"/> mirrors every iteration, while
    /// <see cref="AnimationDirection.Alternate"/> mirrors odd iterations and
    /// <see cref="AnimationDirection.AlternateReverse"/> mirrors even ones. The
    /// timing function is applied after mirroring, so an alternating
    /// <c>ease-in</c> still accelerates from the visual start of each
    /// iteration.
    /// </remarks>
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

    /// <summary>
    /// Maps linear progress through an iteration to eased progress using a CSS
    /// timing function.
    /// </summary>
    /// <param name="timing">
    /// Timing-function text, for example <c>linear</c> or <c>ease-in-out</c>.
    /// </param>
    /// <param name="t">Linear progress, clamped to 0 to 1 before evaluation.</param>
    /// <returns>Eased progress in the range 0 to 1.</returns>
    /// <remarks>
    /// <para>
    /// The keywords <c>linear</c>, <c>ease</c>, <c>ease-in</c>,
    /// <c>ease-out</c> and <c>ease-in-out</c> use the standard CSS control
    /// points. <c>cubic-bezier(...)</c> is evaluated with
    /// <see cref="CubicBezier(float, float, float, float, float)"/> and
    /// <c>steps(...)</c> with the jump-start or jump-end rule, defaulting to
    /// jump-end.
    /// </para>
    /// <para>
    /// Unrecognised text falls back to linear so a malformed value still
    /// animates. <c>step-start</c> currently resolves to 1 for every input
    /// rather than jumping at the start of the iteration.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Reads up to <paramref name="max"/> comma-separated numbers from the
    /// argument list of a function call.
    /// </summary>
    /// <param name="text">Function text such as <c>cubic-bezier(0.1, 0, 0.2, 1)</c>.</param>
    /// <param name="max">
    /// Maximum number of arguments to read; extra arguments are ignored and
    /// fewer are accepted.
    /// </param>
    /// <returns>
    /// The parsed values, or <see langword="null"/> when parentheses are
    /// missing or any read argument is not a number.
    /// </returns>
    /// <remarks>
    /// Parsing stops bounding the array at <paramref name="max"/>, but a
    /// malformed argument within that window rejects the whole list. The
    /// method allocates a small array on each call and is used only while
    /// evaluating animations.
    /// </remarks>
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

    /// <summary>
    /// Evaluates a cubic Bezier timing curve at a given x coordinate.
    /// </summary>
    /// <param name="x1">First control point x, normally in the range 0 to 1.</param>
    /// <param name="y1">First control point y.</param>
    /// <param name="x2">Second control point x, normally in the range 0 to 1.</param>
    /// <param name="y2">Second control point y.</param>
    /// <param name="x">Input progress in the range 0 to 1.</param>
    /// <returns>The curve's y value for <paramref name="x"/>.</returns>
    /// <remarks>
    /// The curve has implicit endpoints at (0, 0) and (1, 1). The parameter is
    /// found with eight Newton-Raphson steps from <paramref name="x"/> as the
    /// initial guess; each step is clamped to the unit interval to keep the
    /// iteration stable for overshooting control points. Values at or beyond
    /// the endpoints short-circuit to 0 and 1.
    /// </remarks>
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

    /// <summary>Evaluates one axis of a cubic Bezier with implicit 0 and 1 endpoints.</summary>
    /// <param name="t">Curve parameter in the range 0 to 1.</param>
    /// <param name="p1">First control point on this axis.</param>
    /// <param name="p2">Second control point on this axis.</param>
    /// <returns>The coordinate at <paramref name="t"/>.</returns>
    private static float Bezier(float t, float p1, float p2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * t * p1 + 3 * inverse * t * t * p2 + t * t * t;
    }

    /// <summary>
    /// Gets the derivative of <see cref="Bezier"/> with respect to its parameter.
    /// </summary>
    /// <param name="t">Curve parameter in the range 0 to 1.</param>
    /// <param name="p1">First control point on this axis.</param>
    /// <param name="p2">Second control point on this axis.</param>
    /// <returns>The slope at <paramref name="t"/>.</returns>
    private static float BezierDerivative(float t, float p1, float p2)
    {
        var inverse = 1 - t;
        return 3 * inverse * inverse * p1 + 6 * inverse * t * (p2 - p1) + 3 * t * t * (1 - p2);
    }

    // ----------------------------------------------------------- application

    /// <summary>
    /// Writes the interpolated values of one keyframe segment into an animated
    /// style.
    /// </summary>
    /// <param name="style">Animated style being mutated in place.</param>
    /// <param name="baseStyle">
    /// Unanimated style used for properties absent from the segment's start.
    /// </param>
    /// <param name="parent">Parent style passed through to <see cref="ApplyAnimated"/>.</param>
    /// <param name="track">Track whose stops define the segment.</param>
    /// <param name="progress">Eased progress in the range 0 to 1.</param>
    /// <remarks>
    /// <para>
    /// Properties declared by the segment's start stop are interpolated towards
    /// the value at the end stop, or towards the base style's value when the
    /// end stop omits them. Properties declared only at the end stop are
    /// interpolated from the base value, which models the implicit start
    /// keyframe.
    /// </para>
    /// <para>
    /// When neither stop nor the base style supplies a usable value, the
    /// declaration itself is used for both ends, so the property holds that
    /// value for the segment. Properties with no interpolator are applied
    /// discretely by <see cref="ApplyAnimated"/>.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Locates the keyframe segment containing a given progress value.
    /// </summary>
    /// <param name="track">Track to search; its stops are sorted by offset.</param>
    /// <param name="progress">Eased progress in the range 0 to 1.</param>
    /// <param name="from">Receives the stop starting the segment.</param>
    /// <param name="to">
    /// Receives the stop ending the segment; equal to <paramref name="from"/> at
    /// the ends.
    /// </param>
    /// <param name="t">Receives the position within the segment in the range 0 to 1.</param>
    /// <returns>
    /// <see langword="true"/> when a segment was found. A track always has at
    /// least one stop, so the current implementation never returns
    /// <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Progress before the first stop or after the last stop collapses the
    /// segment to that single stop with <paramref name="t"/> equal to zero, so
    /// the edge keyframe's values apply directly. A zero-width segment (two
    /// stops at the same offset) reports <paramref name="t"/> as zero instead of
    /// dividing by zero. The lookup is a linear scan over the stops.
    /// </remarks>
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

    /// <summary>Finds the value declared for a property at one keyframe stop.</summary>
    /// <param name="stop">Stop to search.</param>
    /// <param name="property">Property to look for.</param>
    /// <returns>
    /// The declared value, or <see langword="null"/> when the stop omits the
    /// property.
    /// </returns>
    /// <remarks>Stops hold few declarations, so this intentionally uses a linear scan.</remarks>
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

    /// <summary>
    /// Returns whether animating a property affects painting only rather than
    /// geometry.
    /// </summary>
    /// <param name="property">Property being animated.</param>
    /// <returns>
    /// <see langword="true"/> for opacity, transforms and paint effects such as
    /// colours, shadows, filters and clipping; otherwise <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// Paint-only animations let a host repaint with the previous layout, so
    /// <see cref="HasLayoutAnimations"/> stays clear. Properties not listed
    /// here are assumed to change layout, even when no interpolator exists.
    /// </remarks>
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

    /// <summary>
    /// Interpolates one property for one keyframe segment and writes it into the
    /// animated style.
    /// </summary>
    /// <param name="style">Animated style being mutated in place.</param>
    /// <param name="parent">Parent style; currently unused by the interpolators.</param>
    /// <param name="property">Property to interpolate.</param>
    /// <param name="from">Declaration text at the segment start.</param>
    /// <param name="to">Declaration text at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <remarks>
    /// <para>
    /// Values are cleaned of <c>!important</c> first. Empty values and values
    /// containing <c>var()</c> are skipped, because custom properties are not
    /// resolved in this pass. The layout flag is raised before those checks, so
    /// a skipped non-paint property still forces a full layout.
    /// </para>
    /// <para>
    /// Interpolation is property specific:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <description>
    /// <b>Numbers.</b> <c>opacity</c> is linearly interpolated and clamped to 0
    /// to 1; <c>font-weight</c> and <c>z-index</c> are rounded to integers, and
    /// <c>z-index</c> marks the style as having an explicit value.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Lengths.</b> Single lengths such as <c>width</c>, offsets,
    /// <c>font-size</c>, <c>line-height</c>, gaps and <c>outline-width</c>
    /// interpolate componentwise. Matching units interpolate numerically;
    /// mismatched units and intrinsic keywords switch discretely at the
    /// halfway point.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Four-sided lengths.</b> <c>margin</c>, <c>padding</c> and
    /// <c>border-width</c> accept the one-to-four value shorthand, which is
    /// expanded to top, right, bottom and left before componentwise
    /// interpolation.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Colours.</b> <c>color</c>, background, border and outline colours
    /// interpolate each 8-bit RGBA channel independently in sRGB space. The
    /// <c>border-color</c> shorthand writes the same result to all four edges.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Transform.</b> Operation lists must match in length and kind;
    /// otherwise the whole value switches at the halfway point. Rotations
    /// interpolate in degrees, translations and scales per axis, skew angles
    /// and matrices componentwise.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Transform origin.</b> The one- or two-value form is expanded and
    /// interpolated per axis.
    /// </description>
    /// </item>
    /// <item>
    /// <description>
    /// <b>Discrete properties.</b> <c>visibility</c> and <c>display</c> use the
    /// start value while <paramref name="t"/> is less than 0.5 and the end value
    /// afterwards. Any other property falls through without changing the style.
    /// </description>
    /// </item>
    /// </list>
    /// </remarks>
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

    /// <summary>Trims a declaration and removes a trailing <c>!important</c> flag.</summary>
    /// <param name="value">Raw declaration text.</param>
    /// <returns>The value text without the priority flag.</returns>
    /// <remarks>
    /// The search is case-insensitive and discards everything from the flag
    /// onwards. Keyframe declarations are already resolved, so the flag carries
    /// no meaning here and only interferes with value parsing.
    /// </remarks>
    private static string Clean(string value)
    {
        var important = value.IndexOf("!important", StringComparison.OrdinalIgnoreCase);

        if (important >= 0)
        {
            value = value[..important];
        }

        return value.Trim();
    }

    /// <summary>Parses a unitless numeric value after removing the priority flag.</summary>
    /// <param name="value">Declaration text such as <c>0.5</c>.</param>
    /// <returns>The parsed number, or the parser's fallback when the text is not numeric.</returns>
    private static float ParseNumber(string value)
    {
        return CssValueParser.ParseNumber(Clean(value));
    }

    /// <summary>Linearly interpolates between two numbers.</summary>
    /// <param name="from">Value at <paramref name="t"/> equal to zero.</param>
    /// <param name="to">Value at <paramref name="t"/> equal to one.</param>
    /// <param name="t">Interpolation position, normally in the range 0 to 1.</param>
    /// <returns><c>from + (to - from) * t</c>.</returns>
    private static float Lerp(float from, float to, float t)
    {
        return from + (to - from) * t;
    }

    /// <summary>Parses both declarations and interpolates them as CSS lengths.</summary>
    /// <param name="from">Declaration text at the segment start.</param>
    /// <param name="to">Declaration text at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <param name="fallback">Length forwarded to the parsed-length overload.</param>
    /// <returns>The interpolated length.</returns>
    /// <remarks>
    /// Both declarations are cleaned and parsed before the overload is called.
    /// The parsed-length overload currently ignores <paramref name="fallback"/>.
    /// </remarks>
    private static CssLength LerpLength(string from, string to, float t, CssLength fallback)
    {
        return LerpLength(CssValueParser.ParseLength(Clean(from)), CssValueParser.ParseLength(Clean(to)), t, fallback);
    }

    /// <summary>Interpolates two parsed lengths, falling back to a discrete switch.</summary>
    /// <param name="from">Length at the segment start.</param>
    /// <param name="to">Length at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <param name="fallback">Unused; accepted so both length overloads share a signature.</param>
    /// <returns>The interpolated length, or the nearer operand when discrete.</returns>
    /// <remarks>
    /// <para>
    /// Only operands with the same unit in the interpolable set are combined
    /// numerically. Mismatched units, and the keyword units <c>auto</c>,
    /// <c>min-content</c>, <c>max-content</c>, <c>fit-content</c>,
    /// <c>stretch</c> and <c>fr</c>, switch to <paramref name="from"/> before
    /// the halfway point and to <paramref name="to"/> afterwards. The
    /// <paramref name="fallback"/> argument is accepted for symmetry but is not
    /// used by this overload.
    /// </para>
    /// <para>
    /// Interpolation preserves the unit. Units outside the set understood by
    /// <see cref="MakeLength"/> are reconstructed as pixels, so a pair of
    /// viewport units loses its unit even though the numeric value is lerped.
    /// </para>
    /// </remarks>
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

    /// <summary>Reconstructs a length from a unit and a numeric value.</summary>
    /// <param name="unit">Unit to construct.</param>
    /// <param name="value">Numeric value in that unit.</param>
    /// <returns>A length in <paramref name="unit"/>; unsupported units become pixels.</returns>
    /// <remarks>
    /// Only <c>px</c>, <c>%</c>, <c>em</c>, <c>rem</c> and unitless numbers are
    /// reconstructed faithfully. Every other unit, including viewport and
    /// absolute units, falls back to <c>px</c> with the same numeric value.
    /// </remarks>
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

    /// <summary>Interpolates two four-sided shorthand declarations componentwise.</summary>
    /// <param name="from">Shorthand text at the segment start.</param>
    /// <param name="to">Shorthand text at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <param name="fallback">Edges used when a declaration has no parsable values.</param>
    /// <returns>A four-sided length with independently interpolated edges.</returns>
    /// <remarks>
    /// Each side follows <see cref="LerpLength(CssLength, CssLength, float, CssLength)"/>,
    /// so an edge whose units do not match switches discretely while its
    /// neighbours keep interpolating.
    /// </remarks>
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

    /// <summary>Expands a one-to-four value CSS shorthand into four edges.</summary>
    /// <param name="value">Shorthand text such as <c>1px 2px</c>.</param>
    /// <param name="fallback">Edges returned when the text contains no parsable values.</param>
    /// <returns>Top, right, bottom and left values in CSS shorthand order.</returns>
    /// <remarks>
    /// One value applies to all edges, two values alternate vertical and
    /// horizontal, three values set top, horizontal and bottom, and four values
    /// map directly to top, right, bottom and left.
    /// </remarks>
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

    /// <summary>Splits whitespace-separated values and parses each as a length.</summary>
    /// <param name="value">Text to split, normally a shorthand declaration.</param>
    /// <returns>The parsed lengths in source order; unparsable parts become auto lengths.</returns>
    /// <remarks>
    /// Splitting is purely on whitespace, so functional values such as
    /// <c>calc(1px + 2px)</c> are broken into several parts and are not
    /// interpreted as a single length by the animation interpolators.
    /// </remarks>
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

    /// <summary>Interpolates two colours channel by channel in sRGB space.</summary>
    /// <param name="from">Colour text at the segment start.</param>
    /// <param name="to">Colour text at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <param name="fallback">Colour returned when either operand fails to parse.</param>
    /// <returns>The packed RGBA result.</returns>
    /// <remarks>
    /// Red, green, blue and alpha are each linearly interpolated and rounded
    /// down by the byte cast after clamping. Interpolation is straight,
    /// non-premultiplied sRGB, so semi-transparent colours can pass through
    /// unexpected intermediate hues.
    /// </remarks>
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

    /// <summary>Parses a <c>visibility</c> keyword.</summary>
    /// <param name="value">Keyword text, matched case-insensitively.</param>
    /// <param name="fallback">Value used for an unrecognised keyword.</param>
    /// <returns>The matching <see cref="Visibility"/> value.</returns>
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

    /// <summary>Parses a <c>display</c> keyword.</summary>
    /// <param name="value">Keyword text, matched case-insensitively.</param>
    /// <param name="fallback">Value used for an unrecognised keyword.</param>
    /// <returns>The matching <see cref="Display"/> value.</returns>
    /// <remarks>
    /// Only the outer display keywords listed in the switch are recognized;
    /// newer forms such as <c>inline flow-root</c> or <c>block flex</c> keep
    /// the fallback value.
    /// </remarks>
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

    /// <summary>
    /// Formats one computed property back into CSS text so it can act as the
    /// implicit endpoint of a keyframe segment.
    /// </summary>
    /// <param name="style">Computed style to read; normally the unanimated base style.</param>
    /// <param name="property">Property to format.</param>
    /// <returns>
    /// Declaration text the parsers in this file understand, or
    /// <see langword="null"/> when no base representation is defined for the
    /// property.
    /// </returns>
    /// <remarks>
    /// This exists so a keyframe that declares a property at only one offset can
    /// still interpolate from the element's underlying value. Lengths and colours
    /// are rendered lossily but round-trip through the same parsers used for
    /// declarations.
    /// </remarks>
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
            CssProperty.Margin => FormatLength4(style.Margin),
            CssProperty.Padding => FormatLength4(style.Padding),
            CssProperty.BorderWidth => FormatLength4(style.BorderWidth),
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

    /// <summary>Formats a packed colour as <c>#RRGGBBAA</c>.</summary>
    /// <param name="color">Packed RGBA colour.</param>
    /// <returns>An eight-digit hexadecimal colour string.</returns>
    private static string FormatColor(uint color)
    {
        return $"#{CssColor.Red(color):X2}{CssColor.Green(color):X2}{CssColor.Blue(color):X2}{CssColor.Alpha(color):X2}";
    }

    /// <summary>Formats four sides as a one-to-four value CSS shorthand.</summary>
    /// <param name="value">Edges to serialise.</param>
    /// <returns>Text such as <c>1px 2px 3px 4px</c>.</returns>
    /// <remarks>
    /// All four values are always emitted, even when they are equal, because
    /// the animation parser accepts that form and keeping the output uniform
    /// makes transition snapshots easy to compare.
    /// </remarks>
    private static string FormatLength4(CssLength4 value)
    {
        return value.Top + " " + value.Right + " " + value.Bottom + " " + value.Left;
    }

    /// <summary>Serialises a parsed transform list back to CSS transform text.</summary>
    /// <param name="spec">Transform to serialise.</param>
    /// <returns>
    /// Space-separated function calls in their original order, or <c>none</c>
    /// when the list is empty.
    /// </returns>
    /// <remarks>
    /// Angles use up to three decimals and matrix components up to six, all with
    /// invariant culture, so the text is suitable as input to
    /// <see cref="StyleResolver.ParseTransform"/>. A translation with an
    /// automatic y component repeats the x value, matching its used meaning.
    /// </remarks>
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

    /// <summary>Interpolates two transform lists operation by operation.</summary>
    /// <param name="from">
    /// Transform at the segment start, or <see langword="null"/> for <c>none</c>.
    /// </param>
    /// <param name="to">
    /// Transform at the segment end, or <see langword="null"/> for <c>none</c>.
    /// </param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <returns>
    /// The interpolated transform, or <see langword="null"/> when both operands
    /// are absent.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A missing operand is replaced by the identity form of the other list (see
    /// <see cref="IdentityFor"/>), so animating to or from <c>none</c> works.
    /// Interpolation is positional: lists must have the same operation count and
    /// the same kind at each index. If either check fails the value switches
    /// discretely at the halfway point rather than being decomposed and
    /// recomposed.
    /// </para>
    /// <para>
    /// Rotations interpolate their angle, translations and scales interpolate
    /// each axis as a length, skew interpolates both angles, and matrices
    /// interpolate all six entries. Translation and scale axes switch discretely
    /// when their units do not match.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Builds the identity transform with the same operation kinds as a given
    /// list.
    /// </summary>
    /// <param name="spec">Transform whose operation structure is mirrored.</param>
    /// <returns>A transform where every operation is the identity for its kind.</returns>
    /// <remarks>
    /// Rotation and skew angles become zero, translations become zero pixels,
    /// scales become one, and matrices become
    /// <see cref="Matrix3x2.Identity"/>. This is the implicit endpoint used when
    /// only one side of a segment declares a transform.
    /// </remarks>
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

    /// <summary>Interpolates every component of two affine matrices.</summary>
    /// <param name="from">Matrix at the segment start.</param>
    /// <param name="to">Matrix at the segment end.</param>
    /// <param name="t">Segment position in the range 0 to 1.</param>
    /// <returns>The componentwise interpolation of the six entries.</returns>
    /// <remarks>
    /// Componentwise interpolation is not a true matrix decomposition, so
    /// rotations expressed as matrices shrink through the segment rather than
    /// rotating along an arc.
    /// </remarks>
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

    /// <summary>
    /// Hashes the fields that define an animation's timeline so the runtime can
    /// detect a changed declaration.
    /// </summary>
    /// <param name="animations">Animation list attached to a computed style.</param>
    /// <param name="transitions">Transition list attached to a computed style.</param>
    /// <returns>A hash combining every timing field of every spec, in list order.</returns>
    /// <remarks>
    /// The name, duration, delay, iteration count, direction, fill mode, play
    /// state and timing function participate; the list order and length are
    /// implied by how the hash is accumulated. Transition layers contribute
    /// their property, duration, delay and timing function. A changed signature
    /// restarts the node's animation and its baseline, which is why a transition
    /// edits does not animate from a stale value. Hash collisions are
    /// theoretically possible, in which case a changed declaration may not
    /// restart, so this is a change detector rather than an identity.
    /// </remarks>
    private static int Signature(List<AnimationSpec>? animations, List<TransitionSpec>? transitions)
    {
        var hash = new HashCode();

        if (animations is not null)
        {
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
        }

        if (transitions is not null)
        {
            for (var i = 0; i < transitions.Count; i++)
            {
                var spec = transitions[i];
                hash.Add((int)spec.Property);
                hash.Add(spec.Duration);
                hash.Add(spec.Delay);
                hash.Add(spec.TimingFunction);
            }
        }

        return hash.ToHashCode();
    }

    /// <summary>
    /// Retained animation state for one node: the declaration signature that
    /// created it, its start time and its cached animated style.
    /// </summary>
    /// <remarks>
    /// Instances live in <see cref="AnimationRuntime._states"/> and are removed
    /// by <see cref="Prune"/> once their node is no longer laid out. They are
    /// mutable and owned exclusively by the runtime.
    /// </remarks>
    private sealed class NodeAnimationState
    {
        /// <summary>Hash of the animation declarations this state was created for.</summary>
        public int Signature;
        /// <summary>Clock time at which the state was created, in seconds.</summary>
        /// <remarks>
        /// All animations on the node share this origin, so their individual
        /// delays are applied on top of it.
        /// </remarks>
        public double StartTime;
        /// <summary>
        /// Last frame number that touched this state. <see cref="Prune"/> removes
        /// the state when it does not match the current frame.
        /// </summary>
        public int Frame;

        /// <summary>
        /// The animated style is reused between frames so descendants keep a
        /// stable parent identity and their cached styles stay valid.
        /// </summary>
        /// <remarks>
        /// Created lazily with <see cref="ComputedStyle.CloneForAnimation"/> the
        /// first time an animation becomes active, then refreshed in place each
        /// frame. Never <see langword="null"/> once animation has applied.
        /// </remarks>
        public ComputedStyle? Animated;

        /// <summary>
        /// Last observed base value for each transitioned property, formatted as
        /// CSS text. Used to detect a style change on the following frame.
        /// </summary>
        public Dictionary<CssProperty, string>? BaseValues;

        /// <summary>Active transitions keyed by property.</summary>
        public Dictionary<CssProperty, TransitionRun>? Runs;

        /// <summary>
        /// Base style instance observed on the previous frame. Transitions only
        /// need to rescan values when this changes, which keeps
        /// <c>transition: all</c> cheap while a run is in flight.
        /// </summary>
        public ComputedStyle? LastBaseStyle;
    }

    /// <summary>One in-flight CSS transition for a single property.</summary>
    /// <remarks>
    /// Created by <see cref="CaptureTransitionBase"/> when a base value changes
    /// and removed once the run reaches its end.
    /// </remarks>
    private sealed class TransitionRun
    {
        /// <summary>Value at the start of the run, as CSS text.</summary>
        public string From = string.Empty;

        /// <summary>Target value, as CSS text.</summary>
        public string To = string.Empty;

        /// <summary>Clock time at which the run was created, in seconds.</summary>
        public double Start;

        /// <summary>Duration of the run, in seconds.</summary>
        public float Duration;

        /// <summary>Delay before the run starts, in seconds.</summary>
        public float Delay;

        /// <summary>Timing function text used for the run.</summary>
        public string TimingFunction = "ease";
    }

    /// <summary>An ordered set of keyframe stops for one animation name.</summary>
    /// <remarks>
    /// Stops are sorted by offset and immutable once built. Tracks are cached by
    /// interned name in <see cref="AnimationRuntime._tracks"/>.
    /// </remarks>
    private sealed class KeyframeTrack
    {
        /// <summary>Creates a track from already sorted stops.</summary>
        /// <param name="stops">Stops in ascending offset order; must not be empty.</param>
        public KeyframeTrack(KeyframeStop[] stops)
        {
            Stops = stops;
        }

        /// <summary>Gets the stops in ascending offset order.</summary>
        public KeyframeStop[] Stops { get; }
    }

    /// <summary>One keyframe offset and the declarations attached to it.</summary>
    private sealed class KeyframeStop
    {
        /// <summary>Creates a stop.</summary>
        /// <param name="offset">Normalised offset in the range 0 to 1.</param>
        /// <param name="declarations">
        /// Deduplicated declarations at this offset; may be empty.
        /// </param>
        public KeyframeStop(float offset, KeyframeDeclaration[] declarations)
        {
            Offset = offset;
            Declarations = declarations;
        }

        /// <summary>Gets the normalised offset, where 0 is the start and 1 the end.</summary>
        public float Offset { get; }

        /// <summary>Gets the declarations at this offset, at most one per property.</summary>
        public KeyframeDeclaration[] Declarations { get; }
    }

    /// <summary>A single property/value pair declared at one keyframe offset.</summary>
    /// <param name="Property">Property the declaration targets.</param>
    /// <param name="Value">Raw declaration text, still including units.</param>
    private readonly record struct KeyframeDeclaration(CssProperty Property, string Value);
}
