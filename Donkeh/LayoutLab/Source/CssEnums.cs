namespace Donkeh.LayoutLab;

/// <summary>The CSS <c>display</c> property, including the table internal roles.</summary>
/// <remarks>
/// The numeric ordinals are an internal implementation detail and are not part
/// of the CSS value space. <see cref="Display.None"/> removes the box and its
/// subtree from layout; table values are produced by the anonymous-table fixup
/// during layout rather than authored directly.
/// </remarks>
public enum Display : byte
{
    /// <summary><c>display: none</c>; the element generates no box.</summary>
    None,
    /// <summary><c>display: block</c>.</summary>
    Block,
    /// <summary><c>display: inline</c>.</summary>
    Inline,
    /// <summary>
    /// <c>display: inline-block</c>; flows inline but establishes a block
    /// formatting context.
    /// </summary>
    InlineBlock,
    /// <summary><c>display: flex</c>.</summary>
    Flex,
    /// <summary><c>display: inline-flex</c>.</summary>
    InlineFlex,
    /// <summary><c>display: grid</c>.</summary>
    Grid,
    /// <summary><c>display: inline-grid</c>.</summary>
    InlineGrid,
    /// <summary><c>display: table</c>.</summary>
    Table,
    /// <summary><c>display: inline-table</c>.</summary>
    InlineTable,
    /// <summary><c>display: table-row-group</c>.</summary>
    TableRowGroup,
    /// <summary><c>display: table-header-group</c>; repeated at the top of fragmented tables.</summary>
    TableHeaderGroup,
    /// <summary><c>display: table-footer-group</c>; repeated at the bottom of fragmented tables.</summary>
    TableFooterGroup,
    /// <summary><c>display: table-row</c>.</summary>
    TableRow,
    /// <summary><c>display: table-cell</c>.</summary>
    TableCell,
    /// <summary><c>display: table-column</c>.</summary>
    TableColumn,
    /// <summary><c>display: table-column-group</c>.</summary>
    TableColumnGroup,
    /// <summary><c>display: table-caption</c>.</summary>
    TableCaption,
    /// <summary><c>display: list-item</c>; generates a principal block box plus a marker.</summary>
    ListItem,
    /// <summary>
    /// <c>display: contents</c>; the box is skipped and its children
    /// participate in the parent.
    /// </summary>
    Contents,
    /// <summary>
    /// <c>display: flow-root</c>; a block box that always establishes a new
    /// formatting context.
    /// </summary>
    FlowRoot
}

/// <summary>The CSS <c>position</c> property.</summary>
public enum Position : byte
{
    /// <summary><c>position: static</c>; normal flow, offsets ignored.</summary>
    Static,
    /// <summary>
    /// <c>position: relative</c>; laid out in flow then offset visually
    /// without affecting siblings.
    /// </summary>
    Relative,
    /// <summary>
    /// <c>position: absolute</c>; removed from flow and positioned against the
    /// nearest positioned ancestor.
    /// </summary>
    Absolute,
    /// <summary>
    /// <c>position: fixed</c>; positioned against the viewport and unaffected
    /// by scrolling.
    /// </summary>
    Fixed,
    /// <summary><c>position: sticky</c>; relative until a scroll threshold is crossed, then fixed.</summary>
    Sticky
}

/// <summary>The CSS <c>float</c> property.</summary>
public enum Float : byte
{
    /// <summary><c>float: none</c>; the box stays in normal flow.</summary>
    None,
    /// <summary>
    /// <c>float: left</c>; the box is placed against the left edge of its block
    /// formatting context.
    /// </summary>
    Left,
    /// <summary>
    /// <c>float: right</c>; the box is placed against the right edge of its
    /// block formatting context.
    /// </summary>
    Right
}

/// <summary>The CSS <c>clear</c> property, expressed as a bit mask of sides to avoid.</summary>
/// <remarks>
/// The flag values are significant: <see cref="Both"/> is the bitwise union of
/// <see cref="Left"/> and <see cref="Right"/>, so the layout pass can test a
/// single bit per side.
/// </remarks>
[Flags]
public enum Clear : byte
{
    /// <summary><c>clear: none</c>; no restriction.</summary>
    None = 0,
    /// <summary><c>clear: left</c>; the box must be below earlier left floats.</summary>
    Left = 1,
    /// <summary><c>clear: right</c>; the box must be below earlier right floats.</summary>
    Right = 2,
    /// <summary><c>clear: both</c>; the box must be below floats on either side.</summary>
    Both = Left | Right
}

