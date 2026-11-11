namespace Ignite.LayoutLab;

public enum Display : byte
{
    None,
    Block,
    Inline,
    InlineBlock,
    Flex,
    InlineFlex,
    Grid,
    InlineGrid,
    Table,
    InlineTable,
    TableRowGroup,
    TableHeaderGroup,
    TableFooterGroup,
    TableRow,
    TableCell,
    TableColumn,
    TableColumnGroup,
    TableCaption,
    ListItem,
    Contents,
    FlowRoot
}

public enum Position : byte
{
    Static,
    Relative,
    Absolute,
    Fixed,
    Sticky
}

public enum Float : byte
{
    None,
    Left,
    Right
}

[Flags]
public enum Clear : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Both = Left | Right
}

public enum Overflow : byte
{
    Visible,
    Hidden,
    Clip,
    Scroll,
    Auto
}

public enum ScrollBehavior : byte
{
    Auto,
    Smooth
}

public enum BoxSizing : byte
{
    ContentBox,
    BorderBox
}

public enum Visibility : byte
{
    Visible,
    Hidden,
    Collapse
}

public enum FlexDirection : byte
{
    Row,
    RowReverse,
    Column,
    ColumnReverse
}

public enum FlexWrap : byte
{
    NoWrap,
    Wrap,
    WrapReverse
}

public enum JustifyContent : byte
{
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    Start,
    End,
    Left,
    Right,
    Stretch
}

public enum AlignItems : byte
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    Baseline,
    Start,
    End,
    SelfStart,
    SelfEnd
}

public enum AlignContent : byte
{
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    SpaceBetween,
    SpaceAround,
    SpaceEvenly,
    Start,
    End
}

public enum AlignSelf : byte
{
    Auto,
    Stretch,
    FlexStart,
    FlexEnd,
    Center,
    Baseline,
    Start,
    End,
    SelfStart,
    SelfEnd
}

public enum JustifyItems : byte
{
    Legacy,
    Normal,
    Stretch,
    Start,
    End,
    Center,
    Left,
    Right,
    SelfStart,
    SelfEnd
}

public enum JustifySelf : byte
{
    Auto,
    Normal,
    Stretch,
    Start,
    End,
    Center,
    Left,
    Right,
    SelfStart,
    SelfEnd
}

public enum TextAlign : byte
{
    Start,
    End,
    Left,
    Right,
    Center,
    Justify,
    MatchParent
}

public enum WhiteSpace : byte
{
    Normal,
    NoWrap,
    Pre,
    PreWrap,
    PreLine,
    BreakSpaces
}

public enum TextOverflow : byte
{
    Clip,
    Ellipsis
}

public enum BorderStyle : byte
{
    None,
    Hidden,
    Solid,
    Dashed,
    Dotted,
    Double,
    Groove,
    Ridge,
    Inset,
    Outset
}

public enum ObjectFit : byte
{
    Fill,
    Contain,
    Cover,
    None,
    ScaleDown
}

public enum BackgroundRepeat : byte
{
    Repeat,
    RepeatX,
    RepeatY,
    NoRepeat,
    Space,
    Round
}

public enum BackgroundBox : byte
{
    BorderBox,
    PaddingBox,
    ContentBox,
    Text
}

public enum BackgroundSizeMode : byte
{
    Auto,
    Cover,
    Contain,
    Explicit
}

public enum ImageKind : byte
{
    None,
    Url,
    LinearGradient,
    RadialGradient,
    ConicGradient
}

public enum GradientKind : byte
{
    Linear,
    Radial,
    Conic
}

[Flags]
public enum TextDecoration : byte
{
    None = 0,
    Underline = 1,
    Overline = 2,
    LineThrough = 4,
    Blink = 8
}

public enum Direction : byte
{
    LeftToRight,
    RightToLeft
}

public enum WritingMode : byte
{
    HorizontalTopToBottom,
    VerticalRightToLeft,
    VerticalLeftToRight
}

public enum TableLayout : byte
{
    Auto,
    Fixed
}

public enum BorderCollapse : byte
{
    Separate,
    Collapse
}

/// <summary>The CSS <c>mix-blend-mode</c> modes. <see cref="Normal"/> is the identity.</summary>
public enum MixBlendMode : byte
{
    Normal,
    Multiply,
    Screen,
    Overlay,
    Darken,
    Lighten,
    ColorDodge,
    ColorBurn,
    HardLight,
    SoftLight,
    Difference,
    Exclusion,
    Hue,
    Saturation,
    Color,
    Luminosity,
    PlusLighter
}

/// <summary>Filter functions understood by the layout instruction stream.</summary>
public enum FilterFunctionKind : byte
{
    Blur,
    Brightness,
    Contrast,
    Grayscale,
    Invert,
    Opacity,
    Saturate,
    Sepia,
    HueRotate,
    DropShadow,
    Url
}

/// <summary>Supported <c>clip-path</c> geometry families.</summary>
public enum ClipPathKind : byte
{
    None,
    Inset,
    Circle,
    Ellipse,
    Polygon,
    Url
}

/// <summary>How a mask image contributes its alpha.</summary>
public enum MaskMode : byte
{
    MatchSource,
    Alpha,
    Luminance
}
