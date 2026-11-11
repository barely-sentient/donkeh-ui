using System.Numerics;

namespace Donkeh.LayoutLab;

/// <summary>
/// The resolved, layout-ready style for one element. Values that depend on the
/// containing block (percentages, auto) stay as <see cref="CssLength"/> until a
/// layout pass supplies the right basis.
/// </summary>
/// <remarks>
/// <para>
/// Instances are produced by the style resolver from the element's cascaded
/// declarations and are mutable: the animation runtime copies values into them
/// between frames rather than rebuilding the style. Treat a style as owned by
/// the layout engine and do not share it across threads.
/// </para>
/// <para>
/// Inherited scalars (colour, font, text and visibility properties) are copied
/// from the parent by <see cref="CopyInheritedFrom"/>; custom properties are
/// not copied on the hot path and are instead resolved by walking
/// <see cref="StyleParent"/>. Percentages, <c>auto</c>, <c>em</c> and viewport
/// units are resolved during layout through a <see cref="LengthContext"/>.
/// </para>
/// <para>
/// <see cref="Clone"/> and <see cref="CopyAllFrom"/> copy references to
/// arrays, filters and the animation list, so those objects must be treated as
/// immutable once published.
/// </para>
/// </remarks>
/// <seealso cref="StyleResolver"/>
/// <seealso cref="TextStyle"/>
public sealed class ComputedStyle
{
    /// <summary>Sentinel for <c>currentColor</c>.</summary>
    /// <remarks>
    /// The value is an out-of-band marker, not a valid ARGB colour; it flows
    /// through <see cref="BorderColorTop"/> and friends until
    /// <see cref="ResolveBorderColor"/> replaces it with <see cref="Color"/>.
    /// </remarks>
    public const uint CurrentColor = 0x00000001;

    /// <summary>
    /// Paint specs derived from this style. They are immutable and are reused
    /// across frames so background painting does not allocate each layout.
    /// </summary>
    /// <remarks>
    /// The array is built lazily by the paint pass by merging
    /// <see cref="BackgroundImages"/> with the layer-independent
    /// <see cref="BackgroundRepeat"/>, <see cref="BackgroundClip"/>,
    /// <see cref="BackgroundOrigin"/> and <see cref="BackgroundFixed"/> values.
    /// The cache is not invalidated automatically when those properties change,
    /// so mutating them requires clearing this field.
    /// </remarks>
    internal BackgroundImageSpec[]? CachedBackgroundSpecs;

    /// <summary>
    /// Gets or sets the used <c>display</c> value, which selects the box type
    /// and formatting context.
    /// </summary>
    /// <remarks>
    /// <see cref="Display.None"/> suppresses box generation entirely;
    /// <see cref="Display.Contents"/> generates only children.
    /// </remarks>
    public Display Display { get; set; } = Display.Block;

    /// <summary>Gets or sets the positioning scheme used to place the box.</summary>
    public Position Position { get; set; } = Position.Static;

    /// <summary>Gets or sets the side the box floats to, or <c>none</c> for the normal flow.</summary>
    public Float Float { get; set; } = Float.None;

    /// <summary>Gets or sets which earlier floats the box must clear.</summary>
    public Clear Clear { get; set; } = Clear.None;

    /// <summary>Gets or sets whether <see cref="Width"/> and <see cref="Height"/> include padding and border.</summary>
    public BoxSizing BoxSizing { get; set; } = BoxSizing.ContentBox;

    /// <summary>Gets or sets whether the box is painted. <see cref="Visibility.Hidden"/> still occupies layout space.</summary>
    public Visibility Visibility { get; set; } = Visibility.Visible;

    /// <summary>Gets or sets whether a <c>z-index</c> was specified, distinguishing an explicit zero from <c>auto</c>.</summary>
    public bool HasZIndex { get; set; }

    /// <summary>Gets or sets the stacking level used when <see cref="HasZIndex"/> is <see langword="true"/>.</summary>
    public int ZIndex { get; set; }

    /// <summary>Gets or sets the group opacity in the range 0 to 1.</summary>
    /// <remarks>Values below <c>0.9999</c> force a stacking context and an opacity scope in the display list.</remarks>
    public float Opacity { get; set; } = 1;