/// <summary>The CSS <c>overflow</c> property for one axis, or the shorthand for both.</summary>
public enum Overflow : byte
{
    /// <summary><c>overflow: visible</c>; content may paint outside the box.</summary>
    Visible,
    /// <summary>
    /// <c>overflow: hidden</c>; content is clipped and the box is scrollable
    /// programmatically.
    /// </summary>
    Hidden,
    /// <summary><c>overflow: clip</c>; content is clipped with no scrolling mechanism.</summary>
    Clip,
    /// <summary><c>overflow: scroll</c>; scrollbars are always reserved.</summary>
    Scroll,
    /// <summary><c>overflow: auto</c>; scrollbars appear only when content overflows.</summary>
    Auto
}

/// <summary>The CSS <c>scroll-behavior</c> property.</summary>
public enum ScrollBehavior : byte
{
    /// <summary><c>scroll-behavior: auto</c>; instant scrolling, subject to user preferences.</summary>
    Auto,
    /// <summary><c>scroll-behavior: smooth</c>; animated scrolling.</summary>
    Smooth
}

/// <summary>The CSS <c>box-sizing</c> property, selecting which box the width and height size.</summary>
public enum BoxSizing : byte
{
    /// <summary><c>box-sizing: content-box</c>; width and height exclude padding and border.</summary>
    ContentBox,
    /// <summary><c>box-sizing: border-box</c>; width and height include padding and border.</summary>
    BorderBox
}

/// <summary>The CSS <c>pointer-events</c> property.</summary>
/// <remarks>
/// <see cref="None"/> makes a box transparent to pointer input while leaving
/// painting and layout untouched. It is commonly used for decorative overlays
/// such as floating labels so the control underneath stays clickable.
/// </remarks>
public enum PointerEvents : byte
{
    /// <summary><c>pointer-events: auto</c>; the box receives pointer input.</summary>
    Auto,

    /// <summary><c>pointer-events: none</c>; the box and its subtree are transparent to pointer input.</summary>
    None
}

/// <summary>The CSS <c>visibility</c> property.</summary>
/// <remarks>
/// Unlike <see cref="Display.None"/>, hidden boxes still occupy space; only
/// their painting and hit testing are suppressed.
/// </remarks>
public enum Visibility : byte
{
    /// <summary><c>visibility: visible</c>.</summary>
    Visible,
    /// <summary><c>visibility: hidden</c>; the box keeps its space but is not painted.</summary>
    Hidden,
    /// <summary>
    /// <c>visibility: collapse</c>; for table rows and columns, removes the
    /// track entirely.
    /// </summary>
    Collapse
}

/// <summary>The CSS <c>flex-direction</c> property.</summary>
public enum FlexDirection : byte
{
    /// <summary><c>flex-direction: row</c>; main axis runs inline, left to right.</summary>
    Row,
    /// <summary><c>flex-direction: row-reverse</c>; main axis runs inline, right to left.</summary>
    RowReverse,
    /// <summary><c>flex-direction: column</c>; main axis runs block, top to bottom.</summary>
    Column,
    /// <summary><c>flex-direction: column-reverse</c>; main axis runs block, bottom to top.</summary>
    ColumnReverse
}

/// <summary>The CSS <c>flex-wrap</c> property.</summary>
public enum FlexWrap : byte
{
    /// <summary><c>flex-wrap: nowrap</c>; all items share one line.</summary>
    NoWrap,
    /// <summary><c>flex-wrap: wrap</c>; lines are added in the cross-axis direction.</summary>
    Wrap,
    /// <summary>
    /// <c>flex-wrap: wrap-reverse</c>; lines are added in the opposite
    /// cross-axis direction.
    /// </summary>
    WrapReverse
}

