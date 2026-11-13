namespace Donkeh.LayoutLab;

public sealed partial class StyleResolver
{
    /// <summary>
    /// The CSS initial values for every property, used as the source for
    /// <c>initial</c> and non-inherited <c>unset</c>.
    /// </summary>
    /// <remarks>
    /// A single shared instance is enough because it is only ever read from. It
    /// must never be mutated: doing so would change the initial value for every
    /// subsequent resolution in the process. <c>all</c> uses a fresh
    /// <see cref="ComputedStyle"/> instead because it also resets bookkeeping
    /// fields.
    /// </remarks>
    private static readonly ComputedStyle DefaultStyle = new();

    /// <summary>
    /// Recognises the CSS-wide keywords.
    /// </summary>
    /// <param name="value">The cleaned declaration value.</param>
    /// <param name="keyword">
    /// Receives the lower-case keyword on success, or an empty string otherwise.
    /// </param>
    /// <returns><see langword="true"/> for a CSS-wide keyword.</returns>
    /// <remarks>
    /// The set is <c>inherit</c>, <c>initial</c>, <c>unset</c>, <c>revert</c>
    /// and <c>revert-layer</c>. Matching is case-insensitive and ignores
    /// surrounding whitespace.
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>all</c> property, which resets every non-custom property at
    /// once.
    /// </summary>
    /// <param name="style">The style to reset in place.</param>
    /// <param name="parent">The parent style, or <see langword="null"/> at the root.</param>
    /// <param name="keyword">The CSS-wide keyword from the <c>all</c> declaration.</param>
    /// <remarks>
    /// <para>
    /// <c>inherit</c> copies the parent wholesale, including properties that are
    /// normally non-inherited. <c>initial</c> leaves a fresh initial style.
    /// <c>unset</c>, <c>revert</c> and <c>revert-layer</c> copy only inherited
    /// properties and leave the rest initial.
    /// </para>
    /// <para>
    /// The element's own custom properties and its
    /// <see cref="ComputedStyle.StyleParent"/> link are saved and restored after
    /// the copy: CSS's <c>all</c> does not reset custom properties, and dropping
    /// the link would break <c>var()</c> lookup for descendants.
    /// </para>
    /// <para>
    /// There is one origin in this engine, so <c>revert</c> has nothing to roll
    /// back to and is treated as <c>unset</c>.
    /// </para>
    /// </remarks>
    private void ApplyAll(ComputedStyle style, ComputedStyle? parent, string keyword)
    {
        var custom = style.CustomProperties;
        var styleParent = style.StyleParent;
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
        style.StyleParent = styleParent;
    }

    /// <summary>
    /// Applies a CSS-wide keyword to a single property.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="parent">The parent style, or <see langword="null"/> at the root.</param>
    /// <param name="property">The property named by the declaration.</param>
    /// <param name="keyword">The lower-case keyword.</param>
    /// <remarks>
    /// <c>inherit</c> copies from the parent. <c>initial</c> copies from
    /// <see cref="DefaultStyle"/>. <c>unset</c> chooses between the two based on
    /// <see cref="IsInheritedProperty"/>. <c>revert</c> and <c>revert-layer</c>
    /// behave like <c>unset</c> because this engine models a single origin.
    /// </remarks>
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

    /// <summary>
    /// Determines whether a property inherits by default.
    /// </summary>
    /// <param name="property">The property to test.</param>
    /// <returns><see langword="true"/> for inherited properties.</returns>
    /// <remarks>
    /// This table mirrors the engine's scalar inheritance rules in
    /// <see cref="ComputedStyle"/>; the two must stay in sync. Properties not
    /// listed here are treated as non-inherited, which is the CSS default. Note
    /// that <c>visibility</c> and <c>cursor</c> are inherited while
    /// <c>display</c> is not.
    /// </remarks>
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

    /// <summary>
    /// Copies one property, or all sub-properties of a shorthand, from a source
    /// style.
    /// </summary>
    /// <param name="dest">The style to update.</param>
    /// <param name="source">
    /// The style to read; typically the parent or <see cref="DefaultStyle"/>.
    /// </param>
    /// <param name="property">The property named by the declaration.</param>
    /// <remarks>
    /// <para>
    /// The switch mirrors the property handling in
    /// <see cref="ApplyDeclaration"/>: a shorthand case copies every field the
    /// shorthand would set, while a longhand case copies only its field. This is
    /// what makes <c>margin-top: inherit</c> affect only the top edge while
    /// <c>margin: inherit</c> copies all four.
    /// </para>
    /// <para>
    /// Properties with no case are silently ignored. This method is only used
    /// for CSS-wide keywords; ordinary values never go through it.
    /// </para>
    /// </remarks>
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
            case CssProperty.Cursor: dest.Cursor = source.Cursor; break;
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

