using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab;

/// <summary>
/// One laid-out box. Coordinates are absolute CSS pixels in the unscrolled
/// layout space; the display-list builder applies scroll translations while it
/// emits, so changing a scroll offset never invalidates the layout tree.
/// </summary>
/// <remarks>
/// <para>
/// A box exists for every generated element (and for the root); nodes with
/// <see cref="Display.None"/> produce no box. <see cref="Children"/> contains
/// every generated child, including out-of-flow boxes that are positioned
/// separately.
/// </para>
/// <para>
/// Boxes are owned by <see cref="LayoutEngine"/>. With
/// <see cref="LayoutOptions.ReuseLayoutBoxes"/> they are mutated in place on the
/// next frame, so a result that must outlive the frame needs its instruction
/// stream snapshotted; the box tree itself is never copied. Instances are not
/// thread-safe.
/// </para>
/// </remarks>
/// <seealso cref="LayoutResult"/>
public sealed class LayoutBox
{
    /// <summary>Cached intrinsic (max-content) width in CSS pixels.</summary>
    private float _intrinsicWidth;

    /// <summary>
    /// Whether <see cref="_intrinsicWidth"/> has been computed. The flag lets a
    /// cached zero be distinguished from "not computed yet".
    /// </summary>
    private bool _hasIntrinsicWidth;

    /// <summary>Creates a box for a node and pre-sizes its child list.</summary>
    /// <param name="node">The DOM node the box represents; stored as <see cref="Node"/>.</param>
    /// <param name="style">The resolved style; stored as <see cref="Style"/>.</param>
    /// <param name="depth">The depth from the root, where the root is 0.</param>
    /// <param name="childCapacity">The initial child list capacity; 0 leaves the list empty and unallocated.</param>
    /// <remarks>
    /// Construction only sets identity and style. Geometry stays at zero until
    /// the layout pass calls the internal box-sizing helper.
    /// </remarks>
    public LayoutBox(IgniteSupportedDomNode node, ComputedStyle style, int depth, int childCapacity = 0)
    {
        Node = node;
        Style = style;
        Depth = depth;
        Children = childCapacity > 0 ? new List<LayoutBox>(childCapacity) : [];
    }

    /// <summary>Gets the DOM node this box was generated for.</summary>
    /// <remarks>The identity is stable across frames, which lets the engine cache and reuse boxes by node.</remarks>
    public IgniteSupportedDomNode Node { get; }

    /// <summary>Gets the resolved style used for this frame's layout and paint.</summary>
    /// <remarks>
    /// The setter is internal because the animation runtime may swap in an
    /// animated style; callers should treat the value as read-only.
    /// </remarks>
    public ComputedStyle Style { get; internal set; }

    /// <summary>Gets the depth of the box in the generated tree, where the root is 0.</summary>
    public int Depth { get; internal set; }

    /// <summary>
    /// Prepares a cached box for another layout pass. The node identity is
    /// stable, so only the derived style, depth, children and geometry need to
    /// be replaced. Reusing the box (and its child list capacity) removes the
    /// per-frame allocation of the whole box tree.
    /// </summary>
    /// <param name="style">The style for the new frame.</param>
    /// <param name="depth">The box's depth in the new tree.</param>
    /// <param name="parent">The new parent, or <see langword="null"/> for the root.</param>
    /// <remarks>
    /// Clears the child list and resets every geometry, scroll, text and float
    /// field. The owned float context (<see cref="OwnedFloatContext"/>) is kept
    /// so its active-float list can be reused, and the child list keeps its
    /// capacity.
    /// </remarks>
    internal void Reset(ComputedStyle style, int depth, LayoutBox? parent)
    {
        Style = style;
        Depth = depth;
        Parent = parent;
        Children.Clear();

        BorderBox = default;
        PaddingBox = default;
        ContentBox = default;
        MarginBox = default;
        Margin = default;
        Border = default;
        Padding = default;
        Radius = default;

        IsContents = false;
        PseudoElementKind = PseudoElementKind.None;
        ContainingBlock = null;
        StaticPosition = default;
        IsScrollContainer = false;
        ScrollOffsetX = 0;
        ScrollOffsetY = 0;
        ScrollWidth = 0;
        ScrollHeight = 0;
        FloatContext = null;
        Text = null;
        TextOrigin = default;
        Baseline = 0;
        _intrinsicWidth = 0;
        _hasIntrinsicWidth = false;
    }

