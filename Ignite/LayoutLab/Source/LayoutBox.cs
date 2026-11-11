using Ignite.Data.Dom;

namespace Ignite.LayoutLab;

/// <summary>
/// One laid-out box. Coordinates are absolute CSS pixels in the unscrolled
/// layout space; the display-list builder applies scroll translations while it
/// emits, so changing a scroll offset never invalidates the layout tree.
/// </summary>
public sealed class LayoutBox
{
    private float _intrinsicWidth;
    private bool _hasIntrinsicWidth;

    public LayoutBox(IgniteSupportedDomNode node, ComputedStyle style, int depth, int childCapacity = 0)
    {
        Node = node;
        Style = style;
        Depth = depth;
        Children = childCapacity > 0 ? new List<LayoutBox>(childCapacity) : [];
    }

    public IgniteSupportedDomNode Node { get; }

    public ComputedStyle Style { get; }

    public int Depth { get; }

    public LayoutBox? Parent { get; internal set; }

    public List<LayoutBox> Children { get; }

    /// <summary>Absolute border-box rect.</summary>
    public Rect BorderBox { get; internal set; }

    /// <summary>Absolute padding-box rect, inside the border.</summary>
    public Rect PaddingBox { get; internal set; }

    /// <summary>Absolute content-box rect, inside the padding.</summary>
    public Rect ContentBox { get; internal set; }

    /// <summary>Absolute margin-box rect.</summary>
    public Rect MarginBox { get; internal set; }

    public Thickness Margin { get; internal set; }
    public Thickness Border { get; internal set; }
    public Thickness Padding { get; internal set; }
    public CornerRadius Radius { get; internal set; }

    public bool IsContents { get; internal set; }

    /// <summary>True when this box is out of normal flow and laid out separately.</summary>
    public bool IsOutOfFlow => Style.Position is Position.Absolute or Position.Fixed;

    public bool IsFixed => Style.Position is Position.Fixed;

    /// <summary>The containing block for an absolutely positioned box, or null for the viewport.</summary>
    public LayoutBox? ContainingBlock { get; internal set; }

    /// <summary>Flow position used when an absolutely positioned box has no insets.</summary>
    public Point StaticPosition { get; internal set; }

    public bool IsScrollContainer { get; internal set; }
    public float ScrollOffsetX { get; internal set; }
    public float ScrollOffsetY { get; internal set; }
    public float ScrollWidth { get; internal set; }
    public float ScrollHeight { get; internal set; }
    public float ClientWidth => PaddingBox.Width;
    public float ClientHeight => PaddingBox.Height;

    /// <summary>
    /// Active floats for the block formatting context this box participates in.
    /// The storage is shared with ancestors until a box establishes a new BFC.
    /// </summary>
    internal FloatContext? FloatContext { get; set; }

    /// <summary>Laid-out text belonging directly to this element, if any.</summary>
    public TextLayout? Text { get; internal set; }

    public Point TextOrigin { get; internal set; }

    /// <summary>First-line baseline, relative to the border box top.</summary>
    public float Baseline { get; internal set; }

    public bool HasZIndex => Style.HasZIndex;

    public int EffectiveZIndex => Style.HasZIndex ? Style.ZIndex : 0;

    public bool CreatesStackingContext =>
        Style.HasZIndex
        || Style.Opacity < 0.9999f
        || Style.Transform is not null
        || Style.Position is Position.Fixed
        || Style.Filter is { IsNone: false }
        || Style.BackdropFilter is { IsNone: false }
        || Style.ClipPath is { IsNone: false }
        || Style.Mask is { IsNone: false }
        || Style.MixBlendMode != MixBlendMode.Normal
        || Style.Isolate;

    public bool IsPainted => !IsContents && Style.Display != Display.None;

    /// <summary>True when the box has a CSS animation applied on this frame.</summary>
    public bool IsAnimated => Style.IsAnimated;

    internal float IntrinsicWidth
    {
        get => _intrinsicWidth;
        set
        {
            _intrinsicWidth = value;
            _hasIntrinsicWidth = true;
        }
    }

