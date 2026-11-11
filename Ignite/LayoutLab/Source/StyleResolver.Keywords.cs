namespace Ignite.LayoutLab;

public sealed partial class StyleResolver
{
    private static readonly ComputedStyle DefaultStyle = new();

    private static bool TryGetWideKeyword(string value, out string keyword)
    {
        var text = value.Trim();

        if (text.Equals("inherit", StringComparison.OrdinalIgnoreCase)
            || text.Equals("initial", StringComparison.OrdinalIgnoreCase)
            || text.Equals("unset", StringComparison.OrdinalIgnoreCase)
            || text.Equals("revert", StringComparison.OrdinalIgnoreCase)
            || text.Equals("revert-layer", StringComparison.OrdinalIgnoreCase))
        {
            keyword = text.ToLowerInvariant();
            return true;
        }

        keyword = string.Empty;
        return false;
    }

    private void ApplyAll(ComputedStyle style, ComputedStyle? parent, string keyword)
    {
        var custom = style.CustomProperties;
        var source = new ComputedStyle();

        if (keyword is "inherit")
        {
            if (parent is not null)
            {
                source = parent.Clone();
            }
        }
        else if (keyword is "unset" or "revert" or "revert-layer")
        {
            if (parent is not null)
            {
                source.CopyInheritedFrom(parent);
            }
        }

        style.CopyAllFrom(source);
        style.CustomProperties = custom;
    }

    private void ApplyWideKeyword(
        ComputedStyle style,
        ComputedStyle? parent,
        CssProperty property,
        string keyword)
    {
        if (keyword is "inherit")
        {
            if (parent is not null)
            {
                CopyProperty(style, parent, property);
            }

            return;
        }

        if (keyword is "initial")
        {
            CopyProperty(style, DefaultStyle, property);
            return;
        }

        // unset / revert / revert-layer
        if (IsInheritedProperty(property) && parent is not null)
        {
            CopyProperty(style, parent, property);
        }
        else
        {
            CopyProperty(style, DefaultStyle, property);
        }
    }

    private static bool IsInheritedProperty(CssProperty property)
    {
        return property is CssProperty.Color
            or CssProperty.FontFamily
            or CssProperty.FontSize
            or CssProperty.FontWeight
            or CssProperty.FontStyle
            or CssProperty.LineHeight
            or CssProperty.LetterSpacing
            or CssProperty.TextAlign
            or CssProperty.TextTransform
            or CssProperty.WhiteSpace
            or CssProperty.WordBreak
            or CssProperty.OverflowWrap
            or CssProperty.Direction
            or CssProperty.WritingMode
            or CssProperty.Visibility
            or CssProperty.BorderCollapse
            or CssProperty.BorderSpacing
            or CssProperty.Cursor
            or CssProperty.ObjectFit;
    }