    /// <summary>Gets the parent box, or <see langword="null"/> for the root.</summary>
    public LayoutBox? Parent { get; internal set; }

    /// <summary>Gets the generated child boxes in document order.</summary>
    /// <remarks>
    /// The list includes out-of-flow children, which are positioned separately
    /// and may paint outside the parent's geometry. The list is reused when the
    /// box is reset.
    /// </remarks>
    public List<LayoutBox> Children { get; }

    /// <summary>Absolute border-box rect.</summary>
    /// <remarks>Unscrolled CSS pixels; includes padding and border but not margin.</remarks>
    public Rect BorderBox { get; internal set; }

    /// <summary>Absolute padding-box rect, inside the border.</summary>
    public Rect PaddingBox { get; internal set; }

    /// <summary>Absolute content-box rect, inside the padding.</summary>
    /// <remarks>This is the containing block for normal-flow children and the box used for text layout.</remarks>
    public Rect ContentBox { get; internal set; }

    /// <summary>Absolute margin-box rect.</summary>
    /// <remarks>Can extend outside the parent or the viewport; it is not clipped to either.</remarks>
    public Rect MarginBox { get; internal set; }

    /// <summary>Gets the resolved margin thicknesses in CSS pixels.</summary>
    public Thickness Margin { get; internal set; }

    /// <summary>Gets the resolved border thicknesses in CSS pixels.</summary>
    /// <remarks>Sides whose style is <see cref="BorderStyle.None"/> or <see cref="BorderStyle.Hidden"/> are zero.</remarks>
    public Thickness Border { get; internal set; }

    /// <summary>Gets the resolved padding thicknesses in CSS pixels.</summary>
    public Thickness Padding { get; internal set; }

    /// <summary>Gets the resolved corner radii in CSS pixels.</summary>
    public CornerRadius Radius { get; internal set; }

    /// <summary>Gets or sets which generated pseudo-element produced this box, if any.</summary>
    internal PseudoElementKind PseudoElementKind { get; set; }

    /// <summary>Gets a value indicating whether the box comes from <c>display: contents</c>.</summary>
    /// <remarks>Such a box owns no background, border or geometry of its own; its children are painted in its place.</remarks>
    public bool IsContents { get; internal set; }

    /// <summary>True when this box is out of normal flow and laid out separately.</summary>
    public bool IsOutOfFlow => Style.Position is Position.Absolute or Position.Fixed;

    /// <summary>Gets a value indicating whether the box is fixed to the viewport.</summary>
    public bool IsFixed => Style.Position is Position.Fixed;

    /// <summary>The containing block for an absolutely positioned box, or null for the viewport.</summary>
    /// <remarks>
    /// Fixed boxes always use <see langword="null"/>, meaning the viewport. The
    /// reference is not updated by <see cref="Translate"/>.
    /// </remarks>
    public LayoutBox? ContainingBlock { get; internal set; }

    /// <summary>Flow position used when an absolutely positioned box has no insets.</summary>
    /// <remarks>Absolute CSS pixels in unscrolled layout space.</remarks>
    public Point StaticPosition { get; internal set; }

    /// <summary>Gets a value indicating whether this box clips or scrolls its content.</summary>
    public bool IsScrollContainer { get; internal set; }

    /// <summary>Gets the current horizontal scroll position in CSS pixels.</summary>
    /// <remarks>Non-negative; 0 when the box is not scrolled. Layout geometry is stored unscrolled.</remarks>
    public float ScrollOffsetX { get; internal set; }

    /// <summary>Gets the current vertical scroll position in CSS pixels.</summary>
    /// <remarks>Non-negative; 0 when the box is not scrolled. Layout geometry is stored unscrolled.</remarks>
    public float ScrollOffsetY { get; internal set; }

    /// <summary>Gets the scrollable content width in CSS pixels.</summary>
    /// <remarks>May exceed <see cref="ClientWidth"/>; this is the extent used to size scrollbar thumbs.</remarks>
    public float ScrollWidth { get; internal set; }