/// <summary>The CSS <c>justify-content</c> property: distribution along the main axis.</summary>
/// <remarks>
/// <see cref="Stretch"/> is only meaningful for grid containers; flex
/// containers treat it as <see cref="FlexStart"/>.
/// </remarks>
public enum JustifyContent : byte
{
    /// <summary><c>justify-content: flex-start</c>.</summary>
    FlexStart,
    /// <summary><c>justify-content: flex-end</c>.</summary>
    FlexEnd,
    /// <summary><c>justify-content: center</c>.</summary>
    Center,
    /// <summary><c>justify-content: space-between</c>; first and last items hug the edges.</summary>
    SpaceBetween,
    /// <summary>
    /// <c>justify-content: space-around</c>; equal space around every item,
    /// halved at the edges.
    /// </summary>
    SpaceAround,
    /// <summary><c>justify-content: space-evenly</c>; equal space between and at the edges.</summary>
    SpaceEvenly,
    /// <summary><c>justify-content: start</c>; writing-mode-relative start edge.</summary>
    Start,
    /// <summary><c>justify-content: end</c>; writing-mode-relative end edge.</summary>
    End,
    /// <summary><c>justify-content: left</c>; physical left edge.</summary>
    Left,
    /// <summary><c>justify-content: right</c>; physical right edge.</summary>
    Right,
    /// <summary><c>justify-content: stretch</c>; grid tracks grow to fill the container.</summary>
    Stretch
}

/// <summary>The CSS <c>align-items</c> property: cross-axis alignment of every item.</summary>
public enum AlignItems : byte
{
    /// <summary><c>align-items: stretch</c>; items fill the cross size of the line.</summary>
    Stretch,
    /// <summary><c>align-items: flex-start</c>.</summary>
    FlexStart,
    /// <summary><c>align-items: flex-end</c>.</summary>
    FlexEnd,
    /// <summary><c>align-items: center</c>.</summary>
    Center,
    /// <summary><c>align-items: baseline</c>; items align on their first baseline.</summary>
    Baseline,
    /// <summary><c>align-items: start</c>; writing-mode-relative start of the cross axis.</summary>
    Start,
    /// <summary><c>align-items: end</c>; writing-mode-relative end of the cross axis.</summary>
    End,
    /// <summary><c>align-items: self-start</c>; the item's own start edge.</summary>
    SelfStart,
    /// <summary><c>align-items: self-end</c>; the item's own end edge.</summary>
    SelfEnd
}

/// <summary>The CSS <c>align-content</c> property: distribution of flex lines or grid tracks.</summary>
public enum AlignContent : byte
{
    /// <summary><c>align-content: stretch</c>; lines grow to fill the container.</summary>
    Stretch,
    /// <summary><c>align-content: flex-start</c>.</summary>
    FlexStart,
    /// <summary><c>align-content: flex-end</c>.</summary>
    FlexEnd,
    /// <summary><c>align-content: center</c>.</summary>
    Center,
    /// <summary><c>align-content: space-between</c>.</summary>
    SpaceBetween,
    /// <summary><c>align-content: space-around</c>.</summary>
    SpaceAround,
    /// <summary><c>align-content: space-evenly</c>.</summary>
    SpaceEvenly,
    /// <summary><c>align-content: start</c>.</summary>
    Start,
    /// <summary><c>align-content: end</c>.</summary>
    End
}

/// <summary>The CSS <c>align-self</c> property, overriding <c>align-items</c> for one item.</summary>
public enum AlignSelf : byte
{
    /// <summary><c>align-self: auto</c>; inherit the parent's <c>align-items</c>.</summary>
    Auto,
    /// <summary><c>align-self: stretch</c>.</summary>
    Stretch,
    /// <summary><c>align-self: flex-start</c>.</summary>
    FlexStart,
    /// <summary><c>align-self: flex-end</c>.</summary>
    FlexEnd,
    /// <summary><c>align-self: center</c>.</summary>
    Center,
    /// <summary><c>align-self: baseline</c>.</summary>
    Baseline,
    /// <summary><c>align-self: start</c>.</summary>
    Start,
    /// <summary><c>align-self: end</c>.</summary>
    End,
    /// <summary><c>align-self: self-start</c>.</summary>
    SelfStart,
    /// <summary><c>align-self: self-end</c>.</summary>
    SelfEnd
}