    private static void CopyProperty(ComputedStyle dest, ComputedStyle source, CssProperty property)
    {
        switch (property)
        {
            case CssProperty.Display: dest.Display = source.Display; break;
            case CssProperty.Position: dest.Position = source.Position; break;
            case CssProperty.Float: dest.Float = source.Float; break;
            case CssProperty.Clear: dest.Clear = source.Clear; break;
            case CssProperty.BoxSizing: dest.BoxSizing = source.BoxSizing; break;
            case CssProperty.Visibility: dest.Visibility = source.Visibility; break;
            case CssProperty.Opacity: dest.Opacity = source.Opacity; break;
            case CssProperty.ZIndex:
                dest.ZIndex = source.ZIndex;
                dest.HasZIndex = source.HasZIndex;
                break;
            case CssProperty.Width: dest.Width = source.Width; break;
            case CssProperty.Height: dest.Height = source.Height; break;
            case CssProperty.MinWidth: dest.MinWidth = source.MinWidth; break;
            case CssProperty.MinHeight: dest.MinHeight = source.MinHeight; break;
            case CssProperty.MaxWidth: dest.MaxWidth = source.MaxWidth; break;
            case CssProperty.MaxHeight: dest.MaxHeight = source.MaxHeight; break;
            case CssProperty.AspectRatio: dest.AspectRatio = source.AspectRatio; break;
            case CssProperty.Top: dest.Top = source.Top; break;
            case CssProperty.Right: dest.Right = source.Right; break;
            case CssProperty.Bottom: dest.Bottom = source.Bottom; break;
            case CssProperty.Left: dest.Left = source.Left; break;
            case CssProperty.Inset:
                dest.Top = source.Top;
                dest.Right = source.Right;
                dest.Bottom = source.Bottom;
                dest.Left = source.Left;
                break;
            case CssProperty.Margin: dest.Margin = source.Margin; break;
            case CssProperty.MarginTop: dest.Margin = dest.Margin.WithTop(source.Margin.Top); break;
            case CssProperty.MarginRight: dest.Margin = dest.Margin.WithRight(source.Margin.Right); break;
            case CssProperty.MarginBottom: dest.Margin = dest.Margin.WithBottom(source.Margin.Bottom); break;
            case CssProperty.MarginLeft: dest.Margin = dest.Margin.WithLeft(source.Margin.Left); break;
            case CssProperty.Padding: dest.Padding = source.Padding; break;
            case CssProperty.PaddingTop: dest.Padding = dest.Padding.WithTop(source.Padding.Top); break;
            case CssProperty.PaddingRight: dest.Padding = dest.Padding.WithRight(source.Padding.Right); break;
            case CssProperty.PaddingBottom: dest.Padding = dest.Padding.WithBottom(source.Padding.Bottom); break;
            case CssProperty.PaddingLeft: dest.Padding = dest.Padding.WithLeft(source.Padding.Left); break;
            case CssProperty.Border:
            case CssProperty.BorderWidth:
                dest.BorderWidth = source.BorderWidth;
                break;
            case CssProperty.BorderStyle:
                dest.BorderStyleTop = source.BorderStyleTop;
                dest.BorderStyleRight = source.BorderStyleRight;
                dest.BorderStyleBottom = source.BorderStyleBottom;
                dest.BorderStyleLeft = source.BorderStyleLeft;
                break;
            case CssProperty.BorderColor:
                dest.BorderColorTop = source.BorderColorTop;
                dest.BorderColorRight = source.BorderColorRight;
                dest.BorderColorBottom = source.BorderColorBottom;
                dest.BorderColorLeft = source.BorderColorLeft;
                break;
            case CssProperty.BorderTop: CopyBorderSide(dest, source, 0); break;
            case CssProperty.BorderRight: CopyBorderSide(dest, source, 1); break;
            case CssProperty.BorderBottom: CopyBorderSide(dest, source, 2); break;
            case CssProperty.BorderLeft: CopyBorderSide(dest, source, 3); break;
            case CssProperty.BorderTopWidth: dest.BorderWidth = dest.BorderWidth.WithTop(source.BorderWidth.Top); break;
            case CssProperty.BorderRightWidth: dest.BorderWidth = dest.BorderWidth.WithRight(source.BorderWidth.Right); break;
            case CssProperty.BorderBottomWidth: dest.BorderWidth = dest.BorderWidth.WithBottom(source.BorderWidth.Bottom); break;
            case CssProperty.BorderLeftWidth: dest.BorderWidth = dest.BorderWidth.WithLeft(source.BorderWidth.Left); break;
            case CssProperty.BorderTopStyle: dest.BorderStyleTop = source.BorderStyleTop; break;
            case CssProperty.BorderRightStyle: dest.BorderStyleRight = source.BorderStyleRight; break;
            case CssProperty.BorderBottomStyle: dest.BorderStyleBottom = source.BorderStyleBottom; break;
            case CssProperty.BorderLeftStyle: dest.BorderStyleLeft = source.BorderStyleLeft; break;
            case CssProperty.BorderTopColor: dest.BorderColorTop = source.BorderColorTop; break;
            case CssProperty.BorderRightColor: dest.BorderColorRight = source.BorderColorRight; break;
            case CssProperty.BorderBottomColor: dest.BorderColorBottom = source.BorderColorBottom; break;
            case CssProperty.BorderLeftColor: dest.BorderColorLeft = source.BorderColorLeft; break;
            case CssProperty.BorderRadius: dest.BorderRadius = source.BorderRadius; break;
            case CssProperty.BorderTopLeftRadius: dest.BorderRadius = dest.BorderRadius with { TopLeft = source.BorderRadius.TopLeft }; break;
            case CssProperty.BorderTopRightRadius: dest.BorderRadius = dest.BorderRadius with { TopRight = source.BorderRadius.TopRight }; break;
            case CssProperty.BorderBottomRightRadius: dest.BorderRadius = dest.BorderRadius with { BottomRight = source.BorderRadius.BottomRight }; break;
            case CssProperty.BorderBottomLeftRadius: dest.BorderRadius = dest.BorderRadius with { BottomLeft = source.BorderRadius.BottomLeft }; break;
            case CssProperty.Background:
                dest.BackgroundColor = source.BackgroundColor;
                dest.BackgroundImages = source.BackgroundImages;
                dest.BackgroundWidth = source.BackgroundWidth;
                dest.BackgroundHeight = source.BackgroundHeight;
                dest.BackgroundSizeMode = source.BackgroundSizeMode;
                dest.BackgroundPositionX = source.BackgroundPositionX;
                dest.BackgroundPositionY = source.BackgroundPositionY;
                dest.BackgroundRepeat = source.BackgroundRepeat;
                dest.BackgroundClip = source.BackgroundClip;
                dest.BackgroundOrigin = source.BackgroundOrigin;
                dest.BackgroundFixed = source.BackgroundFixed;
                break;
            case CssProperty.BackgroundColor: dest.BackgroundColor = source.BackgroundColor; break;
            case CssProperty.BackgroundImage: dest.BackgroundImages = source.BackgroundImages; break;
            case CssProperty.BackgroundSize:
                dest.BackgroundWidth = source.BackgroundWidth;
                dest.BackgroundHeight = source.BackgroundHeight;
                dest.BackgroundSizeMode = source.BackgroundSizeMode;
                break;
            case CssProperty.BackgroundPosition:
                dest.BackgroundPositionX = source.BackgroundPositionX;
                dest.BackgroundPositionY = source.BackgroundPositionY;
                break;
            case CssProperty.BackgroundRepeat: dest.BackgroundRepeat = source.BackgroundRepeat; break;
            case CssProperty.BackgroundClip: dest.BackgroundClip = source.BackgroundClip; break;
            case CssProperty.BackgroundOrigin: dest.BackgroundOrigin = source.BackgroundOrigin; break;
            case CssProperty.BackgroundAttachment: dest.BackgroundFixed = source.BackgroundFixed; break;
            case CssProperty.Color: dest.Color = source.Color; break;
            case CssProperty.Font:
                dest.FontFamily = source.FontFamily;
                dest.FontSize = source.FontSize;
                dest.FontWeight = source.FontWeight;
                dest.Italic = source.Italic;
                dest.LineHeight = source.LineHeight;
                break;
            case CssProperty.FontFamily: dest.FontFamily = source.FontFamily; break;
            case CssProperty.FontSize: dest.FontSize = source.FontSize; break;
            case CssProperty.FontWeight: dest.FontWeight = source.FontWeight; break;
            case CssProperty.FontStyle: dest.Italic = source.Italic; break;
            case CssProperty.LineHeight: dest.LineHeight = source.LineHeight; break;
            case CssProperty.LetterSpacing: dest.LetterSpacing = source.LetterSpacing; break;
            case CssProperty.TextAlign: dest.TextAlign = source.TextAlign; break;
            case CssProperty.TextDecoration:
            case CssProperty.TextDecorationLine:
                dest.TextDecoration = source.TextDecoration;
                break;
            case CssProperty.TextTransform: dest.TextTransform = source.TextTransform; break;
            case CssProperty.WhiteSpace: dest.WhiteSpace = source.WhiteSpace; break;
            case CssProperty.WordBreak:
            case CssProperty.OverflowWrap: dest.OverflowWrap = source.OverflowWrap; break;
            case CssProperty.TextOverflow: dest.TextOverflow = source.TextOverflow; break;
            case CssProperty.Direction: dest.Direction = source.Direction; break;
            case CssProperty.WritingMode: dest.WritingMode = source.WritingMode; break;
            case CssProperty.WebkitLineClamp: dest.WebkitLineClamp = source.WebkitLineClamp; break;
            case CssProperty.Flex:
                dest.FlexGrow = source.FlexGrow;
                dest.FlexShrink = source.FlexShrink;
                dest.FlexBasis = source.FlexBasis;
                break;
            case CssProperty.FlexDirection: dest.FlexDirection = source.FlexDirection; break;
            case CssProperty.FlexWrap: dest.FlexWrap = source.FlexWrap; break;
            case CssProperty.FlexFlow:
                dest.FlexDirection = source.FlexDirection;
                dest.FlexWrap = source.FlexWrap;
                break;
            case CssProperty.JustifyContent: dest.JustifyContent = source.JustifyContent; break;
            case CssProperty.AlignItems: dest.AlignItems = source.AlignItems; break;
            case CssProperty.AlignContent: dest.AlignContent = source.AlignContent; break;
            case CssProperty.AlignSelf: dest.AlignSelf = source.AlignSelf; break;
            case CssProperty.FlexGrow: dest.FlexGrow = source.FlexGrow; break;
            case CssProperty.FlexShrink: dest.FlexShrink = source.FlexShrink; break;
            case CssProperty.FlexBasis: dest.FlexBasis = source.FlexBasis; break;
            case CssProperty.Gap:
            case CssProperty.RowGap:
            case CssProperty.ColumnGap:
                dest.RowGap = source.RowGap;
                dest.ColumnGap = source.ColumnGap;
                break;
            case CssProperty.Order: dest.Order = source.Order; break;
            case CssProperty.Grid:
            case CssProperty.GridTemplate:
                dest.GridTemplateColumns = source.GridTemplateColumns;
                dest.GridTemplateRows = source.GridTemplateRows;
                break;
            case CssProperty.GridTemplateColumns: dest.GridTemplateColumns = source.GridTemplateColumns; break;
            case CssProperty.GridTemplateRows: dest.GridTemplateRows = source.GridTemplateRows; break;
            case CssProperty.GridAutoColumns: dest.GridAutoColumns = source.GridAutoColumns; break;
            case CssProperty.GridAutoRows: dest.GridAutoRows = source.GridAutoRows; break;
            case CssProperty.GridAutoFlow: dest.GridAutoFlow = source.GridAutoFlow; break;
            case CssProperty.GridColumn: dest.GridColumn = source.GridColumn; break;
            case CssProperty.GridRow: dest.GridRow = source.GridRow; break;
            case CssProperty.GridColumnStart: dest.GridColumn = dest.GridColumn with { Start = source.GridColumn.Start, StartAuto = source.GridColumn.StartAuto }; break;
            case CssProperty.GridColumnEnd: dest.GridColumn = dest.GridColumn with { End = source.GridColumn.End, EndAuto = source.GridColumn.EndAuto }; break;
            case CssProperty.GridRowStart: dest.GridRow = dest.GridRow with { Start = source.GridRow.Start, StartAuto = source.GridRow.StartAuto }; break;
            case CssProperty.GridRowEnd: dest.GridRow = dest.GridRow with { End = source.GridRow.End, EndAuto = source.GridRow.EndAuto }; break;
            case CssProperty.GridArea:
                dest.GridColumn = source.GridColumn;
                dest.GridRow = source.GridRow;
                break;
            case CssProperty.JustifyItems: dest.JustifyItems = source.JustifyItems; break;
            case CssProperty.JustifySelf: dest.JustifySelf = source.JustifySelf; break;
            case CssProperty.PlaceItems:
                dest.AlignItems = source.AlignItems;
                dest.JustifyItems = source.JustifyItems;
                break;
            case CssProperty.PlaceContent: dest.AlignContent = source.AlignContent; break;
            case CssProperty.Overflow:
            case CssProperty.OverflowX:
            case CssProperty.OverflowY:
                dest.OverflowX = source.OverflowX;
                dest.OverflowY = source.OverflowY;
                break;
            case CssProperty.Transform: dest.Transform = source.Transform; break;
            case CssProperty.TransformOrigin:
                dest.TransformOriginX = source.TransformOriginX;
                dest.TransformOriginY = source.TransformOriginY;
                break;
            case CssProperty.BoxShadow: dest.BoxShadows = source.BoxShadows; break;
            case CssProperty.Outline:
                dest.OutlineWidth = source.OutlineWidth;
                dest.OutlineStyle = source.OutlineStyle;
                dest.OutlineColor = source.OutlineColor;
                dest.OutlineOffset = source.OutlineOffset;
                break;
            case CssProperty.OutlineWidth: dest.OutlineWidth = source.OutlineWidth; break;
            case CssProperty.OutlineStyle: dest.OutlineStyle = source.OutlineStyle; break;
            case CssProperty.OutlineColor: dest.OutlineColor = source.OutlineColor; break;
            case CssProperty.OutlineOffset: dest.OutlineOffset = source.OutlineOffset; break;
            case CssProperty.Filter: dest.Filter = source.Filter; break;
            case CssProperty.BackdropFilter: dest.BackdropFilter = source.BackdropFilter; break;
            case CssProperty.ClipPath: dest.ClipPath = source.ClipPath; break;
            case CssProperty.Mask: dest.Mask = source.Mask; break;
            case CssProperty.MixBlendMode: dest.MixBlendMode = source.MixBlendMode; break;
            case CssProperty.Isolation: dest.Isolate = source.Isolate; break;
            case CssProperty.GridTemplateAreas: dest.GridTemplateAreas = source.GridTemplateAreas; break;
            case CssProperty.ObjectFit: dest.ObjectFit = source.ObjectFit; break;
            case CssProperty.TableLayout: dest.TableLayout = source.TableLayout; break;
            case CssProperty.BorderCollapse: dest.BorderCollapse = source.BorderCollapse; break;
            case CssProperty.BorderSpacing: dest.BorderSpacing = source.BorderSpacing; break;
            case CssProperty.ColumnCount: dest.ColumnCount = source.ColumnCount; break;
            case CssProperty.ScrollbarColor:
                dest.ScrollbarTrackColor = source.ScrollbarTrackColor;
                dest.ScrollbarThumbColor = source.ScrollbarThumbColor;
                break;
            case CssProperty.ScrollbarWidth: dest.ShowScrollbars = source.ShowScrollbars; break;
            case CssProperty.ScrollBehavior: dest.ScrollBehavior = source.ScrollBehavior; break;
        }
    }