    /// <summary>Gets the scrollable content height in CSS pixels.</summary>
    /// <remarks>May exceed <see cref="ClientHeight"/>; this is the extent used to size scrollbar thumbs.</remarks>
    public float ScrollHeight { get; internal set; }

    /// <summary>Gets the client width, which is the padding-box width in CSS pixels.</summary>
    public float ClientWidth => PaddingBox.Width;

    /// <summary>Gets the client height, which is the padding-box height in CSS pixels.</summary>
    public float ClientHeight => PaddingBox.Height;

    /// <summary>
    /// Active floats for the block formatting context this box participates in.
    /// The storage is shared with ancestors until a box establishes a new BFC.
    /// </summary>
    /// <remarks>This is a borrowed reference; the owning box stores it in <see cref="OwnedFloatContext"/>.</remarks>
    internal FloatContext? FloatContext { get; set; }

    /// <summary>
    /// The float context this box owns when it establishes a block formatting
    /// context. It is kept across layouts so the context and its list can be
    /// cleared and reused instead of reallocated.
    /// </summary>
    internal FloatContext? OwnedFloatContext { get; set; }

    /// <summary>Laid-out text belonging directly to this element, if any.</summary>
    /// <remarks><see langword="null"/> when the element has no direct text; child boxes carry their own text.</remarks>
    public TextLayout? Text { get; internal set; }

    /// <summary>Gets the absolute position of the text layout origin in CSS pixels.</summary>
    /// <remarks>
    /// Line offsets from <see cref="TextLayout"/> are relative to this point;
    /// the paint pass adds them without applying the box's scroll offset.
    /// </remarks>
    public Point TextOrigin { get; internal set; }

    /// <summary>First-line baseline, relative to the border box top.</summary>
    /// <remarks>Zero when the box has no text.</remarks>
    public float Baseline { get; internal set; }

    /// <summary>Gets a value indicating whether the style specified a <c>z-index</c>.</summary>
    public bool HasZIndex => Style.HasZIndex;

    /// <summary>Gets the stacking level, using 0 when <c>z-index</c> is <c>auto</c>.</summary>
    public int EffectiveZIndex => Style.HasZIndex ? Style.ZIndex : 0;

    /// <summary>Gets a value indicating whether this box establishes a stacking context.</summary>
    /// <remarks>
    /// True for a specified <c>z-index</c>, opacity below <c>0.9999</c>, a
    /// transform, fixed positioning, a non-identity filter, backdrop filter,
    /// clip path or mask, a non-normal blend mode, or <c>isolation: isolate</c>.
    /// </remarks>
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

    /// <summary>Gets a value indicating whether the box contributes paint output.</summary>
    /// <remarks>False for <c>display: contents</c> boxes and for styles whose display is <c>none</c>.</remarks>
    public bool IsPainted => !IsContents && Style.Display != Display.None;

    /// <summary>True when the box has a CSS animation applied on this frame.</summary>
    public bool IsAnimated => Style.IsAnimated;

    /// <summary>
    /// Gets or sets the cached intrinsic (max-content) width in CSS pixels.
    /// </summary>
    /// <remarks>Setting the value also records that the width has been computed, even when the value is zero.</remarks>
    internal float IntrinsicWidth
    {
        get => _intrinsicWidth;
        set
        {
            _intrinsicWidth = value;
            _hasIntrinsicWidth = true;
        }
    }

    /// <summary>Gets a value indicating whether <see cref="IntrinsicWidth"/> has been computed for this box.</summary>
    internal bool HasIntrinsicWidth => _hasIntrinsicWidth;