/// <summary>The CSS <c>justify-items</c> property: default inline-axis alignment of grid items.</summary>
public enum JustifyItems : byte
{
    /// <summary>
    /// <c>justify-items: legacy</c>; the keyword inherited from the legacy
    /// <c>text-align</c> behaviour.
    /// </summary>
    Legacy,
    /// <summary><c>justify-items: normal</c>; behaves as stretch for grid items.</summary>
    Normal,
    /// <summary><c>justify-items: stretch</c>.</summary>
    Stretch,
    /// <summary><c>justify-items: start</c>.</summary>
    Start,
    /// <summary><c>justify-items: end</c>.</summary>
    End,
    /// <summary><c>justify-items: center</c>.</summary>
    Center,
    /// <summary><c>justify-items: left</c>.</summary>
    Left,
    /// <summary><c>justify-items: right</c>.</summary>
    Right,
    /// <summary><c>justify-items: self-start</c>.</summary>
    SelfStart,
    /// <summary><c>justify-items: self-end</c>.</summary>
    SelfEnd
}

/// <summary>
/// The CSS <c>justify-self</c> property, overriding <c>justify-items</c> for
/// one grid item.
/// </summary>
public enum JustifySelf : byte
{
    /// <summary><c>justify-self: auto</c>; inherit the parent's <c>justify-items</c>.</summary>
    Auto,
    /// <summary><c>justify-self: normal</c>.</summary>
    Normal,
    /// <summary><c>justify-self: stretch</c>.</summary>
    Stretch,
    /// <summary><c>justify-self: start</c>.</summary>
    Start,
    /// <summary><c>justify-self: end</c>.</summary>
    End,
    /// <summary><c>justify-self: center</c>.</summary>
    Center,
    /// <summary><c>justify-self: left</c>.</summary>
    Left,
    /// <summary><c>justify-self: right</c>.</summary>
    Right,
    /// <summary><c>justify-self: self-start</c>.</summary>
    SelfStart,
    /// <summary><c>justify-self: self-end</c>.</summary>
    SelfEnd
}

/// <summary>The CSS <c>text-align</c> property.</summary>
public enum TextAlign : byte
{
    /// <summary><c>text-align: start</c>; writing-mode-relative start edge.</summary>
    Start,
    /// <summary><c>text-align: end</c>; writing-mode-relative end edge.</summary>
    End,
    /// <summary><c>text-align: left</c>; physical left edge.</summary>
    Left,
    /// <summary><c>text-align: right</c>; physical right edge.</summary>
    Right,
    /// <summary><c>text-align: center</c>.</summary>
    Center,
    /// <summary><c>text-align: justify</c>; text is stretched to both edges except the last line.</summary>
    Justify,
    /// <summary>
    /// <c>text-align: match-parent</c>; inherit the parent's computed
    /// alignment, resolving <c>start</c>/<c>end</c> against its direction.
    /// </summary>
    MatchParent
}

/// <summary>The CSS <c>white-space</c> property, controlling collapsing and wrapping.</summary>
public enum WhiteSpace : byte
{
    /// <summary><c>white-space: normal</c>; sequences collapse and lines wrap.</summary>
    Normal,
    /// <summary><c>white-space: nowrap</c>; sequences collapse and lines do not wrap.</summary>
    NoWrap,
    /// <summary><c>white-space: pre</c>; whitespace and newlines are preserved; no wrapping.</summary>
    Pre,
    /// <summary><c>white-space: pre-wrap</c>; whitespace is preserved and lines wrap.</summary>
    PreWrap,
    /// <summary>
    /// <c>white-space: pre-line</c>; newlines are preserved, other whitespace
    /// collapses, lines wrap.
    /// </summary>
    PreLine,
    /// <summary>
    /// <c>white-space: break-spaces</c>; like <c>pre-wrap</c>, but trailing
    /// spaces take space and can wrap.
    /// </summary>
    BreakSpaces
}

/// <summary>The CSS <c>text-overflow</c> property for non-wrapping overflow.</summary>
public enum TextOverflow : byte
{
    /// <summary><c>text-overflow: clip</c>; overflow is cut off.</summary>
    Clip,
    /// <summary><c>text-overflow: ellipsis</c>; overflow is replaced by an ellipsis character.</summary>
    Ellipsis
}