    /// <summary>
    /// Copies the width, style and colour of one border side.
    /// </summary>
    /// <param name="dest">The style to update.</param>
    /// <param name="source">The style to read.</param>
    /// <param name="side">0 = top, 1 = right, 2 = bottom, 3 = left.</param>
    /// <remarks>
    /// Out-of-range sides are ignored. Used by the <c>border-&lt;side&gt;</c>
    /// cases of <see cref="CopyProperty"/>.
    /// </remarks>
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

    /// <summary>
    /// Parses a <c>display</c> value, including the two-keyword forms.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>The matching <see cref="Display"/>; <see cref="Display.Block"/>
    /// for anything unrecognised.</returns>
    /// <remarks>
    /// Matching is case-insensitive and uses ordered substring checks because
    /// multi-keyword values such as <c>inline flex</c> are supported. The order
    /// matters: <c>inline-block</c> is tested before <c>inline</c>, and
    /// <c>table-row-group</c> before <c>table-row</c>. A value containing
    /// <c>table</c> but no more specific table keyword becomes
    /// <see cref="Display.Table"/>.
    /// </remarks>
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

    /// <summary>
    /// Parses a <c>position</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>The matching <see cref="Position"/>; <see cref="Position.Static"/>
    /// for <c>static</c> or anything unrecognised.</returns>
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

    /// <summary>
    /// Parses a <c>text-align</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="TextAlign"/>; <see cref="TextAlign.Start"/> as the
    /// initial value for <c>start</c> and unrecognised input. <c>justify-all</c>
    /// is not supported and also becomes <see cref="TextAlign.Start"/>.
    /// </returns>
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

    /// <summary>
    /// Parses the line keywords of <c>text-decoration</c> into flags.
    /// </summary>
    /// <param name="value">The declaration value, possibly multi-keyword.</param>
    /// <returns>
    /// A combination of <see cref="TextDecoration.Underline"/>,
    /// <see cref="TextDecoration.Overline"/>,
    /// <see cref="TextDecoration.LineThrough"/> and
    /// <see cref="TextDecoration.Blink"/>; <see cref="TextDecoration.None"/> when
    /// none are present.
    /// </returns>
    /// <remarks>
    /// Matching is substring-based, so <c>line-through</c> is found in the same
    /// value as a colour or style token. <c>none</c> is not special-cased; it
    /// simply matches nothing.
    /// </remarks>
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