    private static void CopyBorderSide(ComputedStyle dest, ComputedStyle source, int side)
    {
        switch (side)
        {
            case 0:
                dest.BorderWidth = dest.BorderWidth.WithTop(source.BorderWidth.Top);
                dest.BorderStyleTop = source.BorderStyleTop;
                dest.BorderColorTop = source.BorderColorTop;
                break;
            case 1:
                dest.BorderWidth = dest.BorderWidth.WithRight(source.BorderWidth.Right);
                dest.BorderStyleRight = source.BorderStyleRight;
                dest.BorderColorRight = source.BorderColorRight;
                break;
            case 2:
                dest.BorderWidth = dest.BorderWidth.WithBottom(source.BorderWidth.Bottom);
                dest.BorderStyleBottom = source.BorderStyleBottom;
                dest.BorderColorBottom = source.BorderColorBottom;
                break;
            case 3:
                dest.BorderWidth = dest.BorderWidth.WithLeft(source.BorderWidth.Left);
                dest.BorderStyleLeft = source.BorderStyleLeft;
                dest.BorderColorLeft = source.BorderColorLeft;
                break;
        }
    }

    private static Display ParseDisplay(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text == "none") return Display.None;
        if (text == "contents") return Display.Contents;
        if (text == "flow-root") return Display.FlowRoot;
        if (text == "list-item") return Display.ListItem;