/// <summary>The CSS <c>border-style</c> property, also used per side.</summary>
public enum BorderStyle : byte
{
    /// <summary><c>border-style: none</c>; no border is painted and its width computes to zero.</summary>
    None,
    /// <summary>
    /// <c>border-style: hidden</c>; no border, and takes precedence over
    /// adjacent collapsed borders.
    /// </summary>
    Hidden,
    /// <summary><c>border-style: solid</c>.</summary>
    Solid,
    /// <summary><c>border-style: dashed</c>.</summary>
    Dashed,
    /// <summary><c>border-style: dotted</c>.</summary>
    Dotted,
    /// <summary><c>border-style: double</c>; two parallel strokes.</summary>
    Double,
    /// <summary><c>border-style: groove</c>; a carved 3D effect.</summary>
    Groove,
    /// <summary><c>border-style: ridge</c>; an embossed 3D effect.</summary>
    Ridge,
    /// <summary><c>border-style: inset</c>; the box appears pressed in.</summary>
    Inset,
    /// <summary><c>border-style: outset</c>; the box appears raised.</summary>
    Outset
}

/// <summary>The CSS <c>object-fit</c> property for replaced content such as images.</summary>
public enum ObjectFit : byte
{
    /// <summary><c>object-fit: fill</c>; content is stretched to the box, ignoring aspect ratio.</summary>
    Fill,
    /// <summary>
    /// <c>object-fit: contain</c>; content is scaled to fit inside the box,
    /// preserving aspect ratio.
    /// </summary>
    Contain,
    /// <summary>
    /// <c>object-fit: cover</c>; content covers the box, preserving aspect
    /// ratio and cropping overflow.
    /// </summary>
    Cover,
    /// <summary><c>object-fit: none</c>; content keeps its intrinsic size.</summary>
    None,
    /// <summary><c>object-fit: scale-down</c>; the smaller of <c>none</c> and <c>contain</c>.</summary>
    ScaleDown
}

/// <summary>The CSS <c>background-repeat</c> property.</summary>
public enum BackgroundRepeat : byte
{
    /// <summary><c>background-repeat: repeat</c>; tiles on both axes.</summary>
    Repeat,
    /// <summary><c>background-repeat: repeat-x</c>; tiles horizontally only.</summary>
    RepeatX,
    /// <summary><c>background-repeat: repeat-y</c>; tiles vertically only.</summary>
    RepeatY,
    /// <summary><c>background-repeat: no-repeat</c>; a single tile.</summary>
    NoRepeat,
    /// <summary><c>background-repeat: space</c>; tiles are spaced out without clipping.</summary>
    Space,
    /// <summary><c>background-repeat: round</c>; tiles are scaled so a whole number fits.</summary>
    Round
}

/// <summary>The CSS <c>background-clip</c> and <c>background-origin</c> boxes.</summary>
public enum BackgroundBox : byte
{
    /// <summary><c>border-box</c>; the area inside the border edge.</summary>
    BorderBox,
    /// <summary><c>padding-box</c>; the area inside the padding edge.</summary>
    PaddingBox,
    /// <summary><c>content-box</c>; the content area only.</summary>
    ContentBox,
    /// <summary>
    /// <c>text</c>; clips the background to the glyphs (used by
    /// <c>background-clip: text</c>).
    /// </summary>
    Text
}

/// <summary>How a <c>background-size</c> value specifies the image size.</summary>
public enum BackgroundSizeMode : byte
{
    /// <summary><c>auto</c>; use the image's intrinsic size.</summary>
    Auto,
    /// <summary><c>cover</c>; scale to cover the positioning area, cropping if necessary.</summary>
    Cover,
    /// <summary><c>contain</c>; scale to fit inside the positioning area.</summary>
    Contain,
    /// <summary>One or two explicit lengths or percentages.</summary>
    Explicit
}

/// <summary>The kind of <c>background-image</c> value that was parsed.</summary>
public enum ImageKind : byte
{
    /// <summary><c>none</c>; no image.</summary>
    None,
    /// <summary>A <c>url(...)</c> reference.</summary>
    Url,
    /// <summary>A <c>linear-gradient(...)</c>.</summary>
    LinearGradient,
    /// <summary>A <c>radial-gradient(...)</c>.</summary>
    RadialGradient,
    /// <summary>A <c>conic-gradient(...)</c>.</summary>
    ConicGradient
}

