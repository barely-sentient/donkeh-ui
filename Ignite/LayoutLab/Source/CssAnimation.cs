namespace Ignite.LayoutLab;

/// <summary>Direction of a CSS animation.</summary>
public enum AnimationDirection : byte
{
    Normal,
    Reverse,
    Alternate,
    AlternateReverse
}

/// <summary>What a CSS animation does outside its active interval.</summary>
public enum AnimationFillMode : byte
{
    None,
    Forwards,
    Backwards,
    Both
}

/// <summary>Whether a CSS animation is running.</summary>
public enum AnimationPlayState : byte
{
    Running,
    Paused
}

/// <summary>
/// One entry of the CSS <c>animation</c> shorthand / longhands. Times are in
/// seconds. An iteration count of -1 means <c>infinite</c>.
/// </summary>
public sealed class AnimationSpec
{
    public string Name { get; set; } = "none";

    public float Duration { get; set; }

    public float Delay { get; set; }

    public float IterationCount { get; set; } = 1;

    public AnimationDirection Direction { get; set; } = AnimationDirection.Normal;

    public AnimationFillMode FillMode { get; set; } = AnimationFillMode.None;

    public AnimationPlayState PlayState { get; set; } = AnimationPlayState.Running;

    /// <summary>Raw timing function text, for example <c>linear</c> or <c>cubic-bezier(...)</c>.</summary>
    public string TimingFunction { get; set; } = "ease";

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
