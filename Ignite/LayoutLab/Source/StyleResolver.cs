using System.Globalization;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.Parser;

namespace Ignite.LayoutLab;

/// <summary>
/// Resolves the rules that matched a node into a <see cref="ComputedStyle"/>.
/// </summary>
/// <remarks>
/// Declarations are applied weakest-first, exactly as
/// <see cref="DataTable.MatchRules"/> returns them. Important declarations get a
/// second pass so they beat every normal declaration without allocating a
/// per-property cascade map. Values that need a containing block stay as
/// <see cref="CssLength"/> and are resolved by the layout pass.
/// </remarks>
public sealed partial class StyleResolver
{
    private readonly DataTable _table;
    private readonly StyleValueCache _cache;
    private readonly LayoutOptions _options;
    private readonly LengthContext _baseContext;

    public StyleResolver(DataTable table, StyleValueCache cache, LayoutOptions options)
    {
        _table = table;
        _cache = cache;
        _options = options;
        _baseContext = options.CreateRootLengthContext();
    }

    public StyleValueCache Cache => _cache;

    public ComputedStyle Resolve(
        IgniteSupportedDomNode node,
        ComputedStyle? parent,
        ReadOnlySpan<MatchedRule> matches)
    {
        ArgumentNullException.ThrowIfNull(node);

        var style = new ComputedStyle();

        if (parent is not null)
        {
            style.CopyInheritedFrom(parent);
        }

        CollectCustomProperties(style, matches);

        ApplyMatches(style, parent, matches, important: false);
        ApplyMatches(style, parent, matches, important: true);

        // Text nodes and fragments are framework concepts, not CSS elements.
        // Text flows inline and a fragment is transparent to layout.
        if (node.IsTextNode)
        {
            style.Display = Display.Inline;
        }
        else if (node.IsFragment)
        {
            style.Display = Display.Contents;
        }

        return style;
    }

    private void CollectCustomProperties(ComputedStyle style, ReadOnlySpan<MatchedRule> matches)
    {
       // Matches arrive weakest first, so later declarations overwrite earlier
        // ones. Normal custom properties are collected first, then important ones
        // overwrite them regardless of where they appear in the cascade.
        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var name = InternTable.GetString(rule.PropertyId);

            if (name is null || !name.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = InternTable.GetString(rule.ValueId);

            if (value is null || IsImportant(rule.ValueId, value))
            {
                continue;
            }

            style.CustomProperties ??= new Dictionary<string, string>(StringComparer.Ordinal);
            style.CustomProperties[name] = CleanValue(rule.ValueId, value);
        }

        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var name = InternTable.GetString(rule.PropertyId);

            if (name is null || !name.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var value = InternTable.GetString(rule.ValueId);

            if (value is not null && IsImportant(rule.ValueId, value))
            {
                style.CustomProperties ??= new Dictionary<string, string>(StringComparer.Ordinal);
                style.CustomProperties[name] = CleanValue(rule.ValueId, value);
            }
        }
    }