/// <summary>The gradient family, used when recording gradient paint data.</summary>
public enum GradientKind : byte
{
    /// <summary>A linear gradient along a direction line.</summary>
    Linear,
    /// <summary>A radial gradient from a centre point.</summary>
    Radial,
    /// <summary>A conic gradient swept around a centre point.</summary>
    Conic
}

/// <summary>The CSS <c>text-decoration-line</c> property, expressed as a bit mask.</summary>
/// <remarks>
/// The flag values are significant and combine: <see cref="Underline"/> is 1,
/// <see cref="Overline"/> 2, <see cref="LineThrough"/> 4 and
/// <see cref="Blink"/> 8. <see cref="None"/> is the empty mask.
/// </remarks>
[Flags]
public enum TextDecoration : byte
{
    /// <summary><c>text-decoration-line: none</c>; no line is drawn.</summary>
    None = 0,
    /// <summary><c>text-decoration-line: underline</c>.</summary>
    Underline = 1,
    /// <summary><c>text-decoration-line: overline</c>.</summary>
    Overline = 2,
    /// <summary><c>text-decoration-line: line-through</c>.</summary>
    LineThrough = 4,
    /// <summary>
    /// <c>text-decoration-line: blink</c>; retained for completeness, though
    /// renderers usually ignore it.
    /// </summary>
    Blink = 8
}

/// <summary>The CSS <c>direction</c> property, used for bidi and logical start/end resolution.</summary>
public enum Direction : byte
{
    /// <summary><c>direction: ltr</c>; inline flow starts on the left.</summary>
    LeftToRight,
    /// <summary><c>direction: rtl</c>; inline flow starts on the right.</summary>
    RightToLeft
}

/// <summary>The CSS <c>writing-mode</c> property.</summary>
public enum WritingMode : byte
{
    /// <summary><c>writing-mode: horizontal-tb</c>; lines stack top to bottom.</summary>
    HorizontalTopToBottom,
    /// <summary><c>writing-mode: vertical-rl</c>; lines stack right to left.</summary>
    VerticalRightToLeft,
    /// <summary><c>writing-mode: vertical-lr</c>; lines stack left to right.</summary>
    VerticalLeftToRight
}

/// <summary>The CSS <c>table-layout</c> property.</summary>
public enum TableLayout : byte
{
    /// <summary><c>table-layout: auto</c>; column widths depend on content.</summary>
    Auto,
    /// <summary>
    /// <c>table-layout: fixed</c>; column widths come from the first row and
    /// explicit widths.
    /// </summary>
    Fixed
}

/// <summary>The CSS <c>border-collapse</c> property.</summary>
public enum BorderCollapse : byte
{
    /// <summary>
    /// <c>border-collapse: separate</c>; each cell paints its own border with
    /// spacing between cells.
    /// </summary>
    Separate,
    /// <summary><c>border-collapse: collapse</c>; adjacent cell borders are merged.</summary>
    Collapse
}

/// <summary>The CSS <c>mix-blend-mode</c> modes. <see cref="Normal"/> is the identity.</summary>
/// <remarks>
/// The mode is applied when the element's group is composited into its
/// backdrop. <see cref="PlusLighter"/> is the additive <c>plus-lighter</c>
/// mode; the remaining values mirror the CSS Color Level 4 blend modes.
/// </remarks>
public enum MixBlendMode : byte
{
    /// <summary><c>normal</c>; source-over, no blending.</summary>
    Normal,
    /// <summary><c>multiply</c>; darkens the backdrop.</summary>
    Multiply,
    /// <summary><c>screen</c>; lightens the backdrop.</summary>
    Screen,
    /// <summary><c>overlay</c>; multiplies or screens depending on the backdrop.</summary>
    Overlay,
    /// <summary><c>darken</c>; keeps the darker of the two colours per channel.</summary>
    Darken,
    /// <summary><c>lighten</c>; keeps the lighter of the two colours per channel.</summary>
    Lighten,
    /// <summary><c>color-dodge</c>.</summary>
    ColorDodge,
    /// <summary><c>color-burn</c>.</summary>
    ColorBurn,
    /// <summary><c>hard-light</c>.</summary>
    HardLight,
    /// <summary><c>soft-light</c>.</summary>
    SoftLight,
    /// <summary><c>difference</c>.</summary>
    Difference,
    /// <summary><c>exclusion</c>.</summary>
    Exclusion,
    /// <summary>
    /// <c>hue</c>; takes the hue of the source with the backdrop's saturation
    /// and luminance.
    /// </summary>
    Hue,
    /// <summary><c>saturation</c>.</summary>
    Saturation,
    /// <summary>
    /// <c>color</c>; takes the hue and saturation of the source with the
    /// backdrop's luminance.
    /// </summary>
    Color,
    /// <summary><c>luminosity</c>.</summary>
    Luminosity,
    /// <summary><c>plus-lighter</c>; additive compositing, useful for cross-fades.</summary>
    PlusLighter
}

