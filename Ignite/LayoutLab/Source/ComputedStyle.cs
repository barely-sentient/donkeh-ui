using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>
/// The resolved, layout-ready style for one element. Values that depend on the
/// containing block (percentages, auto) stay as <see cref="CssLength"/> until a
/// layout pass supplies the right basis.
/// </summary>
public sealed class ComputedStyle
{
    /// <summary>Sentinel for <c>currentColor</c>.</summary>
    public const uint CurrentColor = 0x00000001;

    public Display Display { get; set; } = Display.Block;
    public Position Position { get; set; } = Position.Static;
    public Float Float { get; set; } = Float.None;
    public Clear Clear { get; set; } = Clear.None;
    public BoxSizing BoxSizing { get; set; } = BoxSizing.ContentBox;
    public Visibility Visibility { get; set; } = Visibility.Visible;

    public bool HasZIndex { get; set; }
    public int ZIndex { get; set; }
    public float Opacity { get; set; } = 1;

    public CssLength Width { get; set; } = CssLength.Auto;
    public CssLength Height { get; set; } = CssLength.Auto;
    public CssLength MinWidth { get; set; } = CssLength.Auto;
    public CssLength MinHeight { get; set; } = CssLength.Auto;
    public CssLength MaxWidth { get; set; } = CssLength.Auto;
    public CssLength MaxHeight { get; set; } = CssLength.Auto;
    public float? AspectRatio { get; set; }

    public CssLength Top { get; set; } = CssLength.Auto;
    public CssLength Right { get; set; } = CssLength.Auto;
    public CssLength Bottom { get; set; } = CssLength.Auto;
    public CssLength Left { get; set; } = CssLength.Auto;

    public CssLength4 Margin { get; set; } = CssLength4.Zero;
    public CssLength4 Padding { get; set; } = CssLength4.Zero;
    public CssLength4 BorderWidth { get; set; } = CssLength4.Zero;

    public BorderStyle BorderStyleTop { get; set; } = BorderStyle.None;
    public BorderStyle BorderStyleRight { get; set; } = BorderStyle.None;
    public BorderStyle BorderStyleBottom { get; set; } = BorderStyle.None;
    public BorderStyle BorderStyleLeft { get; set; } = BorderStyle.None;
    public uint BorderColorTop { get; set; } = CurrentColor;
    public uint BorderColorRight { get; set; } = CurrentColor;
    public uint BorderColorBottom { get; set; } = CurrentColor;
    public uint BorderColorLeft { get; set; } = CurrentColor;
    public CssCornerRadius4 BorderRadius { get; set; } = CssCornerRadius4.Zero;

    public uint Color { get; set; } = CssColor.Black;
    public uint BackgroundColor { get; set; } = CssColor.Transparent;
    public BackgroundImageSpec[]? BackgroundImages { get; set; }
    public CssLength BackgroundWidth { get; set; } = CssLength.Auto;
    public CssLength BackgroundHeight { get; set; } = CssLength.Auto;
    public BackgroundSizeMode BackgroundSizeMode { get; set; } = BackgroundSizeMode.Auto;
    public CssLength BackgroundPositionX { get; set; } = CssLength.Percent(0);
    public CssLength BackgroundPositionY { get; set; } = CssLength.Percent(0);
    public BackgroundRepeat BackgroundRepeat { get; set; } = BackgroundRepeat.Repeat;
    public BackgroundBox BackgroundClip { get; set; } = BackgroundBox.BorderBox;
    public BackgroundBox BackgroundOrigin { get; set; } = BackgroundBox.PaddingBox;
    public bool BackgroundFixed { get; set; }

    public string FontFamily { get; set; } = "sans-serif";
    public CssLength FontSize { get; set; } = CssLength.Px(16);
    public int FontWeight { get; set; } = 400;
    public bool Italic { get; set; }
    public CssLength LineHeight { get; set; } = CssLength.Number(1.2f);
    public CssLength LetterSpacing { get; set; } = CssLength.Zero;
    public TextAlign TextAlign { get; set; } = TextAlign.Start;
    public WhiteSpace WhiteSpace { get; set; } = WhiteSpace.Normal;
    public TextOverflow TextOverflow { get; set; } = TextOverflow.Clip;
    public TextDecoration TextDecoration { get; set; } = TextDecoration.None;
    public TextTransform TextTransform { get; set; } = TextTransform.None;
    public Direction Direction { get; set; } = Direction.LeftToRight;
    public WritingMode WritingMode { get; set; } = WritingMode.HorizontalTopToBottom;
    public int WebkitLineClamp { get; set; }
    public OverflowWrap OverflowWrap { get; set; } = OverflowWrap.Normal;

    public FlexDirection FlexDirection { get; set; } = FlexDirection.Row;
    public FlexWrap FlexWrap { get; set; } = FlexWrap.NoWrap;
    public JustifyContent JustifyContent { get; set; } = JustifyContent.FlexStart;
    public AlignItems AlignItems { get; set; } = AlignItems.Stretch;
    public AlignContent AlignContent { get; set; } = AlignContent.Stretch;
    public AlignSelf AlignSelf { get; set; } = AlignSelf.Auto;
    public float FlexGrow { get; set; }
    public float FlexShrink { get; set; } = 1;
    public CssLength FlexBasis { get; set; } = CssLength.Auto;
    public int Order { get; set; }
    public CssLength RowGap { get; set; } = CssLength.Zero;
    public CssLength ColumnGap { get; set; } = CssLength.Zero;