        if (text.Contains("inline-block")) return Display.InlineBlock;
        if (text.Contains("inline-flex")) return Display.InlineFlex;
        if (text.Contains("inline-grid")) return Display.InlineGrid;
        if (text.Contains("inline-table")) return Display.InlineTable;
        if (text.Contains("table-row-group")) return Display.TableRowGroup;
        if (text.Contains("table-header-group")) return Display.TableHeaderGroup;
        if (text.Contains("table-footer-group")) return Display.TableFooterGroup;
        if (text.Contains("table-row")) return Display.TableRow;
        if (text.Contains("table-cell")) return Display.TableCell;
        if (text.Contains("table-column-group")) return Display.TableColumnGroup;
        if (text.Contains("table-column")) return Display.TableColumn;
        if (text.Contains("table-caption")) return Display.TableCaption;
        if (text.Contains("flex")) return text.Contains("inline") ? Display.InlineFlex : Display.Flex;
        if (text.Contains("grid")) return text.Contains("inline") ? Display.InlineGrid : Display.Grid;
        if (text.Contains("table")) return Display.Table;
        if (text.Contains("inline")) return Display.Inline;
        if (text.Contains("block")) return Display.Block;
        return Display.Block;
    }

    private static Position ParsePosition(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        return text switch
        {
            "relative" => Position.Relative,
            "absolute" => Position.Absolute,
            "fixed" => Position.Fixed,
            "sticky" => Position.Sticky,
            _ => Position.Static
        };
    }

    private static TextAlign ParseTextAlign(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "left" => TextAlign.Left,
            "right" => TextAlign.Right,
            "center" => TextAlign.Center,
            "justify" => TextAlign.Justify,
            "end" => TextAlign.End,
            "match-parent" => TextAlign.MatchParent,
            _ => TextAlign.Start
        };
    }

    private static TextDecoration ParseTextDecoration(string value)
    {
        var text = value.ToLowerInvariant();
        var decoration = TextDecoration.None;

        if (text.Contains("underline")) decoration |= TextDecoration.Underline;
        if (text.Contains("overline")) decoration |= TextDecoration.Overline;
        if (text.Contains("line-through")) decoration |= TextDecoration.LineThrough;
        if (text.Contains("blink")) decoration |= TextDecoration.Blink;
        return decoration;
    }

    private static WhiteSpace ParseWhiteSpace(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "nowrap" => WhiteSpace.NoWrap,
            "pre" => WhiteSpace.Pre,
            "pre-wrap" => WhiteSpace.PreWrap,
            "pre-line" => WhiteSpace.PreLine,
            "break-spaces" => WhiteSpace.BreakSpaces,
            _ => WhiteSpace.Normal
        };
    }

    private static FlexDirection ParseFlexDirection(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "row-reverse" => FlexDirection.RowReverse,
            "column" => FlexDirection.Column,
            "column-reverse" => FlexDirection.ColumnReverse,
            _ => FlexDirection.Row
        };
    }

    private static FlexWrap ParseFlexWrap(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "wrap" => FlexWrap.Wrap,
            "wrap-reverse" => FlexWrap.WrapReverse,
            _ => FlexWrap.NoWrap
        };
    }

    private static JustifyContent ParseJustifyContent(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "flex-end" => JustifyContent.FlexEnd,
            "center" => JustifyContent.Center,
            "space-between" => JustifyContent.SpaceBetween,
            "space-around" => JustifyContent.SpaceAround,
            "space-evenly" => JustifyContent.SpaceEvenly,
            "start" => JustifyContent.Start,
            "end" => JustifyContent.End,
            "left" => JustifyContent.Left,
            "right" => JustifyContent.Right,
            "stretch" => JustifyContent.Stretch,
            _ => JustifyContent.FlexStart
        };
    }

    private static AlignItems ParseAlignItems(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "flex-start" => AlignItems.FlexStart,
            "flex-end" => AlignItems.FlexEnd,
            "center" => AlignItems.Center,
            "baseline" => AlignItems.Baseline,
            "start" => AlignItems.Start,
            "end" => AlignItems.End,
            "self-start" => AlignItems.SelfStart,
            "self-end" => AlignItems.SelfEnd,
            _ => AlignItems.Stretch
        };
    }

    private static AlignContent ParseAlignContent(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "flex-start" => AlignContent.FlexStart,
            "flex-end" => AlignContent.FlexEnd,
            "center" => AlignContent.Center,
            "space-between" => AlignContent.SpaceBetween,
            "space-around" => AlignContent.SpaceAround,
            "space-evenly" => AlignContent.SpaceEvenly,
            "start" => AlignContent.Start,
            "end" => AlignContent.End,
            _ => AlignContent.Stretch
        };
    }

    private static AlignSelf ParseAlignSelf(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        return text switch
        {
            "stretch" => AlignSelf.Stretch,
            "flex-start" => AlignSelf.FlexStart,
            "flex-end" => AlignSelf.FlexEnd,
            "center" => AlignSelf.Center,
            "baseline" => AlignSelf.Baseline,
            "start" => AlignSelf.Start,
            "end" => AlignSelf.End,
            "self-start" => AlignSelf.SelfStart,
            "self-end" => AlignSelf.SelfEnd,
            _ => AlignSelf.Auto
        };
    }

    private static JustifyItems ParseJustifyItems(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stretch" => JustifyItems.Stretch,
            "start" => JustifyItems.Start,
            "end" => JustifyItems.End,
            "center" => JustifyItems.Center,
            "left" => JustifyItems.Left,
            "right" => JustifyItems.Right,
            "self-start" => JustifyItems.SelfStart,
            "self-end" => JustifyItems.SelfEnd,
            _ => JustifyItems.Normal
        };
    }

    private static JustifySelf ParseJustifySelf(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "stretch" => JustifySelf.Stretch,
            "start" => JustifySelf.Start,
            "end" => JustifySelf.End,
            "center" => JustifySelf.Center,
            "left" => JustifySelf.Left,
            "right" => JustifySelf.Right,
            "self-start" => JustifySelf.SelfStart,
            "self-end" => JustifySelf.SelfEnd,
            "normal" => JustifySelf.Normal,
            _ => JustifySelf.Auto
        };
    }

    private static Overflow ParseOverflow(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "hidden" => Overflow.Hidden,
            "clip" => Overflow.Clip,
            "scroll" => Overflow.Scroll,
            "auto" => Overflow.Auto,
            "overlay" => Overflow.Auto,
            _ => Overflow.Visible
        };
    }

    private static BackgroundRepeat ParseBackgroundRepeat(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "repeat-x" => BackgroundRepeat.RepeatX,
            "repeat-y" => BackgroundRepeat.RepeatY,
            "no-repeat" => BackgroundRepeat.NoRepeat,
            "space" => BackgroundRepeat.Space,
            "round" => BackgroundRepeat.Round,
            _ => BackgroundRepeat.Repeat
        };
    }

    private static BackgroundBox ParseBackgroundBox(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "padding-box" => BackgroundBox.PaddingBox,
            "content-box" => BackgroundBox.ContentBox,
            "text" => BackgroundBox.Text,
            _ => BackgroundBox.BorderBox
        };
    }

    private static ObjectFit ParseObjectFit(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "contain" => ObjectFit.Contain,
            "cover" => ObjectFit.Cover,
            "none" => ObjectFit.None,
            "scale-down" => ObjectFit.ScaleDown,
            _ => ObjectFit.Fill
        };
    }

    private static GridAutoFlow ParseGridAutoFlow(string value)
    {
        var text = value.ToLowerInvariant();
        var flow = text.Contains("column") ? GridAutoFlow.Column : GridAutoFlow.Row;

        if (text.Contains("dense"))
        {
            flow |= GridAutoFlow.Dense;
        }

        return flow;
    }

    private static MixBlendMode ParseMixBlendMode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "multiply" => MixBlendMode.Multiply,
            "screen" => MixBlendMode.Screen,
            "overlay" => MixBlendMode.Overlay,
            "darken" => MixBlendMode.Darken,
            "lighten" => MixBlendMode.Lighten,
            "color-dodge" => MixBlendMode.ColorDodge,
            "color-burn" => MixBlendMode.ColorBurn,
            "hard-light" => MixBlendMode.HardLight,
            "soft-light" => MixBlendMode.SoftLight,
            "difference" => MixBlendMode.Difference,
            "exclusion" => MixBlendMode.Exclusion,
            "hue" => MixBlendMode.Hue,
            "saturation" => MixBlendMode.Saturation,
            "color" => MixBlendMode.Color,
            "luminosity" => MixBlendMode.Luminosity,
            "plus-lighter" => MixBlendMode.PlusLighter,
            _ => MixBlendMode.Normal
        };
    }

    private static int ParseLineNumber(string value)
    {
        var text = value.Trim();

        if (text.StartsWith("span ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..].Trim();
        }

        return int.TryParse(text, out var line) ? line : 0;
    }

    /// <summary>Returns N for <c>span N</c>, or 0 when the value is not a span.</summary>
    private static int ParseSpanCount(string value)
    {
        var text = value.Trim();

        if (!text.StartsWith("span ", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return int.TryParse(text[5..].Trim(), out var span) && span > 0
            ? Math.Min(span, 1024)
            : 0;
    }

    /// <summary>
    /// Applies one <c>grid-row-*</c> / <c>grid-column-*</c> longhand. A span
    /// belongs to the side it is declared on, so overriding that side clears
    /// the span while overriding the opposite side keeps it.
    /// </summary>
    private static GridPlacement ApplyGridLine(GridPlacement current, string value, bool isStart)
    {
        var span = ParseSpanCount(value);

        if (span > 0)
        {
            return isStart
                ? current with
                {
                    Start = 0,
                    StartAuto = true,
                    StartName = null,
                    SpanCount = span,
                    SpanOnEnd = false
                }
                : current with
                {
                    End = 0,
                    EndAuto = true,
                    EndName = null,
                    SpanCount = span,
                    SpanOnEnd = true
                };
        }

        // A span declared on the opposite side survives this new value.
        var keepSpan = current.SpanOnEnd == isStart ? current.SpanCount : 0;
        var spanOnEnd = keepSpan > 0 && current.SpanOnEnd;

        if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            return isStart
                ? current with
                {
                    Start = 0,
                    StartAuto = true,
                    StartName = null,
                    SpanCount = keepSpan,
                    SpanOnEnd = spanOnEnd
                }
                : current with
                {
                    End = 0,
                    EndAuto = true,
                    EndName = null,
                    SpanCount = keepSpan,
                    SpanOnEnd = spanOnEnd
                };
        }

        if (int.TryParse(value.Trim(), out var line))
        {
            return isStart
                ? current with
                {
                    Start = line,
                    StartAuto = false,
                    StartName = null,
                    SpanCount = keepSpan,
                    SpanOnEnd = spanOnEnd
                }
                : current with
                {
                    End = line,
                    EndAuto = false,
                    EndName = null,
                    SpanCount = keepSpan,
                    SpanOnEnd = spanOnEnd
                };
        }

        var name = value.Trim();

        return isStart
            ? current with
            {
                Start = 0,
                StartAuto = false,
                StartName = name,
                SpanCount = keepSpan,
                SpanOnEnd = spanOnEnd
            }
            : current with
            {
                End = 0,
                EndAuto = false,
                EndName = name,
                SpanCount = keepSpan,
                SpanOnEnd = spanOnEnd
            };
    }

    private static int ParseFontWeight(string value)
    {
        var text = value.Trim().ToLowerInvariant();

        return text switch
        {
            "normal" => 400,
            "bold" => 700,
            "bolder" => 700,
            "lighter" => 300,
            _ => (int)Math.Clamp(CssValueParser.ParseNumber(text, 400), 1, 1000)
        };
    }

    private static CssLength ParseLineHeight(string value)
    {
        if (value.Equals("normal", StringComparison.OrdinalIgnoreCase))
        {
            return CssLength.Number(1.2f);
        }

        var tokens = CssValueTokenizer.Tokenize(value);

        if (tokens.Length == 1 && tokens[0].Kind == CssValueTokenKind.Number)
        {
            return CssLength.Number(tokens[0].Number);
        }

        return CssValueParser.ParseLength(value);
    }

    private static bool TryParseBorderStyle(string value, out BorderStyle style)
    {
        switch (value.Trim().ToLowerInvariant())
        {
            case "none": style = BorderStyle.None; return true;
            case "hidden": style = BorderStyle.Hidden; return true;
            case "solid": style = BorderStyle.Solid; return true;
            case "dashed": style = BorderStyle.Dashed; return true;
            case "dotted": style = BorderStyle.Dotted; return true;
            case "double": style = BorderStyle.Double; return true;
            case "groove": style = BorderStyle.Groove; return true;
            case "ridge": style = BorderStyle.Ridge; return true;
            case "inset": style = BorderStyle.Inset; return true;
            case "outset": style = BorderStyle.Outset; return true;
            default: style = BorderStyle.None; return false;
        }
    }

    private static BorderStyle ParseBorderStyle(string value)
    {
        return TryParseBorderStyle(value, out var style) ? style : BorderStyle.None;
    }

    private static BorderStyle4 ParseBorderStyles(string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        BorderStyle At(int index)
        {
            return parts.Count == 0 ? BorderStyle.None : ParseBorderStyle(parts[Math.Min(index, parts.Count - 1)]);
        }

        if (parts.Count == 1)
        {
            var style = ParseBorderStyle(parts[0]);
            return new BorderStyle4(style, style, style, style);
        }

        if (parts.Count == 2)
        {
            var vertical = ParseBorderStyle(parts[0]);
            var horizontal = ParseBorderStyle(parts[1]);
            return new BorderStyle4(vertical, horizontal, vertical, horizontal);
        }

        if (parts.Count == 3)
        {
            return new BorderStyle4(
                ParseBorderStyle(parts[0]),
                ParseBorderStyle(parts[1]),
                ParseBorderStyle(parts[2]),
                ParseBorderStyle(parts[1]));
        }

        return new BorderStyle4(At(0), At(1), At(2), At(3));
    }

    private void ApplyFontSize(
        ComputedStyle style,
        ComputedStyle? parent,
        int valueId,
        string value)
    {
        var parentFont = parent is null
            ? _options.DefaultFontSize
            : parent.ResolveFontSize(_baseContext);

        var text = value.Trim().ToLowerInvariant();

        float pixels;

        switch (text)
        {
            case "medium":
                pixels = 16;
                break;
            case "small":
                pixels = 13;
                break;
            case "x-small":
                pixels = 10;
                break;
            case "xx-small":
                pixels = 9;
                break;
            case "large":
                pixels = 18;
                break;
            case "x-large":
                pixels = 24;
                break;
            case "xx-large":
                pixels = 32;
                break;
            case "smaller":
                pixels = parentFont * 0.8f;
                break;
            case "larger":
                pixels = parentFont * 1.2f;
                break;
            default:
            {
                var length = ParseLengthCached(valueId, value);
                ApplyFontSizeLength(style, parent, length);
                return;
            }
        }

        style.FontSize = CssLength.Px(Math.Max(0, pixels));
    }

    private void ApplyFontSizeLength(ComputedStyle style, ComputedStyle? parent, CssLength length)
    {
        var parentFont = parent is null
            ? _options.DefaultFontSize
            : parent.ResolveFontSize(_baseContext);

        var context = _baseContext with
        {
            FontSize = parentFont,
            PercentBasis = parentFont
        };

        var pixels = length.ResolveOr(in context, parentFont, parentFont);
        style.FontSize = CssLength.Px(Math.Max(0, pixels));
    }

    private void ApplyFontShorthand(
        ComputedStyle style,
        ComputedStyle? parent,
        string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');
        var size = CssLength.Auto;
        var lineHeight = CssLength.Number(1.2f);
        var weight = 400;
        var italic = false;
        var familyStart = -1;

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i].Trim();
            var lower = part.ToLowerInvariant();

            if (lower is "italic" or "oblique")
            {
                italic = true;
                continue;
            }

            if (lower is "bold" or "bolder")
            {
                weight = 700;
                continue;
            }

            if (lower is "normal")
            {
                continue;
            }

            if (size.IsAuto)
            {
                var slash = part.IndexOf('/');

                if (slash >= 0)
                {
                    size = CssValueParser.ParseLength(part[..slash]);
                    lineHeight = ParseLineHeight(part[(slash + 1)..]);
                }
                else
                {
                    var candidate = CssValueParser.ParseLength(part);

                    if (!candidate.IsAuto)
                    {
                        size = candidate;
                        continue;
                    }
                }

                if (!size.IsAuto)
                {
                    continue;
                }
            }

            familyStart = i;
            break;
        }

        if (!size.IsAuto)
        {
            ApplyFontSizeLength(style, parent, size);
        }

        style.FontWeight = weight;
        style.Italic = italic;
        style.LineHeight = lineHeight;

        if (familyStart >= 0)
        {
            style.FontFamily = CssValueParser.Unquote(string.Join(' ', parts.Skip(familyStart)));
        }
    }

    private static void ApplyFlexShorthand(ComputedStyle style, string value)
    {
        var text = value.Trim();

        if (text.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            style.FlexGrow = 0;
            style.FlexShrink = 0;
            style.FlexBasis = CssLength.Auto;
            return;
        }

        if (text.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            style.FlexGrow = 1;
            style.FlexShrink = 1;
            style.FlexBasis = CssLength.Auto;
            return;
        }

        var parts = CssValueParser.SplitTopLevel(text, ' ');
        var numbers = 0;
        var hasBasis = false;

        foreach (var part in parts)
        {
            if (numbers < 2 && float.TryParse(
                    part,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var number))
            {
                if (numbers == 0)
                {
                    style.FlexGrow = Math.Max(0, number);
                }
                else
                {
                    style.FlexShrink = Math.Max(0, number);
                }

                numbers++;
                continue;
            }

            style.FlexBasis = CssValueParser.ParseLength(part);
            hasBasis = true;
        }

        if (numbers == 1)
        {
            style.FlexShrink = 1;
        }

        // CSS's one- and two-number forms imply a zero flex basis. Treating
        // flex: 1 as "basis: auto" makes every sidebar/scroll area shrink the
        // other flex items in the same container.
        if (!hasBasis && numbers is 1 or 2)
        {
            style.FlexBasis = CssLength.Zero;
        }
    }

    private static void ApplyGridTemplate(ComputedStyle style, string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, '/');

        if (parts.Count >= 1)
        {
            style.GridTemplateColumns = GridTrackList.Parse(parts[0]);
        }

        if (parts.Count >= 2)
        {
            style.GridTemplateRows = GridTrackList.Parse(parts[1]);
        }
    }

    private static void ApplyBackgroundShorthand(ComputedStyle style, string value)
    {
        style.BackgroundColor = CssColor.Transparent;
        style.BackgroundImages = null;
        style.BackgroundRepeat = BackgroundRepeat.Repeat;
        style.BackgroundSizeMode = BackgroundSizeMode.Auto;
        style.BackgroundWidth = CssLength.Auto;
        style.BackgroundHeight = CssLength.Auto;
        style.BackgroundPositionX = CssLength.Percent(0);
        style.BackgroundPositionY = CssLength.Percent(0);
        style.BackgroundClip = BackgroundBox.BorderBox;
        style.BackgroundOrigin = BackgroundBox.PaddingBox;
        style.BackgroundFixed = false;

        var parts = CssValueParser.SplitTopLevel(value, ' ');
        var imageParts = new List<string>();
        var remainder = new List<string>();

        foreach (var part in parts)
        {
            if (part.Contains("url(", StringComparison.OrdinalIgnoreCase)
                || part.Contains("gradient(", StringComparison.OrdinalIgnoreCase))
            {
                imageParts.Add(part);
            }
            else if (CssColor.TryParse(part, style.Color, out var color))
            {
                style.BackgroundColor = color;
            }
            else
            {
                remainder.Add(part);
            }
        }

        if (imageParts.Count > 0)
        {
            style.BackgroundImages = ParseBackgroundLayers(
                string.Join(", ", imageParts),
                style.Color);
        }

        for (var i = 0; i < remainder.Count; i++)
        {
            var part = remainder[i];
            var lower = part.ToLowerInvariant();

            if (lower is "no-repeat" or "repeat-x" or "repeat-y" or "repeat" or "space" or "round")
            {
                style.BackgroundRepeat = ParseBackgroundRepeat(part);
            }
            else if (lower is "border-box" or "padding-box" or "content-box" or "text")
            {
                style.BackgroundClip = ParseBackgroundBox(part);
            }
            else if (lower is "scroll" or "fixed" or "local")
            {
                style.BackgroundFixed = lower == "fixed";
            }
            else if (lower is "cover" or "contain")
            {
                style.BackgroundSizeMode = lower == "cover" ? BackgroundSizeMode.Cover : BackgroundSizeMode.Contain;
            }
            else if (part == "/")
            {
                if (i + 1 < remainder.Count)
                {
                    ApplyBackgroundSize(style, remainder[++i]);
                }
            }
            else if (lower is "left" or "right" or "top" or "bottom" or "center"
                     || part.EndsWith('%') || part.EndsWith("px", StringComparison.OrdinalIgnoreCase))
            {
                var position = remainder.Count > i + 1 && !remainder[i + 1].StartsWith('/')
                    ? part + " " + remainder[++i]
                    : part;
                ApplyBackgroundPosition(style, position);
            }
        }
    }

    private static void ApplyBackgroundSize(ComputedStyle style, string value)
    {
        var text = value.Trim().ToLowerInvariant();

        if (text == "cover")
        {
            style.BackgroundSizeMode = BackgroundSizeMode.Cover;
            return;
        }

        if (text == "contain")
        {
            style.BackgroundSizeMode = BackgroundSizeMode.Contain;
            return;
        }

        var parts = CssValueParser.SplitTopLevel(text, ' ');
        style.BackgroundSizeMode = BackgroundSizeMode.Explicit;
        style.BackgroundWidth = parts.Count > 0 ? CssValueParser.ParseLength(parts[0]) : CssLength.Auto;
        style.BackgroundHeight = parts.Count > 1 ? CssValueParser.ParseLength(parts[1]) : CssLength.Auto;
    }

    private static void ApplyBackgroundPosition(ComputedStyle style, string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        static CssLength Component(string part, bool horizontal, CssLength fallback)
        {
            return part.ToLowerInvariant() switch
            {
                "left" or "top" => CssLength.Percent(0),
                "center" => CssLength.Percent(50),
                "right" or "bottom" => CssLength.Percent(100),
                _ => CssValueParser.ParseLength(part)
            };
        }

        style.BackgroundPositionX = parts.Count > 0
            ? Component(parts[0], horizontal: true, CssLength.Percent(0))
            : CssLength.Percent(0);

        style.BackgroundPositionY = parts.Count > 1
            ? Component(parts[1], horizontal: false, CssLength.Percent(50))
            : CssLength.Percent(50);
    }

    private static void ApplyTransformOrigin(ComputedStyle style, string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        static CssLength Component(string part, bool horizontal, CssLength fallback)
        {
            return part.ToLowerInvariant() switch
            {
                "left" or "top" => CssLength.Percent(0),
                "center" => CssLength.Percent(50),
                "right" or "bottom" => CssLength.Percent(100),
                _ => CssValueParser.ParseLength(part)
            };
        }

        style.TransformOriginX = parts.Count > 0
            ? Component(parts[0], horizontal: true, CssLength.Percent(50))
            : CssLength.Percent(50);

        style.TransformOriginY = parts.Count > 1
            ? Component(parts[1], horizontal: false, CssLength.Percent(50))
            : CssLength.Percent(50);
    }

    private void ApplyOutlineShorthand(ComputedStyle style, int valueId, string value)
    {
        foreach (var part in CssValueParser.SplitTopLevel(value, ' '))
        {
            if (TryParseBorderStyle(part, out var borderStyle))
            {
                style.OutlineStyle = borderStyle;
                continue;
            }

            if (CssColor.TryParse(part, style.Color, out var color))
            {
                style.OutlineColor = color;
                continue;
            }

            var length = CssValueParser.ParseLength(part);

            if (!length.IsAuto)
            {
                style.OutlineWidth = length;
            }
        }
    }
}