    /// <summary>
    /// Parses a <c>white-space</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="WhiteSpace"/>; <see cref="WhiteSpace.Normal"/> for
    /// <c>normal</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>flex-direction</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="FlexDirection"/>; <see cref="FlexDirection.Row"/>
    /// for <c>row</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>flex-wrap</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="FlexWrap"/>; <see cref="FlexWrap.NoWrap"/> for
    /// <c>nowrap</c> and unrecognised input.
    /// </returns>
    private static FlexWrap ParseFlexWrap(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "wrap" => FlexWrap.Wrap,
            "wrap-reverse" => FlexWrap.WrapReverse,
            _ => FlexWrap.NoWrap
        };
    }

    /// <summary>
    /// Parses a <c>justify-content</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="JustifyContent"/>;
    /// <see cref="JustifyContent.FlexStart"/> as the initial value for
    /// <c>flex-start</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses an <c>align-items</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="AlignItems"/>; <see cref="AlignItems.Stretch"/> as
    /// the initial value for <c>stretch</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses an <c>align-content</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="AlignContent"/>; <see cref="AlignContent.Stretch"/>
    /// as the initial value for <c>stretch</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses an <c>align-self</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="AlignSelf"/>; <see cref="AlignSelf.Auto"/> for
    /// <c>auto</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>justify-items</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="JustifyItems"/>; <see cref="JustifyItems.Normal"/>
    /// for <c>normal</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>justify-self</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="JustifySelf"/>; <see cref="JustifySelf.Auto"/> for
    /// <c>auto</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses one <c>overflow</c> component.
    /// </summary>
    /// <param name="value">The declaration or component value.</param>
    /// <returns>
    /// The matching <see cref="Overflow"/>; <see cref="Overflow.Visible"/> for
    /// <c>visible</c> and unrecognised input. The legacy <c>overlay</c> keyword
    /// maps to <see cref="Overflow.Auto"/>.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>background-repeat</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="BackgroundRepeat"/>; <see cref="BackgroundRepeat.Repeat"/>
    /// for <c>repeat</c> and unrecognised input. The two-axis
    /// <c>repeat no-repeat</c> form is not represented.
    /// </returns>
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

    /// <summary>
    /// Parses a background clipping/origin box keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="BackgroundBox"/>; <see cref="BackgroundBox.BorderBox"/>
    /// for <c>border-box</c> and unrecognised input. <c>text</c> is supported for
    /// clipped text backgrounds.
    /// </returns>
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

    /// <summary>
    /// Parses an <c>object-fit</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="ObjectFit"/>; <see cref="ObjectFit.Fill"/> as the
    /// initial value for <c>fill</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Parses a <c>cursor</c> keyword, including the legacy <c>hand</c> alias.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <param name="cursor">Receives the matching cursor when the value is valid.</param>
    /// <returns><see langword="true"/> when the value names a known cursor keyword.</returns>
    /// <remarks>
    /// <para>
    /// A comma-separated fallback list such as <c>url(hand.cur), pointer</c> is
    /// reduced to its final segment, because custom images are not supported and
    /// the generic keyword is the mandatory last entry.
    /// </para>
    /// <para>
    /// Unrecognised values return <see langword="false"/> so the caller can leave
    /// the inherited value in place, matching CSS's rule for invalid
    /// declarations.
    /// </para>
    /// </remarks>
    private static bool TryParseCursor(string value, out Cursor cursor)
    {
        value = value.Trim();

        var comma = value.LastIndexOf(',');

        if (comma >= 0)
        {
            value = value[(comma + 1)..].Trim();
        }

        switch (value.ToLowerInvariant())
        {
            case "auto": cursor = Cursor.Auto; return true;
            case "default": cursor = Cursor.Default; return true;
            case "none": cursor = Cursor.None; return true;
            case "context-menu": cursor = Cursor.ContextMenu; return true;
            case "help": cursor = Cursor.Help; return true;
            case "pointer":
            case "hand": cursor = Cursor.Pointer; return true;
            case "progress": cursor = Cursor.Progress; return true;
            case "wait": cursor = Cursor.Wait; return true;
            case "cell": cursor = Cursor.Cell; return true;
            case "crosshair": cursor = Cursor.Crosshair; return true;
            case "text": cursor = Cursor.Text; return true;
            case "vertical-text": cursor = Cursor.VerticalText; return true;
            case "alias": cursor = Cursor.Alias; return true;
            case "copy": cursor = Cursor.Copy; return true;
            case "move": cursor = Cursor.Move; return true;
            case "no-drop": cursor = Cursor.NoDrop; return true;
            case "not-allowed": cursor = Cursor.NotAllowed; return true;
            case "grab": cursor = Cursor.Grab; return true;
            case "grabbing": cursor = Cursor.Grabbing; return true;
            case "all-scroll": cursor = Cursor.AllScroll; return true;
            case "zoom-in": cursor = Cursor.ZoomIn; return true;
            case "zoom-out": cursor = Cursor.ZoomOut; return true;
            case "e-resize": cursor = Cursor.EResize; return true;
            case "n-resize": cursor = Cursor.NResize; return true;
            case "ne-resize": cursor = Cursor.NeResize; return true;
            case "nw-resize": cursor = Cursor.NwResize; return true;
            case "s-resize": cursor = Cursor.SResize; return true;
            case "se-resize": cursor = Cursor.SeResize; return true;
            case "sw-resize": cursor = Cursor.SwResize; return true;
            case "w-resize": cursor = Cursor.WResize; return true;
            case "ew-resize": cursor = Cursor.EwResize; return true;
            case "ns-resize": cursor = Cursor.NsResize; return true;
            case "nesw-resize": cursor = Cursor.NeswResize; return true;
            case "nwse-resize": cursor = Cursor.NwseResize; return true;
            case "col-resize": cursor = Cursor.ColResize; return true;
            case "row-resize": cursor = Cursor.RowResize; return true;
            default:
                cursor = Cursor.Auto;
                return false;
        }
    }

    /// <summary>
    /// Parses a <c>grid-auto-flow</c> value into flags.
    /// </summary>
    /// <param name="value">The declaration value, such as <c>row dense</c>.</param>
    /// <returns>
    /// <see cref="GridAutoFlow.Row"/> or <see cref="GridAutoFlow.Column"/>, plus
    /// <see cref="GridAutoFlow.Dense"/> when requested.
    /// </returns>
    /// <remarks>
    /// Substring matching makes <c>column</c> and <c>dense</c> order-independent.
    /// The default is <c>row</c> when neither axis keyword is present.
    /// </remarks>
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

    /// <summary>Parses an <c>app-region</c> keyword.</summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// <see cref="AppRegion.Drag"/> for <c>drag</c>, <see cref="AppRegion.NoDrag"/>
    /// for <c>no-drag</c>, and <see cref="AppRegion.None"/> for anything else.
    /// </returns>
    private static AppRegion ParseAppRegion(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "drag" => AppRegion.Drag,
            "no-drag" => AppRegion.NoDrag,
            _ => AppRegion.None
        };
    }

    /// <summary>
    /// Parses a <c>mix-blend-mode</c> keyword.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>
    /// The matching <see cref="MixBlendMode"/>; <see cref="MixBlendMode.Normal"/>
    /// for <c>normal</c> and unrecognised input.
    /// </returns>
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

    /// <summary>
    /// Extracts the line number from a grid line value, ignoring a leading
    /// <c>span</c>.
    /// </summary>
    /// <param name="value">The line value.</param>
    /// <returns>The parsed integer, or 0 when no integer is present.</returns>
    /// <remarks>
    /// This helper is currently unused by the cascade. It does not distinguish
    /// a bare <c>span 2</c> from a line number 2; callers that need that
    /// distinction use <see cref="ParseSpanCount"/> and
    /// <see cref="ApplyGridLine"/> instead.
    /// </remarks>
    private static int ParseLineNumber(string value)
    {
        var text = value.Trim();

        if (text.StartsWith("span ", StringComparison.OrdinalIgnoreCase))
        {
            text = text[5..].Trim();
        }

        return int.TryParse(text, out var line) ? line : 0;
    }

    /// <summary>
    /// Returns N for <c>span N</c>, or 0 when the value is not a span.
    /// </summary>
    /// <param name="value">The grid line value.</param>
    /// <returns>
    /// A positive span count clamped to 1024, or 0 for a non-span or a
    /// non-positive/invalid count.
    /// </returns>
    /// <remarks>
    /// The clamp bounds the work a hostile stylesheet can request from the grid
    /// layout pass.
    /// </remarks>
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
    /// <param name="current">The placement accumulated from weaker declarations.</param>
    /// <param name="value">
    /// The longhand value: a line number, a name, <c>auto</c> or <c>span N</c>.
    /// </param>
    /// <param name="isStart">
    /// <see langword="true"/> for <c>*-start</c>; <see langword="false"/> for
    /// <c>*-end</c>.
    /// </param>
    /// <returns>The updated placement.</returns>
    /// <remarks>
    /// A span sets the owning side to auto and records
    /// <see cref="GridPlacement.SpanCount"/> plus which side owns it. Later
    /// longhand values on the opposite side preserve that span; values on the
    /// same side clear it. A defined line clears the corresponding name.
    /// </remarks>
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

    /// <summary>
    /// Parses a <c>font-weight</c> value.
    /// </summary>
    /// <param name="value">A keyword or numeric weight.</param>
    /// <returns>
    /// 400 for <c>normal</c>, 700 for <c>bold</c> and <c>bolder</c>, 300 for
    /// <c>lighter</c>, or the numeric value clamped to 1-1000. Unparsable text
    /// becomes 400.
    /// </returns>
    /// <remarks>
    /// <c>bolder</c> and <c>lighter</c> use fixed weights rather than being
    /// relative to the parent, so nested bolding does not progressively increase.
    /// </remarks>
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

    /// <summary>
    /// Parses a <c>line-height</c> value.
    /// </summary>
    /// <param name="value">A keyword, number or length.</param>
    /// <returns>
    /// 1.2 for <c>normal</c>; a <see cref="LengthUnit.Number"/> for a unitless
    /// value (multiplied by font size at use time); otherwise the parsed length.
    /// </returns>
    /// <remarks>
    /// Keeping a unitless value as a number preserves CSS's inheritance rule:
    /// descendants multiply their own font size rather than inheriting a
    /// precomputed pixel value.
    /// </remarks>
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

    /// <summary>
    /// Recognises one <c>border-style</c> keyword.
    /// </summary>
    /// <param name="value">The keyword to test.</param>
    /// <param name="style">Receives the matching style, or
    /// <see cref="BorderStyle.None"/> on failure.</param>
    /// <returns><see langword="true"/> when the keyword is recognised.</returns>
    /// <remarks>
    /// Matching is case-insensitive and covers all CSS border styles plus
    /// <c>hidden</c>.
    /// </remarks>
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

    /// <summary>
    /// Parses one <c>border-style</c> keyword, defaulting to
    /// <see cref="BorderStyle.None"/>.
    /// </summary>
    /// <param name="value">The keyword to parse.</param>
    /// <returns>The matching style, or <see cref="BorderStyle.None"/>.</returns>
    private static BorderStyle ParseBorderStyle(string value)
    {
        return TryParseBorderStyle(value, out var style) ? style : BorderStyle.None;
    }

    /// <summary>
    /// Parses the one-to-four keyword form of <c>border-style</c>.
    /// </summary>
    /// <param name="value">The declaration value.</param>
    /// <returns>Top, right, bottom and left styles.</returns>
    /// <remarks>
    /// Expansion follows CSS: one value sets all sides, two set
    /// vertical/horizontal, three set top/horizontal/bottom, and four set
    /// top/right/bottom/left. Unrecognised keywords become
    /// <see cref="BorderStyle.None"/>, which also makes that side invisible.
    /// </remarks>
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

    /// <summary>
    /// Applies <c>font-size</c>, resolving keyword and relative sizes.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="parent">The parent style, or <see langword="null"/> at the root.</param>
    /// <param name="valueId">The interned value id, used by the length cache.</param>
    /// <param name="value">The cleaned, variable-substituted value.</param>
    /// <remarks>
    /// <para>
    /// Absolute keywords use fixed CSS pixel sizes: <c>medium</c> 16,
    /// <c>small</c> 13, <c>x-small</c> 10, <c>xx-small</c> 9, <c>large</c> 18,
    /// <c>x-large</c> 24 and <c>xx-large</c> 32. <c>smaller</c> and
    /// <c>larger</c> multiply the parent font size by 0.8 and 1.2.
    /// </para>
    /// <para>
    /// With no parent, <see cref="LayoutOptions.DefaultFontSize"/> is the basis.
    /// The stored value is always absolute pixels; negative results are clamped
    /// to zero.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Resolves a font-size length against the parent font size and stores it in
    /// pixels.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="parent">The parent style, or <see langword="null"/> at the root.</param>
    /// <param name="length">The length from <c>font-size</c> or the <c>font</c> shorthand.</param>
    /// <remarks>
    /// <para>
    /// A percentage resolves against the parent font size, while <c>em</c>
    /// resolves against <see cref="LengthContext.FontSize"/>, which is also set
    /// to the parent font size. This makes <c>font-size: 150%</c> and
    /// <c>font-size: 1.5em</c> equivalent.
    /// </para>
    /// <para>
    /// The <c>rem</c> basis still comes from <see cref="_baseContext"/>, so it
    /// refers to the root font size rather than the parent.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>font</c> shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="parent">The parent style, used to resolve relative sizes.</param>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <remarks>
    /// <para>
    /// Tokens before the size set <c>font-style</c> (<c>italic</c>/<c>oblique</c>)
    /// and <c>font-weight</c> (<c>bold</c>/<c>bolder</c>); <c>normal</c> is
    /// ignored. The first token that parses as a non-auto length is the size, and
    /// a <c>/line-height</c> suffix on that token is parsed too. The remaining
    /// tokens, including quoted family names, are joined as <c>font-family</c>;
    /// surrounding quotes are removed only when the whole joined string is
    /// quoted.
    /// </para>
    /// <para>
    /// Weight, italic and line height are always assigned, even when no size was
    /// found. This means an invalid shorthand still resets those longhands, and
    /// it does not inherit the parent's values.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>flex</c> shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <remarks>
    /// <para>
    /// <c>none</c> becomes <c>0 0 auto</c>; <c>auto</c> becomes
    /// <c>1 1 auto</c>. Otherwise the first one or two unitless numbers are grow
    /// and shrink, and the remaining token is the basis.
    /// </para>
    /// <para>
    /// A single number implies a shrink factor of 1. When numbers are present but
    /// no basis token is, the basis is set to zero, matching the CSS
    /// <c>flex: &lt;number&gt;</c> form; this deliberately prevents
    /// <c>flex: 1</c> from keeping an auto basis that would collapse sibling
    /// scroll areas.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>grid</c> / <c>grid-template</c> shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <remarks>
    /// Splits at the top-level slash: the first part is the column template and
    /// the second is the row template. Extra parts, such as an area declaration,
    /// are ignored. A missing row template leaves the previous rows untouched.
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>background</c> shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <remarks>
    /// <para>
    /// Every background longhand is first reset to its initial value, as CSS
    /// requires of a shorthand. Tokens are then classified into image layers
    /// (<c>url()</c>/gradients), a colour, and a remainder interpreted as repeat,
    /// clip, attachment, size, or position.
    /// </para>
    /// <para>
    /// Only the last colour is kept and all image tokens are joined into one
    /// comma-separated image list, so per-layer colours and sizes are not
    /// modelled. A <c>/</c> token applies the following token as
    /// <c>background-size</c>. Box keywords set the clip box; origin is left at
    /// its padding-box default.
    /// </para>
    /// </remarks>
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
                    var size = remainder[++i];

                    if (i + 1 < remainder.Count && IsBackgroundSizeToken(remainder[i + 1]))
                    {
                        size += " " + remainder[++i];
                    }

                    ApplyBackgroundSize(style, size);
                }
            }
            else if (part.Contains('/'))
            {
                // The compact position/size form, e.g. center/1em or 50%/cover.
                var slash = part.IndexOf('/');
                var position = part[..slash];
                var size = part[(slash + 1)..];

                if (position.Length > 0)
                {
                    ApplyBackgroundPosition(style, position);
                }

                if (i + 1 < remainder.Count && IsBackgroundSizeToken(remainder[i + 1]))
                {
                    size += " " + remainder[++i];
                }

                if (size.Length > 0)
                {
                    ApplyBackgroundSize(style, size);
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

    /// <summary>Returns whether a shorthand token can be the second background-size value.</summary>
    /// <param name="token">Lower-case-ready token from a background shorthand.</param>
    /// <returns><see langword="true"/> for <c>auto</c>, keywords and length units.</returns>
    private static bool IsBackgroundSizeToken(string token)
    {
        var lower = token.ToLowerInvariant();

        return lower is "auto" or "cover" or "contain"
            || lower.EndsWith('%')
            || lower.EndsWith("px", StringComparison.Ordinal)
            || lower.EndsWith("em", StringComparison.Ordinal)
            || lower.EndsWith("rem", StringComparison.Ordinal)
            || lower.EndsWith("vh", StringComparison.Ordinal)
            || lower.EndsWith("vw", StringComparison.Ordinal)
            || lower.EndsWith("vmin", StringComparison.Ordinal)
            || lower.EndsWith("vmax", StringComparison.Ordinal)
            || lower.EndsWith("pt", StringComparison.Ordinal)
            || lower.EndsWith("cm", StringComparison.Ordinal)
            || lower.EndsWith("mm", StringComparison.Ordinal)
            || lower.EndsWith("in", StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies <c>background-size</c> or the size part of the background
    /// shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The size value.</param>
    /// <remarks>
    /// <c>cover</c> and <c>contain</c> set the corresponding mode. Any other
    /// value switches to explicit sizing and parses one or two lengths; a missing
    /// height stays <see cref="CssLength.Auto"/>.
    /// </remarks>
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

    /// <summary>
    /// Applies <c>background-position</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The position value.</param>
    /// <remarks>
    /// One- to four-value forms are supported. Keywords map to percentages:
    /// left/top 0%, center 50%, right/bottom 100%. When the first value is a
    /// vertical keyword the pair is swapped, so <c>top center</c> works. The
    /// three- and four-value edge-offset forms (<c>right 0.75rem center</c>) are
    /// represented as <c>calc(100% - offset)</c> on the relevant axis.
    /// </remarks>
    private static void ApplyBackgroundPosition(ComputedStyle style, string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        static bool IsHorizontal(string part) =>
            part.Equals("left", StringComparison.OrdinalIgnoreCase)
            || part.Equals("right", StringComparison.OrdinalIgnoreCase);

        static bool IsVertical(string part) =>
            part.Equals("top", StringComparison.OrdinalIgnoreCase)
            || part.Equals("bottom", StringComparison.OrdinalIgnoreCase);

        static CssLength Component(string part)
        {
            return part.ToLowerInvariant() switch
            {
                "left" or "top" => CssLength.Percent(0),
                "center" => CssLength.Percent(50),
                "right" or "bottom" => CssLength.Percent(100),
                _ => CssValueParser.ParseLength(part)
            };
        }

        static CssLength EdgeOffset(string edge, CssLength offset)
        {
            if (!edge.Equals("right", StringComparison.OrdinalIgnoreCase)
                && !edge.Equals("bottom", StringComparison.OrdinalIgnoreCase))
            {
                return offset;
            }

            // right 0.75rem == calc(100% - 0.75rem) against the free space.
            return new CssLength(
                0,
                LengthUnit.Number,
                new CssBinaryExpression(
                    '-',
                    new CssLiteralExpression(CssLength.Percent(100)),
                    new CssLiteralExpression(offset)));
        }

        if (parts.Count == 0)
        {
            style.BackgroundPositionX = CssLength.Percent(0);
            style.BackgroundPositionY = CssLength.Percent(50);
            return;
        }

        if (parts.Count == 3
            && IsHorizontal(parts[0])
            && (parts[2].Equals("center", StringComparison.OrdinalIgnoreCase)
                || IsVertical(parts[2])))
        {
            style.BackgroundPositionX = EdgeOffset(parts[0], CssValueParser.ParseLength(parts[1]));
            style.BackgroundPositionY = Component(parts[2]);
            return;
        }

        if (parts.Count == 3
            && IsVertical(parts[0])
            && (parts[2].Equals("center", StringComparison.OrdinalIgnoreCase)
                || IsHorizontal(parts[2])))
        {
            style.BackgroundPositionY = EdgeOffset(parts[0], CssValueParser.ParseLength(parts[1]));
            style.BackgroundPositionX = Component(parts[2]);
            return;
        }

        if (parts.Count >= 4)
        {
            if (IsHorizontal(parts[0]) && IsVertical(parts[2]))
            {
                style.BackgroundPositionX = EdgeOffset(parts[0], CssValueParser.ParseLength(parts[1]));
                style.BackgroundPositionY = EdgeOffset(parts[2], CssValueParser.ParseLength(parts[3]));
                return;
            }

            if (IsVertical(parts[0]) && IsHorizontal(parts[2]))
            {
                style.BackgroundPositionY = EdgeOffset(parts[0], CssValueParser.ParseLength(parts[1]));
                style.BackgroundPositionX = EdgeOffset(parts[2], CssValueParser.ParseLength(parts[3]));
                return;
            }
        }

        if (parts.Count > 1 && IsVertical(parts[0]) && IsHorizontal(parts[1]))
        {
            style.BackgroundPositionY = Component(parts[0]);
            style.BackgroundPositionX = Component(parts[1]);
            return;
        }

        style.BackgroundPositionX = Component(parts[0]);
        style.BackgroundPositionY = parts.Count > 1 ? Component(parts[1]) : CssLength.Percent(50);
    }

    /// <summary>
    /// Applies <c>transform-origin</c>.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="value">The origin value.</param>
    /// <remarks>
    /// One or two components are supported, with keywords mapped to 0%, 50% and
    /// 100%. The default for both axes is 50%, and the z-offset form is ignored.
    /// Percentages are resolved against the element's border box by the painter.
    /// </remarks>
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

    /// <summary>
    /// Applies the <c>outline</c> shorthand.
    /// </summary>
    /// <param name="style">The style being updated.</param>
    /// <param name="valueId">
    /// The interned value id. It is accepted for symmetry with other declarative
    /// handlers; this implementation parses directly and does not cache.
    /// </param>
    /// <param name="value">The cleaned, variable-substituted shorthand.</param>
    /// <remarks>
    /// Order-insensitive: a border-style keyword sets the outline style, a colour
    /// sets the outline colour, and a non-auto length sets the width. The outline
    /// offset is not part of the shorthand and is left unchanged.
    /// </remarks>
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
