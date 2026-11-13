namespace Donkeh.LayoutLab;

/// <summary>Direction of a CSS animation.</summary>
/// <remarks>
/// Maps to the <c>animation-direction</c> values and controls whether
/// iterations run forwards, backwards or alternate.
/// </remarks>
public enum AnimationDirection : byte
{
    /// <summary><c>animation-direction: normal</c>; every iteration runs forwards.</summary>
    Normal,
    /// <summary><c>animation-direction: reverse</c>; every iteration runs backwards.</summary>
    Reverse,
    /// <summary><c>animation-direction: alternate</c>; odd iterations run backwards.</summary>
    Alternate,
    /// <summary><c>animation-direction: alternate-reverse</c>; odd iterations run forwards.</summary>
    AlternateReverse
}

/// <summary>What a CSS animation does outside its active interval.</summary>
/// <remarks>Maps to the <c>animation-fill-mode</c> values.</remarks>
public enum AnimationFillMode : byte
{
    /// <summary>
    /// <c>animation-fill-mode: none</c>; no styles are applied before or after
    /// the active interval.
    /// </summary>
    None,
    /// <summary>
    /// <c>animation-fill-mode: forwards</c>; the final keyframe persists after
    /// the animation ends.
    /// </summary>
    Forwards,
    /// <summary><c>animation-fill-mode: backwards</c>; the first keyframe applies during any delay.</summary>
    Backwards,
    /// <summary><c>animation-fill-mode: both</c>; both the backwards and forwards behaviours apply.</summary>
    Both
}

/// <summary>Whether a CSS animation is running.</summary>
/// <remarks>Maps to the <c>animation-play-state</c> values.</remarks>
public enum AnimationPlayState : byte
{
    /// <summary><c>animation-play-state: running</c>; the timeline advances.</summary>
    Running,
    /// <summary>
    /// <c>animation-play-state: paused</c>; the timeline is frozen at its
    /// current position.
    /// </summary>
    Paused
}

/// <summary>
/// One entry of the CSS <c>animation</c> shorthand / longhands. Times are in
/// seconds. An iteration count of -1 means <c>infinite</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a mutable reference type because the style resolver fills it in
/// while parsing the shorthand. Computed styles clone each spec when they are
/// copied, so per-element mutation does not leak across elements. Code that
/// receives a spec from shared state should treat it as read-only or call
/// <see cref="Clone"/> before modifying it.
/// </para>
/// <para>
/// Delay may be negative, which starts the animation part-way through its
/// first iteration. Duration is expected to be positive; a zero or negative
/// duration causes the runtime to skip the animation entirely rather than
/// applying its end state.
/// </para>
/// </remarks>
/// <seealso cref="AnimationDirection"/>
/// <seealso cref="AnimationFillMode"/>
/// <seealso cref="AnimationPlayState"/>
public sealed class AnimationSpec
{
    /// <summary>Gets or sets the keyframes name, defaulting to <c>none</c>.</summary>
    /// <value>The <c>@keyframes</c> identifier, or <c>none</c> when the animation is disabled.</value>
    public string Name { get; set; } = "none";

    /// <summary>Gets or sets the duration of one iteration, in seconds.</summary>
    /// <value>
    /// A positive duration. Zero or negative disables evaluation of the
    /// animation.
    /// </value>
    public float Duration { get; set; }

    /// <summary>Gets or sets the delay before the animation starts, in seconds.</summary>
    /// <value>A signed delay; negative values start the animation already in progress.</value>
    public float Delay { get; set; }

    /// <summary>Gets or sets how many times the animation repeats.</summary>
    /// <value>
    /// A positive count, or -1 for <c>infinite</c>. Fractional values are
    /// accepted; the runtime stops after the iteration containing the
    /// fractional boundary. Defaults to 1.
    /// </value>
    public float IterationCount { get; set; } = 1;

    /// <summary>Gets or sets the playback direction.</summary>
    /// <value>
    /// One of the <see cref="AnimationDirection"/> values; defaults to
    /// <see cref="AnimationDirection.Normal"/>.
    /// </value>
    public AnimationDirection Direction { get; set; } = AnimationDirection.Normal;