    public GridTrackList? GridTemplateColumns { get; set; }
    public GridTrackList? GridTemplateRows { get; set; }
    public GridTrackList? GridAutoColumns { get; set; }
    public GridTrackList? GridAutoRows { get; set; }
    public GridAutoFlow GridAutoFlow { get; set; } = GridAutoFlow.Row;
    public GridPlacement GridColumn { get; set; } = GridPlacement.Auto;
    public GridPlacement GridRow { get; set; } = GridPlacement.Auto;
    public string? GridAreaName { get; set; }
    public GridTemplateAreas? GridTemplateAreas { get; set; }
    public JustifyItems JustifyItems { get; set; } = JustifyItems.Normal;
    public JustifySelf JustifySelf { get; set; } = JustifySelf.Auto;
    public AlignItems JustifyItemsBlock { get; set; } = AlignItems.Stretch;

    public Overflow OverflowX { get; set; } = Overflow.Visible;
    public Overflow OverflowY { get; set; } = Overflow.Visible;
    public uint ScrollbarTrackColor { get; set; } = 0xFFE0E0E0;
    public uint ScrollbarThumbColor { get; set; } = 0xFF909090;
    public bool ShowScrollbars { get; set; }

    public TransformSpec? Transform { get; set; }
    public CssLength TransformOriginX { get; set; } = CssLength.Percent(50);
    public CssLength TransformOriginY { get; set; } = CssLength.Percent(50);

    public ShadowSpec[]? BoxShadows { get; set; }
    public FilterSpec? Filter { get; set; }
    public FilterSpec? BackdropFilter { get; set; }
    public ClipPathSpec? ClipPath { get; set; }
    public MaskSpec? Mask { get; set; }
    public MixBlendMode MixBlendMode { get; set; } = MixBlendMode.Normal;
    public bool Isolate { get; set; }

    public CssLength OutlineWidth { get; set; } = CssLength.Zero;
    public BorderStyle OutlineStyle { get; set; } = BorderStyle.None;
    public uint OutlineColor { get; set; } = CurrentColor;
    public CssLength OutlineOffset { get; set; } = CssLength.Zero;

    public ObjectFit ObjectFit { get; set; } = ObjectFit.Fill;
    public TableLayout TableLayout { get; set; } = TableLayout.Auto;
    public BorderCollapse BorderCollapse { get; set; } = BorderCollapse.Separate;
    public CssLength BorderSpacing { get; set; } = CssLength.Px(2);
    public int ColumnCount { get; set; } = 1;
    public CssLength ColumnGapFallback { get; set; } = CssLength.Px(16);

    public Dictionary<string, string>? CustomProperties { get; set; }
    public List<int>? UnsupportedProperties { get; set; }

    public bool IsAbsolutelyPositioned => Position is Position.Absolute or Position.Fixed;

    public bool IsFlexContainer => Display is Display.Flex or Display.InlineFlex;

    public bool IsGridContainer => Display is Display.Grid or Display.InlineGrid;

    public bool IsBlockContainer => Display is Display.Block or Display.FlowRoot or Display.ListItem;

    public bool IsInlineLevel => Display is Display.Inline or Display.InlineBlock or Display.InlineFlex
        or Display.InlineGrid or Display.InlineTable;

    public bool IsScrollContainer => OverflowX is Overflow.Hidden or Overflow.Clip or Overflow.Scroll or Overflow.Auto
        || OverflowY is Overflow.Hidden or Overflow.Clip or Overflow.Scroll or Overflow.Auto;

    public bool IsInFlow => Position is not (Position.Absolute or Position.Fixed) && Float is Float.None;

    public void CopyInheritedFrom(ComputedStyle parent)
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

        if (parent.CustomProperties is { Count: > 0 })
        {
            CustomProperties = new Dictionary<string, string>(parent.CustomProperties, StringComparer.Ordinal);
        }
    }

    public float ResolveFontSize(in LengthContext context)
    {
        var fontSizeContext = context.WithPercentBasis(context.FontSize);
        return FontSize.ResolveOr(in fontSizeContext, context.FontSize, 16);
    }

    public float ResolveLineHeight(in LengthContext context)
    {
        if (LineHeight.Unit == LengthUnit.Number)
        {
            return LineHeight.Value * ResolveFontSize(in context);
        }

        return LineHeight.ResolveOr(in context, ResolveFontSize(in context), ResolveFontSize(in context) * 1.2f);
    }

    public uint ResolveBorderColor(uint color) => color == CurrentColor ? Color : color;

    public uint ResolveOutlineColor() => OutlineColor == CurrentColor ? Color : OutlineColor;

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

    public ComputedStyle Clone()
    {
        var copy = (ComputedStyle)MemberwiseClone();

        if (CustomProperties is not null)
        {
            copy.CustomProperties = new Dictionary<string, string>(CustomProperties, StringComparer.Ordinal);
        }

        return copy;
    }

    /// <summary>Copies every computed field from <paramref name="source"/> into this instance.</summary>
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
        UnsupportedProperties = source.UnsupportedProperties;
    }
}

public enum TextTransform : byte
{
    None,
    Uppercase,
    Lowercase,
    Capitalize
}

public enum OverflowWrap : byte
{
    Normal,
    BreakWord,
    Anywhere
}

[Flags]
public enum GridAutoFlow : byte
{
    Row = 1,
    Column = 2,
    Dense = 4
}

/// <summary>
/// A resolved grid line placement. Zero means auto. Named lines and named grid
/// areas are kept here so the layout pass can resolve them against the
/// container's template.
/// </summary>
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
    public static readonly GridPlacement Auto = new(0, 0, true, true);

    public bool IsAuto => StartAuto && EndAuto && StartName is null && EndName is null;

    /// <summary>
    /// Number of tracks covered by this placement. A span only decides the
    /// size while one side is still auto; once both lines are definite the
    /// distance between them wins, which keeps the value consistent when a
    /// longhand later replaces the side that owned the span.
    /// </summary>
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

    public bool HasNames => StartName is not null || EndName is not null;
}