    /// <summary>Translates this box and its in-flow descendants.</summary>
    /// <param name="dx">The horizontal delta in CSS pixels.</param>
    /// <param name="dy">The vertical delta in CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// Moves all four rects and <see cref="TextOrigin"/>; margins, borders and
    /// padding are offsets and stay unchanged. A zero/zero translation returns
    /// immediately.
    /// </para>
    /// <para>
    /// Out-of-flow children need more care than in-flow children. Their
    /// <see cref="StaticPosition"/> always moves with the subtree because it
    /// records where the box would have been in normal flow. Their already
    /// resolved geometry only moves when this box is their containing block;
    /// an absolutely positioned child anchored to an ancestor, and a fixed child
    /// anchored to the viewport, must stay where its containing block puts it.
    /// This matters when a flex or grid item is measured at the origin and then
    /// translated into place: without it, a dropdown menu without insets keeps
    /// the static position it had before the item moved.
    /// </para>
    /// </remarks>
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
                continue;
            }

            child.StaticPosition = child.StaticPosition.Translate(dx, dy);

            // A fixed child contained by a transformed/filtered ancestor moves
            // with that ancestor; one anchored to the viewport does not.
            if (ReferenceEquals(child.ContainingBlock, this))
            {
                child.Translate(dx, dy);
            }
        }
    }

    /// <summary>Sets the border box and derives the padding, content and margin boxes from it.</summary>
    /// <param name="x">Absolute left edge in CSS pixels.</param>
    /// <param name="y">Absolute top edge in CSS pixels.</param>
    /// <param name="width">Border-box width; clamped to zero.</param>
    /// <param name="height">Border-box height; clamped to zero.</param>
    /// <remarks>
    /// Padding and content dimensions are each clamped to zero, so a border or
    /// padding larger than the box collapses rather than producing negative
    /// rects. The margin box is expanded by <see cref="Margin"/> and is not
    /// clamped. Text origin and baseline are not updated here.
    /// </remarks>
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

    /// <summary>Returns a diagnostic string containing the display mode, tag name and border box.</summary>
    /// <returns>A string such as <c>Block DIV (0,0,100,20)</c>; not intended for display to users.</returns>
    public override string ToString()
    {
        return $"{Style.Display} {Node.TagName ?? "?"} {BorderBox}";
    }
}

/// <summary>The result of laying out one frame.</summary>
/// <remarks>
/// <para>
/// <see cref="Root"/> and the box tree are owned by the engine. Instruction
/// snapshots, the constant pool and the node map are copied only when
/// <see cref="LayoutOptions.SnapshotResults"/> is enabled; otherwise all views
/// alias engine storage that is cleared at the start of the next layout.
/// </para>
/// <para>
/// A snapshot owns an independent copy of the constants, so ids embedded in the
/// copied instructions stay valid after the engine clears its pool for the next
/// layout. With snapshotting disabled the pool is the engine's and is only
/// valid until the next call to
/// <see cref="LayoutEngine.Layout(IgniteSupportedDomNode, LayoutOptions)"/>.
/// </para>
/// </remarks>
public sealed class LayoutResult
{
    private readonly Dictionary<IgniteSupportedDomNode, LayoutBox> _byNode;

    /// <summary>Creates a frame result, optionally copying the instruction stream and node map.</summary>
    /// <param name="root">The root box, or <see langword="null"/> when the document produced no box.</param>
    /// <param name="instructions">The display list to expose.</param>
    /// <param name="constants">The constant pool referenced by the instructions; copied when <paramref name="snapshot"/> is <see langword="true"/>.</param>
    /// <param name="byNode">The node-to-box map; copied when <paramref name="snapshot"/> is <see langword="true"/>.</param>
    /// <param name="stats">The optimiser statistics for this frame.</param>
    /// <param name="hasAnimations">Whether any box had a running animation.</param>
    /// <param name="snapshot">Whether to copy the instruction stream and node map.</param>
    /// <param name="hasLayoutAnimations">Whether an active animation can change geometry.</param>
    /// <remarks>
    /// The instruction copy pre-sizes its buffer to the source count; the
    /// constant pool and node map are shallow copies. The box tree is always
    /// shared, so box geometry is only immutable when boxes are not reused.
    /// </remarks>
    public LayoutResult(
        LayoutBox? root,
        InstructionBuffer instructions,
        ConstantPool constants,
        Dictionary<IgniteSupportedDomNode, LayoutBox> byNode,
        OptimisationStats stats,
        bool hasAnimations = false,
        bool snapshot = true,
        bool hasLayoutAnimations = false)
    {
        Root = root;
        Instructions = snapshot ? Snapshot(instructions) : instructions;
        Constants = snapshot ? constants.Snapshot() : constants;
        _byNode = snapshot
            ? new Dictionary<IgniteSupportedDomNode, LayoutBox>(byNode)
            : byNode;
        Stats = stats;
        HasAnimations = hasAnimations;
        HasLayoutAnimations = hasLayoutAnimations;
    }