    /// <summary>Gets or sets the fill mode applied outside the active interval.</summary>
    /// <value>
    /// One of the <see cref="AnimationFillMode"/> values; defaults to
    /// <see cref="AnimationFillMode.None"/>.
    /// </value>
    public AnimationFillMode FillMode { get; set; } = AnimationFillMode.None;

    /// <summary>Gets or sets whether the animation is running or paused.</summary>
    /// <value>
    /// One of the <see cref="AnimationPlayState"/> values; defaults to
    /// <see cref="AnimationPlayState.Running"/>.
    /// </value>
    public AnimationPlayState PlayState { get; set; } = AnimationPlayState.Running;

    /// <summary>Raw timing function text, for example <c>linear</c> or <c>cubic-bezier(...)</c>.</summary>
    /// <remarks>
    /// The string is kept unparsed so the runtime can evaluate it per frame and
    /// so unresolved values can round-trip. Defaults to <c>ease</c>.
    /// </remarks>
    public string TimingFunction { get; set; } = "ease";

    /// <summary>Creates an independent copy of this specification.</summary>
    /// <returns>A new <see cref="AnimationSpec"/> with the same values.</returns>
    /// <remarks>
    /// The copy is shallow: <see cref="Name"/> and <see cref="TimingFunction"/>
    /// are immutable strings, so sharing them is safe. Mutating the returned
    /// instance does not affect this one.
    /// </remarks>
    public AnimationSpec Clone()
    {
        return new AnimationSpec
        {
            Name = Name,
            Duration = Duration,
            Delay = Delay,
            IterationCount = IterationCount,
            Direction = Direction,
            FillMode = FillMode,
            PlayState = PlayState,
            TimingFunction = TimingFunction
        };
    }
}

/// <summary>
/// One entry of the CSS <c>transition</c> shorthand / longhands. Times are in
/// seconds.
/// </summary>
/// <remarks>
/// <para>
/// This is a mutable reference type because the style resolver fills it in
/// while parsing the shorthand. Computed styles clone each spec when they are
/// copied, so per-element mutation does not leak across elements. Code that
/// receives a spec from shared state should treat it as read-only or call
/// <see cref="Clone"/> before modifying it.
/// </para>
/// <para>
/// A property of <see cref="CssProperty.All"/> matches every interpolable
/// property. <see cref="CssProperty.Unknown"/> represents <c>none</c>, which
/// disables the layer. The runtime observes the base style each frame and
/// starts a transition when a tracked property changes.
/// </para>
/// </remarks>
public sealed class TransitionSpec
{
    /// <summary>Gets or sets the property this layer animates.</summary>
    /// <value>
    /// A concrete <see cref="CssProperty"/>, <see cref="CssProperty.All"/> for
    /// every interpolable property, or <see cref="CssProperty.Unknown"/> for
    /// <c>none</c>.
    /// </value>
    public CssProperty Property { get; set; } = CssProperty.All;

    /// <summary>Gets or sets how long the transition runs, in seconds.</summary>
    /// <value>A non-negative duration; zero or less disables the transition.</value>
    public float Duration { get; set; }

    /// <summary>Gets or sets the delay before the transition starts, in seconds.</summary>
    /// <value>A signed delay; negative values start the transition part-way through.</value>
    public float Delay { get; set; }

    /// <summary>Raw timing function text, for example <c>linear</c> or <c>cubic-bezier(...)</c>.</summary>
    /// <remarks>
    /// The string is kept unparsed so the runtime can evaluate it per frame and
    /// so unresolved values can round-trip. Defaults to <c>ease</c>.
    /// </remarks>
    public string TimingFunction { get; set; } = "ease";

    /// <summary>Creates an independent copy of this specification.</summary>
    /// <returns>A new <see cref="TransitionSpec"/> with the same values.</returns>
    /// <remarks>
    /// The copy is shallow: <see cref="TimingFunction"/> is an immutable string,
    /// so sharing it is safe. Mutating the returned instance does not affect
    /// this one.
    /// </remarks>
    public TransitionSpec Clone()
    {
        return new TransitionSpec
        {
            Property = Property,
            Duration = Duration,
            Delay = Delay,
            TimingFunction = TimingFunction
        };
    }
}