    /// <summary>Gets or sets the used width, or <see cref="CssLength.Auto"/> to size from the layout mode.</summary>
    /// <remarks>Percentages resolve against the containing block width by the layout pass.</remarks>
    public CssLength Width { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the used height, or <see cref="CssLength.Auto"/> to size from content.</summary>
    /// <remarks>Percentages resolve against the containing block height when it is definite.</remarks>
    public CssLength Height { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the minimum width constraint applied after the preferred width is computed.</summary>
    /// <remarks><see cref="CssLength.Auto"/> means no minimum, which is also how <c>none</c> is represented.</remarks>
    public CssLength MinWidth { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the minimum height constraint applied after the preferred height is computed.</summary>
    /// <remarks><see cref="CssLength.Auto"/> means no minimum.</remarks>
    public CssLength MinHeight { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the maximum width constraint.</summary>
    /// <remarks><see cref="CssLength.Auto"/> means no maximum, which is how <c>none</c> is stored.</remarks>
    public CssLength MaxWidth { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the maximum height constraint.</summary>
    /// <remarks><see cref="CssLength.Auto"/> means no maximum.</remarks>
    public CssLength MaxHeight { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the preferred width-to-height ratio, or <see langword="null"/> when unset.</summary>
    /// <remarks>Used to derive a missing dimension when exactly one of width or height is definite.</remarks>
    public float? AspectRatio { get; set; }

    /// <summary>Gets or sets the top inset for a positioned box.</summary>
    /// <remarks><see cref="CssLength.Auto"/> leaves the inset to the box's static position.</remarks>
    public CssLength Top { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the right inset for a positioned box; <c>auto</c> uses the static position.</summary>
    public CssLength Right { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the bottom inset for a positioned box; <c>auto</c> uses the static position.</summary>
    public CssLength Bottom { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the left inset for a positioned box; <c>auto</c> uses the static position.</summary>
    public CssLength Left { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the four resolved margins.</summary>
    /// <remarks>
    /// Auto margins absorb free space in block and flex layout; percentages
    /// resolve against the containing block width.
    /// </remarks>
    public CssLength4 Margin { get; set; } = CssLength4.Zero;

    /// <summary>Gets or sets the four paddings, inside the border and outside the content box.</summary>
    public CssLength4 Padding { get; set; } = CssLength4.Zero;

    /// <summary>Gets or sets the four border widths before the border style is considered.</summary>
    /// <remarks>
    /// A side whose style is <see cref="BorderStyle.None"/> or
    /// <see cref="BorderStyle.Hidden"/> has no painted width.
    /// </remarks>
    public CssLength4 BorderWidth { get; set; } = CssLength4.Zero;

    /// <summary>Gets or sets the style of the top border.</summary>
    public BorderStyle BorderStyleTop { get; set; } = BorderStyle.None;

    /// <summary>Gets or sets the style of the right border.</summary>
    public BorderStyle BorderStyleRight { get; set; } = BorderStyle.None;

    /// <summary>Gets or sets the style of the bottom border.</summary>
    public BorderStyle BorderStyleBottom { get; set; } = BorderStyle.None;

    /// <summary>Gets or sets the style of the left border.</summary>
    public BorderStyle BorderStyleLeft { get; set; } = BorderStyle.None;

    /// <summary>Gets or sets the top border colour, or <see cref="CurrentColor"/>.</summary>
    public uint BorderColorTop { get; set; } = CurrentColor;

    /// <summary>Gets or sets the right border colour, or <see cref="CurrentColor"/>.</summary>
    public uint BorderColorRight { get; set; } = CurrentColor;

    /// <summary>Gets or sets the bottom border colour, or <see cref="CurrentColor"/>.</summary>
    public uint BorderColorBottom { get; set; } = CurrentColor;

    /// <summary>Gets or sets the left border colour, or <see cref="CurrentColor"/>.</summary>
    public uint BorderColorLeft { get; set; } = CurrentColor;

    /// <summary>Gets or sets the four corner radii before resolution.</summary>
    /// <remarks>Percentages resolve against the smaller of the box width and height.</remarks>
    public CssCornerRadius4 BorderRadius { get; set; } = CssCornerRadius4.Zero;

    /// <summary>Gets or sets the computed text colour as packed ARGB; inherited by <see cref="CopyInheritedFrom"/>.</summary>
    public uint Color { get; set; } = CssColor.Black;

    /// <summary>Gets or sets the background colour as packed ARGB.</summary>
    public uint BackgroundColor { get; set; } = CssColor.Transparent;

    /// <summary>Gets or sets the parsed background layers in declaration order, or <see langword="null"/> when none.</summary>
    /// <remarks>
    /// Each layer keeps its own gradient stops and image URL. Layer-independent
    /// properties such as <see cref="BackgroundRepeat"/> are merged into
    /// <see cref="CachedBackgroundSpecs"/> at paint time.
    /// </remarks>
    public BackgroundImageSpec[]? BackgroundImages { get; set; }

    /// <summary>
    /// Gets or sets the explicit background width used when
    /// <see cref="BackgroundSizeMode"/> is <see cref="BackgroundSizeMode.Explicit"/>.
    /// </summary>
    public CssLength BackgroundWidth { get; set; } = CssLength.Auto;

    /// <summary>
    /// Gets or sets the explicit background height used when
    /// <see cref="BackgroundSizeMode"/> is <see cref="BackgroundSizeMode.Explicit"/>.
    /// </summary>
    public CssLength BackgroundHeight { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets how the background image is sized inside its positioning area.</summary>
    public BackgroundSizeMode BackgroundSizeMode { get; set; } = BackgroundSizeMode.Auto;

    /// <summary>Gets or sets the horizontal background position.</summary>
    /// <remarks>Percentages are relative to the positioning area minus the image size, matching CSS.</remarks>
    public CssLength BackgroundPositionX { get; set; } = CssLength.Percent(0);

    /// <summary>Gets or sets the vertical background position.</summary>
    /// <remarks>Percentages are relative to the positioning area minus the image size, matching CSS.</remarks>
    public CssLength BackgroundPositionY { get; set; } = CssLength.Percent(0);

    /// <summary>Gets or sets how the background image tiles.</summary>
    public BackgroundRepeat BackgroundRepeat { get; set; } = BackgroundRepeat.Repeat;

    /// <summary>Gets or sets the box the background is clipped to.</summary>
    public BackgroundBox BackgroundClip { get; set; } = BackgroundBox.BorderBox;

    /// <summary>Gets or sets the box the background is positioned and sized against.</summary>
    public BackgroundBox BackgroundOrigin { get; set; } = BackgroundBox.PaddingBox;

    /// <summary>Gets or sets whether the background is fixed to the viewport rather than the box.</summary>
    public bool BackgroundFixed { get; set; }

    /// <summary>Gets or sets the first font family name; inherited.</summary>
    public string FontFamily { get; set; } = "sans-serif";

    /// <summary>Gets or sets the font size; inherited. A percentage resolves against the parent font size.</summary>
    public CssLength FontSize { get; set; } = CssLength.Px(16);

    /// <summary>Gets or sets the numeric font weight (1 to 1000); inherited.</summary>
    /// <remarks><see cref="SimpleTextMeasurer"/> widens runs at weight 600 and above.</remarks>
    public int FontWeight { get; set; } = 400;

    /// <summary>Gets or sets whether the font is italic; inherited.</summary>
    public bool Italic { get; set; }

    /// <summary>Gets or sets the line height; inherited.</summary>
    /// <remarks>
    /// A <see cref="LengthUnit.Number"/> value is a multiplier of the resolved
    /// font size; other units are absolute and resolve through the length
    /// context. The initial value is the number <c>1.2</c>.
    /// </remarks>
    public CssLength LineHeight { get; set; } = CssLength.Number(1.2f);

    /// <summary>Gets or sets the extra spacing added after each character; inherited.</summary>
    public CssLength LetterSpacing { get; set; } = CssLength.Zero;

    /// <summary>Gets or sets the horizontal alignment of text lines; inherited.</summary>
    public TextAlign TextAlign { get; set; } = TextAlign.Start;

    /// <summary>Gets or sets whitespace collapsing and wrapping rules; inherited.</summary>
    public WhiteSpace WhiteSpace { get; set; } = WhiteSpace.Normal;

    /// <summary>Gets or sets whether overflowing text is clipped or ellipsised; inherited.</summary>
    public TextOverflow TextOverflow { get; set; } = TextOverflow.Clip;

    /// <summary>Gets or sets the text decoration flags; copied from the parent by inheritance.</summary>
    public TextDecoration TextDecoration { get; set; } = TextDecoration.None;

    /// <summary>Gets or sets the case transformation.</summary>
    /// <remarks>
    /// The value is copied from the parent, but <see cref="TextLayoutEngine"/>
    /// does not apply it: the layout engine transforms the string before
    /// building the <see cref="TextStyle"/>.
    /// </remarks>
    public TextTransform TextTransform { get; set; } = TextTransform.None;

    /// <summary>Gets or sets the inline base direction; inherited.</summary>
    /// <remarks>Affects <see cref="TextAlign.Start"/> and <see cref="TextAlign.End"/> mapping.</remarks>
    public Direction Direction { get; set; } = Direction.LeftToRight;

    /// <summary>Gets or sets the writing mode; inherited.</summary>
    public WritingMode WritingMode { get; set; } = WritingMode.HorizontalTopToBottom;

    /// <summary>Gets or sets the maximum number of text lines, or 0 for no clamp.</summary>
    /// <remarks>When the clamp is exceeded the text layout is marked truncated.</remarks>
    public int WebkitLineClamp { get; set; }

    /// <summary>Gets or sets whether long words may break; not copied by inheritance in this engine.</summary>
    public OverflowWrap OverflowWrap { get; set; } = OverflowWrap.Normal;

    /// <summary>Gets or sets the main-axis direction of a flex container.</summary>
    public FlexDirection FlexDirection { get; set; } = FlexDirection.Row;

    /// <summary>Gets or sets whether flex items wrap onto multiple lines.</summary>
    public FlexWrap FlexWrap { get; set; } = FlexWrap.NoWrap;

    /// <summary>Gets or sets main-axis distribution of flex items and grid tracks.</summary>
    public JustifyContent JustifyContent { get; set; } = JustifyContent.FlexStart;

    /// <summary>Gets or sets cross-axis alignment of flex and grid items.</summary>
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;

    /// <summary>Gets or sets distribution of wrapped flex lines or grid tracks along the cross axis.</summary>
    public AlignContent AlignContent { get; set; } = AlignContent.Stretch;

    /// <summary>
    /// Gets or sets a per-item cross-axis alignment override, or
    /// <see cref="AlignSelf.Auto"/> to inherit <see cref="AlignItems"/>.
    /// </summary>
    public AlignSelf AlignSelf { get; set; } = AlignSelf.Auto;

    /// <summary>Gets or sets the flex grow factor, which distributes positive free space.</summary>
    public float FlexGrow { get; set; }

    /// <summary>Gets or sets the flex shrink factor, which distributes negative free space.</summary>
    public float FlexShrink { get; set; } = 1;

    /// <summary>Gets or sets the flex base size, or <c>auto</c> to use the item's main size.</summary>
    public CssLength FlexBasis { get; set; } = CssLength.Auto;

    /// <summary>Gets or sets the ordinal used to reorder flex and grid items.</summary>
    public int Order { get; set; }

    /// <summary>Gets or sets the gap between flex lines or grid rows.</summary>
    public CssLength RowGap { get; set; } = CssLength.Zero;

    /// <summary>Gets or sets the gap between flex items or grid columns.</summary>
    public CssLength ColumnGap { get; set; } = CssLength.Zero;

    /// <summary>Gets or sets the explicit column track list, or <see langword="null"/> when none was declared.</summary>
    public GridTrackList? GridTemplateColumns { get; set; }

    /// <summary>Gets or sets the explicit row track list, or <see langword="null"/> when none was declared.</summary>
    public GridTrackList? GridTemplateRows { get; set; }

    /// <summary>
    /// Gets or sets the track list used to size implicit columns, or
    /// <see langword="null"/> for a single auto track.
    /// </summary>
    public GridTrackList? GridAutoColumns { get; set; }

    /// <summary>
    /// Gets or sets the track list used to size implicit rows, or
    /// <see langword="null"/> for a single auto track.
    /// </summary>
    public GridTrackList? GridAutoRows { get; set; }

    /// <summary>Gets or sets the auto-placement direction and whether packing is dense.</summary>
    public GridAutoFlow GridAutoFlow { get; set; } = GridAutoFlow.Row;

    /// <summary>Gets or sets the resolved column placement of a grid item.</summary>
    public GridPlacement GridColumn { get; set; } = GridPlacement.Auto;

    /// <summary>Gets or sets the resolved row placement of a grid item.</summary>
    public GridPlacement GridRow { get; set; } = GridPlacement.Auto;

    /// <summary>Gets or sets the custom identifier used by <c>grid-area</c>, or <see langword="null"/> when unnamed.</summary>
    public string? GridAreaName { get; set; }

    /// <summary>Gets or sets the parsed named grid areas, or <see langword="null"/> when none.</summary>
    public GridTemplateAreas? GridTemplateAreas { get; set; }

    /// <summary>Gets or sets the default inline-axis alignment of grid items.</summary>
    public JustifyItems JustifyItems { get; set; } = JustifyItems.Normal;

    /// <summary>Gets or sets a per-item inline-axis alignment override, or <see cref="JustifySelf.Auto"/>.</summary>
    public JustifySelf JustifySelf { get; set; } = JustifySelf.Auto;

    /// <summary>Gets or sets the block-axis item alignment derived from <c>justify-items</c>.</summary>
    public AlignItems JustifyItemsBlock { get; set; } = AlignItems.Stretch;

    /// <summary>Gets or sets the horizontal overflow behaviour.</summary>
    /// <remarks>Any value other than <see cref="Overflow.Visible"/> makes the box a scroll container on that axis.</remarks>
    public Overflow OverflowX { get; set; } = Overflow.Visible;

    /// <summary>Gets or sets the vertical overflow behaviour.</summary>
    public Overflow OverflowY { get; set; } = Overflow.Visible;

    /// <summary>Gets or sets whether programmatic scrolling is instant or smooth.</summary>
    /// <remarks>The layout engine stores the value for the host; it does not animate scroll offsets itself.</remarks>
    public ScrollBehavior ScrollBehavior { get; set; } = ScrollBehavior.Auto;

    /// <summary>Gets or sets the scrollbar track colour as packed ARGB.</summary>
    public uint ScrollbarTrackColor { get; set; } = 0xFFE0E0E0;

    /// <summary>Gets or sets the scrollbar thumb colour as packed ARGB.</summary>
    public uint ScrollbarThumbColor { get; set; } = 0xFF909090;

    /// <summary>Gets or sets whether scrollbars are shown even when overflow does not request them.</summary>
    /// <remarks>See <see cref="ScrollbarLayout.TryGetVertical"/> for the exact visibility rule.</remarks>
    public bool ShowScrollbars { get; set; }

    /// <summary>Gets or sets the parsed transform, or <see langword="null"/> for none.</summary>
    public TransformSpec? Transform { get; set; }

    /// <summary>Gets or sets the horizontal transform origin; percentages resolve against the border box width.</summary>
    public CssLength TransformOriginX { get; set; } = CssLength.Percent(50);

    /// <summary>Gets or sets the vertical transform origin; percentages resolve against the border box height.</summary>
    public CssLength TransformOriginY { get; set; } = CssLength.Percent(50);

    /// <summary>Gets or sets the box shadows in paint order, or <see langword="null"/> when none.</summary>
    public ShadowSpec[]? BoxShadows { get; set; }

    /// <summary>Gets or sets the element filter chain, or <see langword="null"/> for none.</summary>
    public FilterSpec? Filter { get; set; }

    /// <summary>Gets or sets the backdrop filter chain, or <see langword="null"/> for none.</summary>
    public FilterSpec? BackdropFilter { get; set; }

    /// <summary>Gets or sets the clip path, or <see langword="null"/> for none.</summary>
    public ClipPathSpec? ClipPath { get; set; }

    /// <summary>Gets or sets the mask, or <see langword="null"/> for none.</summary>
    public MaskSpec? Mask { get; set; }

    /// <summary>Gets or sets how the element blends with its backdrop.</summary>
    public MixBlendMode MixBlendMode { get; set; } = MixBlendMode.Normal;

    /// <summary>Gets or sets whether the element forms an isolated stacking context.</summary>
    public bool Isolate { get; set; }

    /// <summary>Gets or sets the outline width, which does not affect layout.</summary>
    public CssLength OutlineWidth { get; set; } = CssLength.Zero;

    /// <summary>Gets or sets the outline style.</summary>
    public BorderStyle OutlineStyle { get; set; } = BorderStyle.None;

    /// <summary>Gets or sets the outline colour, or <see cref="CurrentColor"/>.</summary>
    public uint OutlineColor { get; set; } = CurrentColor;

    /// <summary>Gets or sets the gap between the outline and the border box; may be negative.</summary>
    public CssLength OutlineOffset { get; set; } = CssLength.Zero;

    /// <summary>Gets or sets how replaced content is fitted into its content box.</summary>
    public ObjectFit ObjectFit { get; set; } = ObjectFit.Fill;

    /// <summary>Gets or sets the table layout algorithm.</summary>
    public TableLayout TableLayout { get; set; } = TableLayout.Auto;

    /// <summary>Gets or sets whether table borders are collapsed or separated.</summary>
    public BorderCollapse BorderCollapse { get; set; } = BorderCollapse.Separate;

    /// <summary>Gets or sets the spacing between separated table borders.</summary>
    public CssLength BorderSpacing { get; set; } = CssLength.Px(2);

    /// <summary>Gets or sets the requested multi-column count; 1 means a single column.</summary>
    /// <remarks>
    /// The value is parsed and copied between styles, but the layout engine
    /// does not implement multi-column flow and never reads it.
    /// </remarks>
    public int ColumnCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the gap used when <see cref="ColumnGap"/> has no definite
    /// value in a multi-column context.
    /// </summary>
    /// <remarks>Stored for hosts; multi-column layout is not implemented by the engine.</remarks>
    public CssLength ColumnGapFallback { get; set; } = CssLength.Px(16);

    /// <summary>
    /// Custom properties declared by this element's own rules. Inherited
    /// variables are resolved by walking <see cref="StyleParent"/> instead of
    /// copying the whole map into every descendant.
    /// </summary>
    /// <remarks>
    /// Keys are case-sensitive property names including the leading <c>--</c>;
    /// values are the declared token strings, before <c>var()</c> substitution.
    /// <see langword="null"/> when the element declares no variables.
    /// </remarks>
    public Dictionary<string, string>? CustomProperties { get; set; }

    /// <summary>The style this element inherited from, used for variable lookup.</summary>
    /// <remarks>
    /// The chain is read-only during layout. A style must outlive its children
    /// because <see cref="TryGetCustomProperty"/> and
    /// <see cref="RefreshCustomPropertyHash"/> dereference it.
    /// </remarks>
    internal ComputedStyle? StyleParent;

    /// <summary>
    /// Hash of this style's effective custom-property chain. Variable-resolved
    /// declaration values are cached by this hash so restyles reuse the same
    /// resolved strings instead of rebuilding them.
    /// </summary>
    /// <remarks>
    /// The hash folds the parent's hash with this style's own entries using
    /// ordinal string comparison. It is process-local (it uses
    /// <see cref="System.HashCode"/>) and must not be persisted or compared
    /// across processes. Call <see cref="RefreshCustomPropertyHash"/> after any
    /// mutation of <see cref="CustomProperties"/>.
    /// </remarks>
    internal long CustomPropertyHash;

    /// <summary>Gets or sets the property ids the resolver rejected while computing this style.</summary>
    /// <remarks>
    /// The list is diagnostic only: layout never reads it. <see langword="null"/>
    /// when every declaration was understood.
    /// </remarks>
    public List<int>? UnsupportedProperties { get; set; }

    /// <summary>Animation entries resolved from the <c>animation</c> properties.</summary>
    /// <remarks>
    /// The list is shared by <see cref="CopyAllFrom"/> and cloned only by
    /// <see cref="Clone"/>. <see langword="null"/> when the element has no
    /// animations.
    /// </remarks>
    public List<AnimationSpec>? Animations { get; set; }

    /// <summary>True when this style is the result of applying an animation at the current frame.</summary>
    /// <remarks>
    /// Set by the animation runtime and exposed through
    /// <see cref="LayoutBox.IsAnimated"/> for hosts and diagnostics.
    /// </remarks>
    public bool IsAnimated { get; set; }

    /// <summary>
    /// True when one of this element's declarations uses a viewport-height
    /// unit (<c>vh</c>, <c>dvh</c>, ...). The layout engine then includes the
    /// viewport height in its style cache signature.
    /// </summary>
    /// <remarks>
    /// The flag is sticky for the lifetime of the style: it is set when a
    /// declaration is parsed and is never cleared if that declaration is later
    /// overridden.
    /// </remarks>
    internal bool UsesViewportHeight;

    /// <summary>Gets a value indicating whether the box is absolutely or fixed positioned.</summary>
    public bool IsAbsolutelyPositioned => Position is Position.Absolute or Position.Fixed;

    /// <summary>Gets a value indicating whether this element establishes a flex formatting context.</summary>
    public bool IsFlexContainer => Display is Display.Flex or Display.InlineFlex;

    /// <summary>Gets a value indicating whether this element establishes a grid formatting context.</summary>
    public bool IsGridContainer => Display is Display.Grid or Display.InlineGrid;

    /// <summary>Gets a value indicating whether this element participates in normal block flow as a container.</summary>
    public bool IsBlockContainer => Display is Display.Block or Display.FlowRoot or Display.ListItem;

    /// <summary>Gets a value indicating whether the box generates an inline-level box.</summary>
    public bool IsInlineLevel => Display is Display.Inline or Display.InlineBlock or Display.InlineFlex
        or Display.InlineGrid or Display.InlineTable;

    /// <summary>Gets a value indicating whether either overflow axis clips or scrolls content.</summary>
    public bool IsScrollContainer => OverflowX is Overflow.Hidden or Overflow.Clip or Overflow.Scroll or Overflow.Auto
        || OverflowY is Overflow.Hidden or Overflow.Clip or Overflow.Scroll or Overflow.Auto;

    /// <summary>Gets a value indicating whether the box remains in normal flow (not positioned, not floated).</summary>
    public bool IsInFlow => Position is not (Position.Absolute or Position.Fixed) && Float is Float.None;

    /// <summary>
    /// Copies the inherited scalar values from <paramref name="parent"/>, links
    /// the parent chain, and clones the parent's custom-property map when it is
    /// non-empty.
    /// </summary>
    /// <param name="parent">The style of the element's parent, which must outlive this style.</param>
    /// <remarks>
    /// <para>
    /// The clone is a snapshot: later changes to the parent's map do not
    /// propagate. <see cref="CopyInheritedFromShared"/> is the allocation-free
    /// alternative used on the hot path.
    /// </para>
    /// <para>
    /// Non-inherited values such as <see cref="Display"/> and
    /// <see cref="Margin"/> keep their initial values.
    /// </para>
    /// </remarks>
    public void CopyInheritedFrom(ComputedStyle parent)
    {
        CopyInheritedScalarsFrom(parent);
        StyleParent = parent;

        if (parent.CustomProperties is { Count: > 0 })
        {
            CustomProperties = new Dictionary<string, string>(parent.CustomProperties, StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Inherits the scalar values and links the style parent. Custom
    /// properties are resolved through the parent chain on demand, so this hot
    /// path never copies the inherited variable map.
    /// </summary>
    /// <param name="parent">The style of the element's parent, which must outlive this style.</param>
    /// <remarks>
    /// Because the map is not copied, <see cref="TryGetCustomProperty"/> will
    /// observe later mutations of the parent's declarations.
    /// </remarks>
    internal void CopyInheritedFromShared(ComputedStyle parent)
    {
        CopyInheritedScalarsFrom(parent);
        StyleParent = parent;
    }

    /// <summary>Recomputes <see cref="CustomPropertyHash"/> after custom properties change.</summary>
    /// <remarks>
    /// The computation is O(number of declarations) in this style and assumes
    /// the parent's hash is already current; refresh parents before children
    /// when mutating a chain. Uses ordinary <see cref="System.HashCode"/>
    /// combination, so the result is only meaningful within one process.
    /// </remarks>
    internal void RefreshCustomPropertyHash()
    {
        var parentHash = StyleParent?.CustomPropertyHash ?? 0;

        if (CustomProperties is not { Count: > 0 } properties)
        {
            CustomPropertyHash = parentHash;
            return;
        }

        var hash = new HashCode();
        hash.Add(parentHash);

        foreach (var pair in properties)
        {
            hash.Add(pair.Key, StringComparer.Ordinal);
            hash.Add(pair.Value, StringComparer.Ordinal);
        }

        CustomPropertyHash = hash.ToHashCode();
    }

    /// <summary>
    /// Looks up a custom property declared on this style or any ancestor.
    /// </summary>
    /// <param name="name">The case-sensitive property name, including the leading <c>--</c>.</param>
    /// <param name="value">Receives the declared value when found, otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a declaration exists in the chain.</returns>
    /// <remarks>
    /// The nearest declaration wins and the raw, unsubstituted token string is
    /// returned. The lookup walks parent pointers iteratively; cycle-free chains
    /// are assumed.
    /// </remarks>
    public bool TryGetCustomProperty(string name, out string? value)
    {
        for (var style = this; style is not null; style = style.StyleParent)
        {
            if (style.CustomProperties is { } properties
                && properties.TryGetValue(name, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
    }

    /// <summary>Copies the inherited scalar properties from <paramref name="parent"/>.</summary>
    /// <param name="parent">The parent style to inherit from.</param>
    /// <remarks>
    /// <see cref="OverflowWrap"/> is absent from this list even though CSS
    /// makes <c>overflow-wrap</c> inherited.
    /// </remarks>
    private void CopyInheritedScalarsFrom(ComputedStyle parent)
    {
        Color = parent.Color;
        FontFamily = parent.FontFamily;
        FontSize = parent.FontSize;
        FontWeight = parent.FontWeight;
        Italic = parent.Italic;
        LineHeight = parent.LineHeight;
        LetterSpacing = parent.LetterSpacing;
        TextAlign = parent.TextAlign;
        WhiteSpace = parent.WhiteSpace;
        TextOverflow = parent.TextOverflow;
        TextTransform = parent.TextTransform;
        Direction = parent.Direction;
        WritingMode = parent.WritingMode;
        Visibility = parent.Visibility;
        BorderCollapse = parent.BorderCollapse;
        BorderSpacing = parent.BorderSpacing;
        ObjectFit = parent.ObjectFit;
    }

    /// <summary>Resolves <see cref="FontSize"/> to CSS pixels.</summary>
    /// <param name="context">The length context; its font size is the percentage basis.</param>
    /// <returns>The resolved font size in CSS pixels.</returns>
    /// <remarks>
    /// A percentage is relative to <see cref="LengthContext.FontSize"/>, and
    /// unresolvable units fall back to <c>16</c> px.
    /// </remarks>
    public float ResolveFontSize(in LengthContext context)
    {
        var fontSizeContext = context.WithPercentBasis(context.FontSize);
        return FontSize.ResolveOr(in fontSizeContext, context.FontSize, 16);
    }

    /// <summary>Resolves <see cref="LineHeight"/> to CSS pixels.</summary>
    /// <param name="context">The length context used for the font size and units.</param>
    /// <returns>The line height in CSS pixels.</returns>
    /// <remarks>
    /// A unitless number multiplies the resolved font size. Other units are
    /// resolved with the font size as the percentage basis and fall back to
    /// <c>1.2 x font size</c> when no basis is available.
    /// </remarks>
    public float ResolveLineHeight(in LengthContext context)
    {
        if (LineHeight.Unit == LengthUnit.Number)
        {
            return LineHeight.Value * ResolveFontSize(in context);
        }

        return LineHeight.ResolveOr(in context, ResolveFontSize(in context), ResolveFontSize(in context) * 1.2f);
    }

    /// <summary>Replaces the <see cref="CurrentColor"/> sentinel with <see cref="Color"/>.</summary>
    /// <param name="color">A packed ARGB border colour, possibly <see cref="CurrentColor"/>.</param>
    /// <returns>The concrete ARGB colour.</returns>
    public uint ResolveBorderColor(uint color) => color == CurrentColor ? Color : color;

    /// <summary>Resolves <see cref="OutlineColor"/>, replacing <see cref="CurrentColor"/> with <see cref="Color"/>.</summary>
    /// <returns>The concrete ARGB outline colour.</returns>
    public uint ResolveOutlineColor() => OutlineColor == CurrentColor ? Color : OutlineColor;

    /// <summary>Builds the immutable text style passed to the measurer and stored in the constant pool.</summary>
    /// <param name="context">The length context used to resolve letter spacing and line height.</param>
    /// <param name="fontSize">The already resolved font size in CSS pixels.</param>
    /// <param name="color">The already resolved text colour as packed ARGB.</param>
    /// <returns>A <see cref="TextStyle"/> snapshot of the text-related properties.</returns>
    /// <remarks>
    /// The style does not carry <see cref="TextTransform"/>; callers transform
    /// the string before laying it out. Letter spacing resolves with
    /// <paramref name="fontSize"/> as the percentage basis and defaults to 0.
    /// </remarks>
    public TextStyle ToTextStyle(in LengthContext context, float fontSize, uint color)
    {
        return new TextStyle(
            FontFamily,
            fontSize,
            FontWeight,
            Italic,
            color,
            LetterSpacing.ResolveOr(in context, fontSize, 0),
            ResolveLineHeight(in context),
            TextAlign,
            TextDecoration,
            WhiteSpace,
            Direction);
    }

    /// <summary>
    /// Creates a shallow copy for the animation runtime. Animation tracks only
    /// replace individual computed properties, so the custom-property map and
    /// animation list can stay shared with the base style. Copying the map on
    /// every animated frame is expensive because Bootstrap inherits a large
    /// set of <c>--bs-*</c> variables to every element.
    /// </summary>
    /// <returns>A new style whose reference-typed members alias the original's.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="CustomProperties"/>, <see cref="Animations"/>,
    /// <see cref="BackgroundImages"/>, <see cref="BoxShadows"/>,
    /// <see cref="Filter"/>, <see cref="ClipPath"/>, <see cref="Mask"/>,
    /// <see cref="StyleParent"/> and <see cref="CachedBackgroundSpecs"/> are
    /// all shared. The copy must not mutate any of them.
    /// </para>
    /// <para>
    /// The runtime follows this call with <see cref="CopyAllFrom"/> to replace
    /// the animated values in place.
    /// </para>
    /// </remarks>
    public ComputedStyle CloneForAnimation() => (ComputedStyle)MemberwiseClone();

    /// <summary>Creates a copy whose mutable collections are safe to modify independently.</summary>
    /// <returns>A new style with its own custom-property map and animation list.</returns>
    /// <remarks>
    /// The copy is shallow for every other reference: arrays such as
    /// <see cref="BackgroundImages"/> and <see cref="BoxShadows"/>, the parsed
    /// paint specs and <see cref="StyleParent"/> are shared with the original.
    /// <see cref="CloneForAnimation"/> is cheaper when only scalar values change.
    /// </remarks>
    public ComputedStyle Clone()
    {
        var copy = (ComputedStyle)MemberwiseClone();

        if (CustomProperties is not null)
        {
            copy.CustomProperties = new Dictionary<string, string>(CustomProperties, StringComparer.Ordinal);
        }

        if (Animations is not null)
        {
            copy.Animations = new List<AnimationSpec>(Animations.Count);

            for (var i = 0; i < Animations.Count; i++)
            {
                copy.Animations.Add(Animations[i].Clone());
            }
        }

        return copy;
    }

    /// <summary>Copies every computed field from <paramref name="source"/> into this instance.</summary>
    /// <param name="source">The style to copy; it may be the same instance.</param>
    /// <remarks>
    /// Reference-typed members are copied by reference, not cloned:
    /// <see cref="CustomProperties"/>, <see cref="UnsupportedProperties"/>,
    /// <see cref="Animations"/>, <see cref="BackgroundImages"/>,
    /// <see cref="BoxShadows"/> and the parsed paint specs end up shared.
    /// <see cref="CachedBackgroundSpecs"/> is deliberately not copied.
    /// </remarks>
    public void CopyAllFrom(ComputedStyle source)
    {
        Display = source.Display;
        Position = source.Position;
        Float = source.Float;
        Clear = source.Clear;
        BoxSizing = source.BoxSizing;
        Visibility = source.Visibility;
        HasZIndex = source.HasZIndex;
        ZIndex = source.ZIndex;
        Opacity = source.Opacity;
        Width = source.Width;
        Height = source.Height;
        MinWidth = source.MinWidth;
        MinHeight = source.MinHeight;
        MaxWidth = source.MaxWidth;
        MaxHeight = source.MaxHeight;
        AspectRatio = source.AspectRatio;
        Top = source.Top;
        Right = source.Right;
        Bottom = source.Bottom;
        Left = source.Left;
        Margin = source.Margin;
        Padding = source.Padding;
        BorderWidth = source.BorderWidth;
        BorderStyleTop = source.BorderStyleTop;
        BorderStyleRight = source.BorderStyleRight;
        BorderStyleBottom = source.BorderStyleBottom;
        BorderStyleLeft = source.BorderStyleLeft;
        BorderColorTop = source.BorderColorTop;
        BorderColorRight = source.BorderColorRight;
        BorderColorBottom = source.BorderColorBottom;
        BorderColorLeft = source.BorderColorLeft;
        BorderRadius = source.BorderRadius;
        Color = source.Color;
        BackgroundColor = source.BackgroundColor;
        BackgroundImages = source.BackgroundImages;
        BackgroundWidth = source.BackgroundWidth;
        BackgroundHeight = source.BackgroundHeight;
        BackgroundSizeMode = source.BackgroundSizeMode;
        BackgroundPositionX = source.BackgroundPositionX;
        BackgroundPositionY = source.BackgroundPositionY;
        BackgroundRepeat = source.BackgroundRepeat;
        BackgroundClip = source.BackgroundClip;
        BackgroundOrigin = source.BackgroundOrigin;
        BackgroundFixed = source.BackgroundFixed;
        FontFamily = source.FontFamily;
        FontSize = source.FontSize;
        FontWeight = source.FontWeight;
        Italic = source.Italic;
        LineHeight = source.LineHeight;
        LetterSpacing = source.LetterSpacing;
        TextAlign = source.TextAlign;
        WhiteSpace = source.WhiteSpace;
        TextOverflow = source.TextOverflow;
        TextDecoration = source.TextDecoration;
        TextTransform = source.TextTransform;
        Direction = source.Direction;
        WritingMode = source.WritingMode;
        WebkitLineClamp = source.WebkitLineClamp;
        OverflowWrap = source.OverflowWrap;
        FlexDirection = source.FlexDirection;
        FlexWrap = source.FlexWrap;
        JustifyContent = source.JustifyContent;
        AlignItems = source.AlignItems;
        AlignContent = source.AlignContent;
        AlignSelf = source.AlignSelf;
        FlexGrow = source.FlexGrow;
        FlexShrink = source.FlexShrink;
        FlexBasis = source.FlexBasis;
        Order = source.Order;
        RowGap = source.RowGap;
        ColumnGap = source.ColumnGap;
        GridTemplateColumns = source.GridTemplateColumns;
        GridTemplateRows = source.GridTemplateRows;
        GridAutoColumns = source.GridAutoColumns;
        GridAutoRows = source.GridAutoRows;
        GridAutoFlow = source.GridAutoFlow;
        GridColumn = source.GridColumn;
        GridRow = source.GridRow;
        GridAreaName = source.GridAreaName;
        GridTemplateAreas = source.GridTemplateAreas;
        JustifyItems = source.JustifyItems;
        JustifySelf = source.JustifySelf;
        JustifyItemsBlock = source.JustifyItemsBlock;
        OverflowX = source.OverflowX;
        OverflowY = source.OverflowY;
        ScrollBehavior = source.ScrollBehavior;
        ScrollbarTrackColor = source.ScrollbarTrackColor;
        ScrollbarThumbColor = source.ScrollbarThumbColor;
        ShowScrollbars = source.ShowScrollbars;
        Transform = source.Transform;
        TransformOriginX = source.TransformOriginX;
        TransformOriginY = source.TransformOriginY;
        BoxShadows = source.BoxShadows;
        Filter = source.Filter;
        BackdropFilter = source.BackdropFilter;
        ClipPath = source.ClipPath;
        Mask = source.Mask;
        MixBlendMode = source.MixBlendMode;
        Isolate = source.Isolate;
        OutlineWidth = source.OutlineWidth;
        OutlineStyle = source.OutlineStyle;
        OutlineColor = source.OutlineColor;
        OutlineOffset = source.OutlineOffset;
        ObjectFit = source.ObjectFit;
        TableLayout = source.TableLayout;
        BorderCollapse = source.BorderCollapse;
        BorderSpacing = source.BorderSpacing;
        ColumnCount = source.ColumnCount;
        ColumnGapFallback = source.ColumnGapFallback;
        CustomProperties = source.CustomProperties;
        CustomPropertyHash = source.CustomPropertyHash;
        StyleParent = source.StyleParent;
        UnsupportedProperties = source.UnsupportedProperties;
        Animations = source.Animations;
        IsAnimated = source.IsAnimated;
        UsesViewportHeight = source.UsesViewportHeight;
    }
}

/// <summary>How text is case-transformed before it is laid out.</summary>
/// <remarks>
/// The layout engine stores the value but <see cref="TextLayoutEngine"/> does
/// not apply it; the caller transforms the string first.
/// </remarks>
public enum TextTransform : byte
{
    /// <summary>Use the source text unchanged.</summary>
    None,

    /// <summary>Map every character to its uppercase form.</summary>
    Uppercase,

    /// <summary>Map every character to its lowercase form.</summary>
    Lowercase,

    /// <summary>Uppercase the first letter of each word.</summary>
    Capitalize
}

/// <summary>Controls whether a word that does not fit may be broken mid-word.</summary>
public enum OverflowWrap : byte
{
    /// <summary>Only break at normal opportunities such as spaces and hyphens.</summary>
    Normal,

    /// <summary>Break an otherwise unbreakable word only when it does not fit.</summary>
    BreakWord,

    /// <summary>Allow an additional break opportunity between any two characters.</summary>
    Anywhere
}

/// <summary>Auto-placement direction and packing mode for grid items.</summary>
/// <remarks>The values are flags so <see cref="Dense"/> can be combined with either axis.</remarks>
[Flags]
public enum GridAutoFlow : byte
{
    /// <summary>Fill rows first, adding new rows as needed (value 1).</summary>
    Row = 1,

    /// <summary>Fill columns first, adding new columns as needed (value 2).</summary>
    Column = 2,

    /// <summary>Back-fill earlier holes instead of always appending (value 4).</summary>
    Dense = 4
}

/// <summary>
/// A resolved grid line placement. Zero means auto. Named lines and named grid
/// areas are kept here so the layout pass can resolve them against the
/// container's template.
/// </summary>
/// <param name="Start">The start line number; 0 means auto.</param>
/// <param name="End">The end line number; 0 means auto.</param>
/// <param name="StartAuto">Whether the start line is auto-placed.</param>
/// <param name="EndAuto">Whether the end line is auto-placed.</param>
/// <param name="SpanCount">The explicit span, or 0 when not a <c>span</c> placement.</param>
/// <param name="StartName">The named start line, or <see langword="null"/>.</param>
/// <param name="EndName">The named end line, or <see langword="null"/>.</param>
/// <param name="SpanOnEnd">Whether the span applies to the end side.</param>
/// <remarks>
/// Line numbers are 1-based, as in CSS; the value 0 is reserved for <c>auto</c>.
/// The struct is immutable and is stored by value on
/// <see cref="ComputedStyle.GridColumn"/> and <see cref="ComputedStyle.GridRow"/>.
/// </remarks>
public readonly record struct GridPlacement(
    int Start,
    int End,
    bool StartAuto,
    bool EndAuto,
    int SpanCount = 0,
    string? StartName = null,
    string? EndName = null,
    bool SpanOnEnd = false)
{
    /// <summary>The fully automatic placement: both lines auto and no names.</summary>
    public static readonly GridPlacement Auto = new(0, 0, true, true);

    /// <summary>Gets a value indicating whether both lines are auto and no line names are present.</summary>
    public bool IsAuto => StartAuto && EndAuto && StartName is null && EndName is null;

    /// <summary>
    /// Number of tracks covered by this placement. A span only decides the
    /// size while one side is still auto; once both lines are definite the
    /// distance between them wins, which keeps the value consistent when a
    /// longhand later replaces the side that owned the span.
    /// </summary>
    /// <value>At least 1; the explicit span while a side is auto, otherwise the line distance.</value>
    public int Span
    {
        get
        {
            if (SpanCount > 0 && (StartAuto || EndAuto))
            {
                return SpanCount;
            }

            return EndAuto || StartAuto ? 1 : Math.Max(1, End - Start);
        }
    }

    /// <summary>Gets a value indicating whether either side names a grid line.</summary>
    public bool HasNames => StartName is not null || EndName is not null;
}