/// <summary>Filter functions understood by the layout instruction stream.</summary>
/// <remarks>
/// Each value corresponds to one <c>filter</c> function; parameters such as the
/// radius, amount or shadow are carried alongside the kind in the paint
/// specification rather than in this enum.
/// </remarks>
public enum FilterFunctionKind : byte
{
    /// <summary><c>blur(&lt;length&gt;)</c>; a Gaussian blur.</summary>
    Blur,
    /// <summary><c>brightness(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is neutral.</summary>
    Brightness,
    /// <summary><c>contrast(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is neutral.</summary>
    Contrast,
    /// <summary><c>grayscale(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is fully grey.</summary>
    Grayscale,
    /// <summary><c>invert(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is fully inverted.</summary>
    Invert,
    /// <summary><c>opacity(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is opaque.</summary>
    Opacity,
    /// <summary><c>saturate(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is neutral.</summary>
    Saturate,
    /// <summary><c>sepia(&lt;number&gt;|&lt;percentage&gt;)</c>; 1 or 100% is fully sepia.</summary>
    Sepia,
    /// <summary><c>hue-rotate(&lt;angle&gt;)</c>; the parameter is an angle in degrees.</summary>
    HueRotate,
    /// <summary><c>drop-shadow(&lt;shadow&gt;)</c>; an offset, blur and colour.</summary>
    DropShadow,
    /// <summary><c>url(...)</c>; a reference to an external SVG filter.</summary>
    Url
}

/// <summary>Supported <c>clip-path</c> geometry families.</summary>
public enum ClipPathKind : byte
{
    /// <summary><c>clip-path: none</c>; no clipping.</summary>
    None,
    /// <summary><c>inset()</c>; a rectangle inset from each edge, optionally with rounded corners.</summary>
    Inset,
    /// <summary><c>circle()</c>; a circle with an optional radius and centre.</summary>
    Circle,
    /// <summary><c>ellipse()</c>; an ellipse with optional radii and centre.</summary>
    Ellipse,
    /// <summary><c>polygon()</c>; a closed polygon of explicit vertices.</summary>
    Polygon,
    /// <summary><c>url(...)</c>; an SVG clip path reference.</summary>
    Url
}

/// <summary>
/// The <c>app-region</c> (Chromium <c>-webkit-app-region</c>) value that marks
/// part of the page as a window-drag region.
/// </summary>
public enum AppRegion : byte
{
    /// <summary>No special behaviour; the element is an ordinary hit target.</summary>
    None,
    /// <summary>The element can be dragged to move the native window.</summary>
    Drag,
    /// <summary>
    /// The element is excluded from an enclosing drag region, so it keeps
    /// normal pointer interaction.
    /// </summary>
    NoDrag
}

/// <summary>How a mask image contributes its alpha.</summary>
public enum MaskMode : byte
{
    /// <summary><c>mask-mode: match-source</c>; alpha for images, luminance for SVG masks.</summary>
    MatchSource,
    /// <summary><c>mask-mode: alpha</c>; the mask's alpha channel is used directly.</summary>
    Alpha,
    /// <summary><c>mask-mode: luminance</c>; the mask's luminance is converted to alpha.</summary>
    Luminance
}