    /// <summary>Copies the visible instructions into a new buffer.</summary>
    /// <param name="source">The buffer to copy; only the first <see cref="InstructionBuffer.Count"/> entries are read.</param>
    /// <returns>A new buffer with the same instruction sequence.</returns>
    private static InstructionBuffer Snapshot(InstructionBuffer source)
    {
        var copy = new InstructionBuffer(Math.Max(4, source.Count));
        copy.AddRange(source.Span);
        return copy;
    }

    /// <summary>Gets the root box, or <see langword="null"/> when the root generated no box.</summary>
    public LayoutBox? Root { get; }

    /// <summary>Gets the display list for this frame.</summary>
    /// <remarks>May alias the engine's reusable buffer when snapshotting is disabled.</remarks>
    public InstructionBuffer Instructions { get; }

    /// <summary>Gets the constant pool referenced by <see cref="Instructions"/>.</summary>
    /// <remarks>
    /// An independent snapshot when <see cref="LayoutOptions.SnapshotResults"/>
    /// is enabled; otherwise the engine's pool, which is cleared at the start of
    /// the next layout.
    /// </remarks>
    public ConstantPool Constants { get; }

    /// <summary>Gets the optimiser statistics for this frame.</summary>
    /// <remarks>All zero when optimisation is disabled.</remarks>
    public OptimisationStats Stats { get; }

    /// <summary>True when at least one box had a running CSS animation on this frame.</summary>
    public bool HasAnimations { get; }

    /// <summary>True when an active animation can change geometry.</summary>
    /// <remarks>
    /// When <see langword="false"/> animation frames still need a repaint but
    /// can reuse the previous layout; when true the box tree must be rebuilt.
    /// </remarks>
    public bool HasLayoutAnimations { get; }

    /// <summary>The node-to-box map, shared with the engine that built it.</summary>
    /// <remarks>Aliases the engine's dictionary when snapshotting is disabled; do not mutate.</remarks>
    internal IReadOnlyDictionary<IgniteSupportedDomNode, LayoutBox> Boxes => _byNode;

    /// <summary>Gets the instruction span for allocation-free enumeration.</summary>
    /// <value>A view over the live buffer; invalidated when the buffer grows, is cleared or is reused.</value>
    public ReadOnlySpan<Instruction> Opcodes => Instructions.Span;

    /// <summary>Looks up the box generated for a node.</summary>
    /// <param name="node">The DOM node to look up.</param>
    /// <param name="box">Receives the box when found; otherwise the default value.</param>
    /// <returns><see langword="true"/> when the node generated a box on this frame.</returns>
    /// <remarks>
    /// Nodes with <c>display: none</c> are absent, as are descendants beyond
    /// the configured depth limit.
    /// </remarks>
    public bool TryGetBox(IgniteSupportedDomNode node, out LayoutBox box)
        => _byNode.TryGetValue(node, out box!);
}

/// <summary>Counts recorded by the display-list optimiser for one frame.</summary>
/// <param name="InputInstructions">The number of instructions before optimisation.</param>
/// <param name="OutputInstructions">The number of instructions after optimisation.</param>
/// <param name="Culled">Draws removed because they were outside the clip or had an empty clip.</param>
/// <param name="Merged">Draws fused into a previous rectangle.</param>
/// <param name="ScopesRemoved">Clip and opacity instructions dropped as redundant.</param>
public readonly record struct OptimisationStats(
    int InputInstructions,
    int OutputInstructions,
    int Culled,
    int Merged,
    int ScopesRemoved)
{
    /// <summary>All counts zero, used when optimisation is disabled.</summary>
    public static readonly OptimisationStats Empty = new(0, 0, 0, 0, 0);

    /// <summary>Gets the instruction count removed by optimisation.</summary>
    public int Saved => Math.Max(0, InputInstructions - OutputInstructions);
}