    private void ApplyMatches(
        ComputedStyle style,
        ComputedStyle? parent,
        ReadOnlySpan<MatchedRule> matches,
        bool important)
    {
        for (var i = 0; i < matches.Length; i++)
        {
            var rule = matches[i].Rule;
            var property = CssPropertyIds.Get(rule.PropertyId);
            var value = InternTable.GetString(rule.ValueId);

            if (value is null || IsImportant(rule.ValueId, value) != important)
            {
                continue;
            }

            if (property == CssProperty.Unknown)
            {
                var name = InternTable.GetString(rule.PropertyId);

                if (name is not null && name.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                style.UnsupportedProperties ??= [];
                style.UnsupportedProperties.Add(rule.PropertyId);
                continue;
            }

            ApplyDeclaration(style, parent, property, rule.ValueId, value);
        }
    }

    private void ApplyDeclaration(
        ComputedStyle style,
        ComputedStyle? parent,
        CssProperty property,
        int valueId,
        string rawValue)
    {
        var value = CleanValue(valueId, rawValue);

        if (value.Length == 0)
        {
            return;
        }

        if (property == CssProperty.All)
        {
            ApplyAll(style, parent, value);
            return;
        }

        value = ResolveVariables(value, style, valueId);

        if (value.Length == 0)
        {
            return;
        }

        if (TryGetWideKeyword(value, out var keyword))
        {
            ApplyWideKeyword(style, parent, property, keyword);
            return;
        }

        switch (property)
        {
            case CssProperty.Display:
                style.Display = ParseDisplay(value);
                break;

            case CssProperty.Position:
                style.Position = ParsePosition(value);
                break;

            case CssProperty.Float:
                style.Float = value.Equals("left", StringComparison.OrdinalIgnoreCase)
                    ? Float.Left
                    : value.Equals("right", StringComparison.OrdinalIgnoreCase)
                        ? Float.Right
                        : Float.None;
                break;

            case CssProperty.Clear:
                style.Clear = value.Equals("both", StringComparison.OrdinalIgnoreCase) ? Clear.Both
                    : value.Equals("left", StringComparison.OrdinalIgnoreCase) ? Clear.Left
                    : value.Equals("right", StringComparison.OrdinalIgnoreCase) ? Clear.Right
                    : Clear.None;
                break;

            case CssProperty.BoxSizing:
                style.BoxSizing = value.Equals("border-box", StringComparison.OrdinalIgnoreCase)
                    ? BoxSizing.BorderBox
                    : BoxSizing.ContentBox;
                break;

            case CssProperty.Visibility:
                style.Visibility = value.Equals("hidden", StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Hidden
                    : value.Equals("collapse", StringComparison.OrdinalIgnoreCase)
                        ? Visibility.Collapse
                        : Visibility.Visible;
                break;

            case CssProperty.Opacity:
                style.Opacity = Math.Clamp(ParseNumberCached(valueId, value), 0, 1);
                break;

            case CssProperty.ZIndex:
                if (value.Equals("auto", StringComparison.OrdinalIgnoreCase))
                {
                    style.HasZIndex = false;
                    style.ZIndex = 0;
                }
                else
                {
                    style.HasZIndex = true;
                    style.ZIndex = (int)MathF.Round(ParseNumberCached(valueId, value));
                }

                break;

            case CssProperty.Width:
                style.Width = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Height:
                style.Height = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MinWidth:
                style.MinWidth = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MinHeight:
                style.MinHeight = ParseLengthCached(valueId, value);
                break;

            case CssProperty.MaxWidth:
                style.MaxWidth = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Auto
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.MaxHeight:
                style.MaxHeight = value.Equals("none", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Auto
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.AspectRatio:
                style.AspectRatio = ParseAspectRatio(value);
                break;

            case CssProperty.Top:
                style.Top = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Right:
                style.Right = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Bottom:
                style.Bottom = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Left:
                style.Left = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Inset:
                style.Top = ParseLength4Cached(valueId, value).Top;
                style.Right = ParseLength4Cached(valueId, value).Right;
                style.Bottom = ParseLength4Cached(valueId, value).Bottom;
                style.Left = ParseLength4Cached(valueId, value).Left;
                break;

            case CssProperty.Margin:
                style.Margin = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.MarginTop:
                style.Margin = style.Margin.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginRight:
                style.Margin = style.Margin.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginBottom:
                style.Margin = style.Margin.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.MarginLeft:
                style.Margin = style.Margin.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.Padding:
                style.Padding = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.PaddingTop:
                style.Padding = style.Padding.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingRight:
                style.Padding = style.Padding.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingBottom:
                style.Padding = style.Padding.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.PaddingLeft:
                style.Padding = style.Padding.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.Border:
            {
                var border = ParseBorderCached(valueId, value);
                style.BorderWidth = CssLengths.All(border.Width);
                style.BorderStyleTop = style.BorderStyleRight = style.BorderStyleBottom = style.BorderStyleLeft = border.Style;
                style.BorderColorTop = style.BorderColorRight = style.BorderColorBottom = style.BorderColorLeft = border.Color;
                break;
            }

            case CssProperty.BorderWidth:
                style.BorderWidth = ParseLength4Cached(valueId, value);
                break;

            case CssProperty.BorderStyle:
            {
                var styles = ParseBorderStyles(value);
                style.BorderStyleTop = styles.Top;
                style.BorderStyleRight = styles.Right;
                style.BorderStyleBottom = styles.Bottom;
                style.BorderStyleLeft = styles.Left;
                break;
            }

            case CssProperty.BorderColor:
            {
                var colors = ParseColor4Cached(valueId, value, style.Color);
                style.BorderColorTop = colors.Top;
                style.BorderColorRight = colors.Right;
                style.BorderColorBottom = colors.Bottom;
                style.BorderColorLeft = colors.Left;
                break;
            }

            case CssProperty.BorderTop:
                ApplyBorderSide(style, 0, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderRight:
                ApplyBorderSide(style, 1, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderBottom:
                ApplyBorderSide(style, 2, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderLeft:
                ApplyBorderSide(style, 3, ParseBorderCached(valueId, value));
                break;

            case CssProperty.BorderTopWidth:
                style.BorderWidth = style.BorderWidth.WithTop(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderRightWidth:
                style.BorderWidth = style.BorderWidth.WithRight(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderBottomWidth:
                style.BorderWidth = style.BorderWidth.WithBottom(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderLeftWidth:
                style.BorderWidth = style.BorderWidth.WithLeft(ParseLengthCached(valueId, value));
                break;

            case CssProperty.BorderTopStyle:
                style.BorderStyleTop = ParseBorderStyle(value);
                break;

            case CssProperty.BorderRightStyle:
                style.BorderStyleRight = ParseBorderStyle(value);
                break;

            case CssProperty.BorderBottomStyle:
                style.BorderStyleBottom = ParseBorderStyle(value);
                break;

            case CssProperty.BorderLeftStyle:
                style.BorderStyleLeft = ParseBorderStyle(value);
                break;

            case CssProperty.BorderTopColor:
                style.BorderColorTop = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderRightColor:
                style.BorderColorRight = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderBottomColor:
                style.BorderColorBottom = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderLeftColor:
                style.BorderColorLeft = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BorderRadius:
                style.BorderRadius = ParseRadiusCached(valueId, value);
                break;

            case CssProperty.BorderTopLeftRadius:
                style.BorderRadius = style.BorderRadius with { TopLeft = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderTopRightRadius:
                style.BorderRadius = style.BorderRadius with { TopRight = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderBottomRightRadius:
                style.BorderRadius = style.BorderRadius with { BottomRight = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.BorderBottomLeftRadius:
                style.BorderRadius = style.BorderRadius with { BottomLeft = ParseLengthCached(valueId, value) };
                break;

            case CssProperty.Background:
                ApplyBackgroundShorthand(style, value);
                break;

            case CssProperty.BackgroundColor:
                style.BackgroundColor = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.BackgroundImage:
                style.BackgroundImages = ParseBackgroundsCached(valueId, value, style.Color);
                break;

            case CssProperty.BackgroundSize:
                ApplyBackgroundSize(style, value);
                break;

            case CssProperty.BackgroundPosition:
                ApplyBackgroundPosition(style, value);
                break;

            case CssProperty.BackgroundRepeat:
                style.BackgroundRepeat = ParseBackgroundRepeat(value);
                break;

            case CssProperty.BackgroundClip:
                style.BackgroundClip = ParseBackgroundBox(value);
                break;

            case CssProperty.BackgroundOrigin:
                style.BackgroundOrigin = ParseBackgroundBox(value);
                break;

            case CssProperty.BackgroundAttachment:
                style.BackgroundFixed = value.Equals("fixed", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.Color:
                style.Color = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.Font:
                ApplyFontShorthand(style, parent, value);
                break;

            case CssProperty.FontFamily:
                style.FontFamily = CssValueParser.Unquote(value.Trim());
                break;

            case CssProperty.FontSize:
                ApplyFontSize(style, parent, valueId, value);
                break;

            case CssProperty.FontWeight:
                style.FontWeight = ParseFontWeight(value);
                break;

            case CssProperty.FontStyle:
                style.Italic = value.StartsWith("italic", StringComparison.OrdinalIgnoreCase)
                    || value.StartsWith("oblique", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.LineHeight:
                style.LineHeight = ParseLineHeight(value);
                break;

            case CssProperty.LetterSpacing:
                style.LetterSpacing = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.TextAlign:
                style.TextAlign = ParseTextAlign(value);
                break;

            case CssProperty.TextDecoration:
            case CssProperty.TextDecorationLine:
                style.TextDecoration = ParseTextDecoration(value);
                break;

            case CssProperty.TextTransform:
                style.TextTransform = value.Equals("uppercase", StringComparison.OrdinalIgnoreCase)
                    ? TextTransform.Uppercase
                    : value.Equals("lowercase", StringComparison.OrdinalIgnoreCase)
                        ? TextTransform.Lowercase
                        : value.Equals("capitalize", StringComparison.OrdinalIgnoreCase)
                            ? TextTransform.Capitalize
                            : TextTransform.None;
                break;

            case CssProperty.WhiteSpace:
                style.WhiteSpace = ParseWhiteSpace(value);
                break;

            case CssProperty.WordBreak:
                if (value.Equals("break-all", StringComparison.OrdinalIgnoreCase))
                {
                    style.OverflowWrap = OverflowWrap.Anywhere;
                }

                break;

            case CssProperty.OverflowWrap:
                style.OverflowWrap = value.Equals("break-word", StringComparison.OrdinalIgnoreCase)
                    ? OverflowWrap.BreakWord
                    : value.Equals("anywhere", StringComparison.OrdinalIgnoreCase)
                        ? OverflowWrap.Anywhere
                        : OverflowWrap.Normal;
                break;

            case CssProperty.TextOverflow:
                style.TextOverflow = value.Equals("ellipsis", StringComparison.OrdinalIgnoreCase)
                    ? TextOverflow.Ellipsis
                    : TextOverflow.Clip;
                break;

            case CssProperty.Direction:
                style.Direction = value.Equals("rtl", StringComparison.OrdinalIgnoreCase)
                    ? Direction.RightToLeft
                    : Direction.LeftToRight;
                break;

            case CssProperty.WritingMode:
                style.WritingMode = value.Equals("vertical-rl", StringComparison.OrdinalIgnoreCase)
                    ? WritingMode.VerticalRightToLeft
                    : value.Equals("vertical-lr", StringComparison.OrdinalIgnoreCase)
                        ? WritingMode.VerticalLeftToRight
                        : WritingMode.HorizontalTopToBottom;
                break;

            case CssProperty.WebkitLineClamp:
                style.WebkitLineClamp = Math.Max(0, (int)MathF.Round(ParseNumberCached(valueId, value)));
                break;

            case CssProperty.Flex:
                ApplyFlexShorthand(style, value);
                break;

            case CssProperty.FlexDirection:
                style.FlexDirection = ParseFlexDirection(value);
                break;

            case CssProperty.FlexWrap:
                style.FlexWrap = ParseFlexWrap(value);
                break;

            case CssProperty.FlexFlow:
                foreach (var part in CssValueParser.SplitTopLevel(value, ' '))
                {
                    if (part.Contains("wrap", StringComparison.OrdinalIgnoreCase))
                    {
                        style.FlexWrap = ParseFlexWrap(part);
                    }
                    else if (part is "row" or "column" or "row-reverse" or "column-reverse")
                    {
                        style.FlexDirection = ParseFlexDirection(part);
                    }
                }

                break;

            case CssProperty.JustifyContent:
                style.JustifyContent = ParseJustifyContent(value);
                break;

            case CssProperty.AlignItems:
                style.AlignItems = ParseAlignItems(value);
                break;

            case CssProperty.AlignContent:
                style.AlignContent = ParseAlignContent(value);
                break;

            case CssProperty.AlignSelf:
                style.AlignSelf = ParseAlignSelf(value);
                break;

            case CssProperty.FlexGrow:
                style.FlexGrow = Math.Max(0, ParseNumberCached(valueId, value));
                break;

            case CssProperty.FlexShrink:
                style.FlexShrink = Math.Max(0, ParseNumberCached(valueId, value));
                break;

            case CssProperty.FlexBasis:
                style.FlexBasis = value.Equals("content", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.MaxContent
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.Gap:
            {
                var lengths = CssLengths.ParseShorthand(value);
                style.RowGap = lengths.Top;
                style.ColumnGap = lengths.Right;
                break;
            }

            case CssProperty.RowGap:
                style.RowGap = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.ColumnGap:
                style.ColumnGap = value.Equals("normal", StringComparison.OrdinalIgnoreCase)
                    ? CssLength.Zero
                    : ParseLengthCached(valueId, value);
                break;

            case CssProperty.Order:
                style.Order = (int)MathF.Round(ParseNumberCached(valueId, value));
                break;

            case CssProperty.Grid:
            case CssProperty.GridTemplate:
                ApplyGridTemplate(style, value);
                break;

            case CssProperty.GridTemplateColumns:
                style.GridTemplateColumns = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridTemplateRows:
                style.GridTemplateRows = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoColumns:
                style.GridAutoColumns = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoRows:
                style.GridAutoRows = ParseTracksCached(valueId, value);
                break;

            case CssProperty.GridAutoFlow:
                style.GridAutoFlow = ParseGridAutoFlow(value);
                break;

            case CssProperty.GridColumn:
                style.GridColumn = ParsePlacementCached(valueId, value);
                break;

            case CssProperty.GridRow:
                style.GridRow = ParsePlacementCached(valueId, value);
                break;

            case CssProperty.GridColumnStart:
                style.GridColumn = ApplyGridLine(style.GridColumn, value, true);
                break;

            case CssProperty.GridColumnEnd:
                style.GridColumn = ApplyGridLine(style.GridColumn, value, false);
                break;

            case CssProperty.GridRowStart:
                style.GridRow = ApplyGridLine(style.GridRow, value, true);
                break;

            case CssProperty.GridRowEnd:
                style.GridRow = ApplyGridLine(style.GridRow, value, false);
                break;

            case CssProperty.GridArea:
            {
                var parts = CssValueParser.SplitTopLevel(value, '/');

                if (parts.Count == 1)
                {
                    var name = parts[0].Trim();

                    if (name.Length > 0
                        && !name.Equals("auto", StringComparison.OrdinalIgnoreCase)
                        && !int.TryParse(name, out _))
                    {
                        style.GridAreaName = name;
                        style.GridRow = GridPlacement.Auto;
                        style.GridColumn = GridPlacement.Auto;
                    }
                    else if (int.TryParse(name, out var line))
                    {
                        style.GridAreaName = null;
                        style.GridRow = new GridPlacement(line, 0, false, true);
                        style.GridColumn = new GridPlacement(line, 0, false, true);
                    }

                    break;
                }

                style.GridAreaName = null;

                // grid-area: row-start / column-start / row-end / column-end
                style.GridRow = GridPlacementParser.Parse(
                    (parts.Count > 0 ? parts[0] : "auto")
                    + " / " + (parts.Count > 2 ? parts[2] : "auto"));

                style.GridColumn = GridPlacementParser.Parse(
                    (parts.Count > 1 ? parts[1] : "auto")
                    + " / " + (parts.Count > 3 ? parts[3] : "auto"));

                break;
            }

            case CssProperty.JustifyItems:
                style.JustifyItems = ParseJustifyItems(value);
                break;

            case CssProperty.JustifySelf:
                style.JustifySelf = ParseJustifySelf(value);
                break;

            case CssProperty.PlaceItems:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.AlignItems = ParseAlignItems(parts.Count > 0 ? parts[0] : value);
                style.JustifyItems = ParseJustifyItems(parts.Count > 1 ? parts[1] : (parts.Count > 0 ? parts[0] : value));
                break;
            }

            case CssProperty.PlaceContent:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.AlignContent = ParseAlignContent(parts.Count > 0 ? parts[0] : value);
                break;
            }

            case CssProperty.Overflow:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');
                style.OverflowX = ParseOverflow(parts.Count > 0 ? parts[0] : value);
                style.OverflowY = ParseOverflow(parts.Count > 1 ? parts[1] : (parts.Count > 0 ? parts[0] : value));
                break;
            }

            case CssProperty.OverflowX:
                style.OverflowX = ParseOverflow(value);
                break;

            case CssProperty.OverflowY:
                style.OverflowY = ParseOverflow(value);
                break;

            case CssProperty.Transform:
                style.Transform = ParseTransformCached(valueId, value);
                break;

            case CssProperty.TransformOrigin:
                ApplyTransformOrigin(style, value);
                break;

            case CssProperty.BoxShadow:
                style.BoxShadows = ParseShadowsCached(valueId, value, style.Color);
                break;

            case CssProperty.Outline:
                ApplyOutlineShorthand(style, valueId, value);
                break;

            case CssProperty.OutlineWidth:
                style.OutlineWidth = ParseLengthCached(valueId, value);
                break;

            case CssProperty.OutlineStyle:
                style.OutlineStyle = ParseBorderStyle(value);
                break;

            case CssProperty.OutlineColor:
                style.OutlineColor = ParseColorCached(valueId, value, style.Color);
                break;

            case CssProperty.OutlineOffset:
                style.OutlineOffset = ParseLengthCached(valueId, value);
                break;

            case CssProperty.Filter:
                style.Filter = ParseFilterCached(valueId, value, style.Color);
                break;

            case CssProperty.BackdropFilter:
                style.BackdropFilter = ParseFilterCached(valueId, value, style.Color);
                break;

            case CssProperty.ClipPath:
                style.ClipPath = ParseClipPathCached(valueId, value);
                break;

            case CssProperty.Mask:
                style.Mask = ParseMaskCached(valueId, value);
                break;

            case CssProperty.MixBlendMode:
                style.MixBlendMode = ParseMixBlendMode(value);
                break;

            case CssProperty.Isolation:
                style.Isolate = value.Equals("isolate", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.GridTemplateAreas:
                style.GridTemplateAreas = ParseGridAreasCached(valueId, value);
                break;

            case CssProperty.ObjectFit:
                style.ObjectFit = ParseObjectFit(value);
                break;

            case CssProperty.TableLayout:
                style.TableLayout = value.Equals("fixed", StringComparison.OrdinalIgnoreCase)
                    ? TableLayout.Fixed
                    : TableLayout.Auto;
                break;

            case CssProperty.BorderCollapse:
                style.BorderCollapse = value.Equals("collapse", StringComparison.OrdinalIgnoreCase)
                    ? BorderCollapse.Collapse
                    : BorderCollapse.Separate;
                break;

            case CssProperty.BorderSpacing:
                style.BorderSpacing = ParseLengthCached(valueId, value);
                break;

            case CssProperty.ColumnCount:
                style.ColumnCount = Math.Max(1, (int)MathF.Round(ParseNumberCached(valueId, value)));
                break;

            case CssProperty.ScrollbarColor:
            {
                var parts = CssValueParser.SplitTopLevel(value, ' ');

                if (parts.Count >= 2)
                {
                    // Parse the two colours directly: the memo cache is keyed by
                    // the declaration's value id, so caching sub-values under it
                    // would make the second colour reuse the first colour's entry.
                    style.ScrollbarThumbColor = CssColor.ParseOr(parts[0], style.Color, style.Color);
                    style.ScrollbarTrackColor = CssColor.ParseOr(parts[1], style.Color, style.Color);
                }

                break;
            }

            case CssProperty.ScrollbarWidth:
                style.ShowScrollbars = !value.Equals("none", StringComparison.OrdinalIgnoreCase);
                break;

            case CssProperty.PointerEvents:
            case CssProperty.Cursor:
            case CssProperty.UserSelect:
            case CssProperty.WillChange:
            case CssProperty.Contain:
            case CssProperty.Content:
            case CssProperty.ObjectPosition:
                // Parsed and ignored by the first-cut painter. Keeping the case
                // explicit documents that these are intentionally not layout
                // inputs rather than accidental omissions.
                break;

            default:
                style.UnsupportedProperties ??= [];
                style.UnsupportedProperties.Add(rulePropertyId(property));
                break;
        }
    }

    private static int rulePropertyId(CssProperty property)
    {
        // Only used for the diagnostic unsupported list; the id is not needed
        // for layout and interning it here keeps the hot switch allocation free.
        return (int)property;
    }

    private void ApplyBorderSide(ComputedStyle style, int side, ParsedBorder border)
    {
        switch (side)
        {
            case 0:
                style.BorderWidth = style.BorderWidth.WithTop(border.Width);
                style.BorderStyleTop = border.Style;
                style.BorderColorTop = border.Color;
                break;
            case 1:
                style.BorderWidth = style.BorderWidth.WithRight(border.Width);
                style.BorderStyleRight = border.Style;
                style.BorderColorRight = border.Color;
                break;
            case 2:
                style.BorderWidth = style.BorderWidth.WithBottom(border.Width);
                style.BorderStyleBottom = border.Style;
                style.BorderColorBottom = border.Color;
                break;
            case 3:
                style.BorderWidth = style.BorderWidth.WithLeft(border.Width);
                style.BorderStyleLeft = border.Style;
                style.BorderColorLeft = border.Color;
                break;
        }
    }
}