    internal bool HasIntrinsicWidth => _hasIntrinsicWidth;

    /// <summary>Translates this box and its in-flow descendants.</summary>
    public void Translate(float dx, float dy)
    {
        if (dx == 0 && dy == 0)
        {
            return;
        }

        BorderBox = BorderBox.Translate(dx, dy);
        PaddingBox = PaddingBox.Translate(dx, dy);
        ContentBox = ContentBox.Translate(dx, dy);
        MarginBox = MarginBox.Translate(dx, dy);
        TextOrigin = TextOrigin.Translate(dx, dy);

        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];

            if (!child.IsOutOfFlow)
            {
                child.Translate(dx, dy);
            }
        }
    }

    internal void ApplyBorderBox(float x, float y, float width, float height)
    {
        width = MathF.Max(0, width);
        height = MathF.Max(0, height);

        BorderBox = new Rect(x, y, width, height);

        var paddingX = x + Border.Left;
        var paddingY = y + Border.Top;
        var paddingWidth = MathF.Max(0, width - Border.Left - Border.Right);
        var paddingHeight = MathF.Max(0, height - Border.Top - Border.Bottom);
        PaddingBox = new Rect(paddingX, paddingY, paddingWidth, paddingHeight);

        var contentX = paddingX + Padding.Left;
        var contentY = paddingY + Padding.Top;
        var contentWidth = MathF.Max(0, paddingWidth - Padding.Left - Padding.Right);
        var contentHeight = MathF.Max(0, paddingHeight - Padding.Top - Padding.Bottom);
        ContentBox = new Rect(contentX, contentY, contentWidth, contentHeight);

        MarginBox = new Rect(
            x - Margin.Left,
            y - Margin.Top,
            width + Margin.Left + Margin.Right,
            height + Margin.Top + Margin.Bottom);
    }

    public override string ToString()
    {
        return $"{Style.Display} {Node.TagName ?? "?"} {BorderBox}";
    }
}

/// <summary>The result of laying out one frame.</summary>
public sealed class LayoutResult
{
    private readonly Dictionary<IgniteSupportedDomNode, LayoutBox> _byNode;

    public LayoutResult(
        LayoutBox? root,
        InstructionBuffer instructions,
        ConstantPool constants,
        Dictionary<IgniteSupportedDomNode, LayoutBox> byNode,
        OptimisationStats stats,
        bool hasAnimations = false)
    {
        Root = root;
        Instructions = Snapshot(instructions);
        Constants = constants;
        _byNode = new Dictionary<IgniteSupportedDomNode, LayoutBox>(byNode);
        Stats = stats;
        HasAnimations = hasAnimations;
    }

    private static InstructionBuffer Snapshot(InstructionBuffer source)
    {
        var copy = new InstructionBuffer(Math.Max(4, source.Count));
        copy.AddRange(source.Span);
        return copy;
    }

    public LayoutBox? Root { get; }

    public InstructionBuffer Instructions { get; }

    public ConstantPool Constants { get; }

    public OptimisationStats Stats { get; }

    /// <summary>True when at least one box had a running CSS animation on this frame.</summary>
    public bool HasAnimations { get; }

    /// <summary>The node-to-box map, shared with the engine that built it.</summary>
    internal IReadOnlyDictionary<IgniteSupportedDomNode, LayoutBox> Boxes => _byNode;

    public ReadOnlySpan<Instruction> Opcodes => Instructions.Span;

    public bool TryGetBox(IgniteSupportedDomNode node, out LayoutBox box)
        => _byNode.TryGetValue(node, out box!);
}

public readonly record struct OptimisationStats(
    int InputInstructions,
    int OutputInstructions,
    int Culled,
    int Merged,
    int ScopesRemoved)
{
    public static readonly OptimisationStats Empty = new(0, 0, 0, 0, 0);

    public int Saved => Math.Max(0, InputInstructions - OutputInstructions);
}
