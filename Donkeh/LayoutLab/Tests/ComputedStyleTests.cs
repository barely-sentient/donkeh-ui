namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for the resolved style object: the initial values that
/// every element starts from, the inheritance contract, the predicates the
/// layout passes branch on and the helpers that fold a style into paint data.
/// </summary>
public class ComputedStyleTests
{
    private static readonly LengthContext Context = new(
        fontSize: 10,
        rootFontSize: 8,
        viewportWidth: 800,
        viewportHeight: 600,
        percentBasis: 200);

    [Fact]
    public void InitialValuesFollowTheCssInitialValues()
    {
        var style = new ComputedStyle();

        Assert.Equal(Display.Block, style.Display);
        Assert.Equal(Position.Static, style.Position);
        Assert.Equal(Float.None, style.Float);
        Assert.Equal(Clear.None, style.Clear);
        Assert.Equal(BoxSizing.ContentBox, style.BoxSizing);
        Assert.Equal(Visibility.Visible, style.Visibility);

        Assert.False(style.HasZIndex);
        Assert.Equal(0, style.ZIndex);
        Assert.Equal(1, style.Opacity);

        Assert.Equal(CssLength.Auto, style.Width);
        Assert.Equal(CssLength.Auto, style.Height);
        Assert.Equal(CssLength.Auto, style.MinWidth);
        Assert.Equal(CssLength.Auto, style.MinHeight);
        Assert.Equal(CssLength.Auto, style.MaxWidth);
        Assert.Equal(CssLength.Auto, style.MaxHeight);
        Assert.Null(style.AspectRatio);

        Assert.Equal(CssLength.Auto, style.Top);
        Assert.Equal(CssLength.Auto, style.Right);
        Assert.Equal(CssLength.Auto, style.Bottom);
        Assert.Equal(CssLength.Auto, style.Left);

        Assert.Equal(CssLength4.Zero, style.Margin);
        Assert.Equal(CssLength4.Zero, style.Padding);
        Assert.Equal(CssLength4.Zero, style.BorderWidth);
        Assert.Equal(CssCornerRadius4.Zero, style.BorderRadius);
    }

    [Fact]
    public void BorderAndOutlineColorsStartAsCurrentColor()
    {
        var style = new ComputedStyle();

        Assert.Equal(ComputedStyle.CurrentColor, style.BorderColorTop);
        Assert.Equal(ComputedStyle.CurrentColor, style.BorderColorRight);
        Assert.Equal(ComputedStyle.CurrentColor, style.BorderColorBottom);
        Assert.Equal(ComputedStyle.CurrentColor, style.BorderColorLeft);
        Assert.Equal(ComputedStyle.CurrentColor, style.OutlineColor);
        Assert.Equal(BorderStyle.None, style.BorderStyleTop);
        Assert.Equal(BorderStyle.None, style.BorderStyleRight);
        Assert.Equal(BorderStyle.None, style.BorderStyleBottom);
        Assert.Equal(BorderStyle.None, style.BorderStyleLeft);
    }

    [Fact]
    public void PaintDefaultsAreBlackOnTransparent()
    {
        var style = new ComputedStyle();

        Assert.Equal(CssColor.Black, style.Color);
        Assert.Equal(CssColor.Transparent, style.BackgroundColor);
        Assert.Null(style.BackgroundImages);
        Assert.Equal(CssLength.Auto, style.BackgroundWidth);
        Assert.Equal(CssLength.Auto, style.BackgroundHeight);
        Assert.Equal(BackgroundSizeMode.Auto, style.BackgroundSizeMode);
        Assert.Equal(CssLength.Percent(0), style.BackgroundPositionX);
        Assert.Equal(CssLength.Percent(0), style.BackgroundPositionY);
        Assert.Equal(BackgroundRepeat.Repeat, style.BackgroundRepeat);
        Assert.Equal(BackgroundBox.BorderBox, style.BackgroundClip);
        Assert.Equal(BackgroundBox.PaddingBox, style.BackgroundOrigin);
        Assert.False(style.BackgroundFixed);
    }

    [Fact]
    public void TextDefaultsUseSansSerifAtSixteenPixels()
    {
        var style = new ComputedStyle();

        Assert.Equal("sans-serif", style.FontFamily);
        Assert.Equal(CssLength.Px(16), style.FontSize);
        Assert.Equal(400, style.FontWeight);
        Assert.False(style.Italic);
        Assert.Equal(CssLength.Number(1.2f), style.LineHeight);
        Assert.Equal(CssLength.Zero, style.LetterSpacing);
        Assert.Equal(TextAlign.Start, style.TextAlign);
        Assert.Equal(WhiteSpace.Normal, style.WhiteSpace);
        Assert.Equal(TextOverflow.Clip, style.TextOverflow);
        Assert.Equal(TextDecoration.None, style.TextDecoration);
        Assert.Equal(TextTransform.None, style.TextTransform);
        Assert.Equal(Direction.LeftToRight, style.Direction);
        Assert.Equal(WritingMode.HorizontalTopToBottom, style.WritingMode);
        Assert.Equal(0, style.WebkitLineClamp);
        Assert.Equal(OverflowWrap.Normal, style.OverflowWrap);
    }

    [Fact]
    public void FlexDefaultsMatchTheFlexboxInitialValues()
    {
        var style = new ComputedStyle();

        Assert.Equal(FlexDirection.Row, style.FlexDirection);
        Assert.Equal(FlexWrap.NoWrap, style.FlexWrap);
        Assert.Equal(JustifyContent.FlexStart, style.JustifyContent);
        Assert.Equal(AlignItems.Stretch, style.AlignItems);
        Assert.Equal(AlignContent.Stretch, style.AlignContent);
        Assert.Equal(AlignSelf.Auto, style.AlignSelf);
        Assert.Equal(0, style.FlexGrow);
        Assert.Equal(1, style.FlexShrink);
        Assert.Equal(CssLength.Auto, style.FlexBasis);
        Assert.Equal(0, style.Order);
        Assert.Equal(CssLength.Zero, style.RowGap);
        Assert.Equal(CssLength.Zero, style.ColumnGap);
    }

    [Fact]
    public void GridDefaultsLeaveTemplatesUnsetAndFlowRow()
    {
        var style = new ComputedStyle();

        Assert.Null(style.GridTemplateColumns);
        Assert.Null(style.GridTemplateRows);
        Assert.Null(style.GridAutoColumns);
        Assert.Null(style.GridAutoRows);
        Assert.Equal(GridAutoFlow.Row, style.GridAutoFlow);
        Assert.Equal(GridPlacement.Auto, style.GridColumn);
        Assert.Equal(GridPlacement.Auto, style.GridRow);
        Assert.Null(style.GridAreaName);
        Assert.Null(style.GridTemplateAreas);
        Assert.Equal(JustifyItems.Normal, style.JustifyItems);
        Assert.Equal(JustifySelf.Auto, style.JustifySelf);
        Assert.Equal(AlignItems.Stretch, style.JustifyItemsBlock);
    }

    [Fact]
    public void MiscellaneousDefaultsCoverOverflowTransformsAndTables()
    {
        var style = new ComputedStyle();

        Assert.Equal(Overflow.Visible, style.OverflowX);
        Assert.Equal(Overflow.Visible, style.OverflowY);
        Assert.Equal(0xFFE0E0E0u, style.ScrollbarTrackColor);
        Assert.Equal(0xFF909090u, style.ScrollbarThumbColor);
        Assert.False(style.ShowScrollbars);

        Assert.Null(style.Transform);
        Assert.Equal(CssLength.Percent(50), style.TransformOriginX);
        Assert.Equal(CssLength.Percent(50), style.TransformOriginY);
        Assert.Null(style.BoxShadows);
        Assert.Null(style.Filter);
        Assert.Null(style.BackdropFilter);
        Assert.Null(style.ClipPath);
        Assert.Null(style.Mask);
        Assert.Equal(MixBlendMode.Normal, style.MixBlendMode);
        Assert.False(style.Isolate);

        Assert.Equal(CssLength.Zero, style.OutlineWidth);
        Assert.Equal(BorderStyle.None, style.OutlineStyle);
        Assert.Equal(CssLength.Zero, style.OutlineOffset);

        Assert.Equal(ObjectFit.Fill, style.ObjectFit);
        Assert.Equal(TableLayout.Auto, style.TableLayout);
        Assert.Equal(BorderCollapse.Separate, style.BorderCollapse);
        Assert.Equal(CssLength.Px(2), style.BorderSpacing);
        Assert.Equal(1, style.ColumnCount);
        Assert.Equal(CssLength.Px(16), style.ColumnGapFallback);

        Assert.Null(style.CustomProperties);
        Assert.Null(style.UnsupportedProperties);
    }

    [Theory]
    [InlineData(Position.Static, false)]
    [InlineData(Position.Relative, false)]
    [InlineData(Position.Absolute, true)]
    [InlineData(Position.Fixed, true)]
    [InlineData(Position.Sticky, false)]
    public void IsAbsolutelyPositionedOnlyCoversAbsoluteAndFixed(Position position, bool expected)
    {
        var style = new ComputedStyle { Position = position };
        Assert.Equal(expected, style.IsAbsolutelyPositioned);
    }

    [Theory]
    [InlineData(Display.Flex, true)]
    [InlineData(Display.InlineFlex, true)]
    [InlineData(Display.Block, false)]
    [InlineData(Display.Grid, false)]
    [InlineData(Display.Inline, false)]
    public void IsFlexContainerCoversBothFlexDisplays(Display display, bool expected)
    {
        var style = new ComputedStyle { Display = display };
        Assert.Equal(expected, style.IsFlexContainer);
    }

    [Theory]
    [InlineData(Display.Grid, true)]
    [InlineData(Display.InlineGrid, true)]
    [InlineData(Display.Block, false)]
    [InlineData(Display.Flex, false)]
    public void IsGridContainerCoversBothGridDisplays(Display display, bool expected)
    {
        var style = new ComputedStyle { Display = display };
        Assert.Equal(expected, style.IsGridContainer);
    }

    [Theory]
    [InlineData(Display.Block, true)]
    [InlineData(Display.FlowRoot, true)]
    [InlineData(Display.ListItem, true)]
    [InlineData(Display.InlineBlock, false)]
    [InlineData(Display.Flex, false)]
    [InlineData(Display.None, false)]
    public void IsBlockContainerCoversBlockFlowDisplays(Display display, bool expected)
    {
        var style = new ComputedStyle { Display = display };
        Assert.Equal(expected, style.IsBlockContainer);
    }

    [Theory]
    [InlineData(Display.Inline, true)]
    [InlineData(Display.InlineBlock, true)]
    [InlineData(Display.InlineFlex, true)]
    [InlineData(Display.InlineGrid, true)]
    [InlineData(Display.InlineTable, true)]
    [InlineData(Display.Block, false)]
    [InlineData(Display.Flex, false)]
    public void IsInlineLevelCoversEveryInlineDisplay(Display display, bool expected)
    {
        var style = new ComputedStyle { Display = display };
        Assert.Equal(expected, style.IsInlineLevel);
    }

    [Theory]
    [InlineData(Overflow.Visible, Overflow.Visible, false)]
    [InlineData(Overflow.Hidden, Overflow.Visible, true)]
    [InlineData(Overflow.Visible, Overflow.Hidden, true)]
    [InlineData(Overflow.Clip, Overflow.Visible, true)]
    [InlineData(Overflow.Scroll, Overflow.Visible, true)]
    [InlineData(Overflow.Auto, Overflow.Visible, true)]
    [InlineData(Overflow.Visible, Overflow.Auto, true)]
    public void IsScrollContainerDetectsEitherAxisBeingClipped(
        Overflow x, Overflow y, bool expected)
    {
        var style = new ComputedStyle { OverflowX = x, OverflowY = y };
        Assert.Equal(expected, style.IsScrollContainer);
    }

    [Theory]
    [InlineData(Position.Static, Float.None, true)]
    [InlineData(Position.Relative, Float.None, true)]
    [InlineData(Position.Sticky, Float.None, true)]
    [InlineData(Position.Absolute, Float.None, false)]
    [InlineData(Position.Fixed, Float.None, false)]
    [InlineData(Position.Static, Float.Left, false)]
    [InlineData(Position.Static, Float.Right, false)]
    public void IsInFlowRejectsOutOfFlowBoxes(Position position, Float floatValue, bool expected)
    {
        var style = new ComputedStyle { Position = position, Float = floatValue };
        Assert.Equal(expected, style.IsInFlow);
    }

    [Fact]
    public void CopyInheritedFromCopiesOnlyInheritedProperties()
    {
        var parent = new ComputedStyle
        {
            Color = 0xFF112233,
            FontFamily = "serif",
            FontSize = CssLength.Em(2),
            FontWeight = 700,
            Italic = true,
            LineHeight = CssLength.Number(1.5f),
            LetterSpacing = CssLength.Px(2),
            TextAlign = TextAlign.Center,
            WhiteSpace = WhiteSpace.Pre,
            TextOverflow = TextOverflow.Ellipsis,
            TextTransform = TextTransform.Uppercase,
            Direction = Direction.RightToLeft,
            WritingMode = WritingMode.VerticalRightToLeft,
            Visibility = Visibility.Hidden,
            BorderCollapse = BorderCollapse.Collapse,
            BorderSpacing = CssLength.Px(9),
            ObjectFit = ObjectFit.Cover,
            Display = Display.Flex,
            Width = CssLength.Px(50),
            Margin = CssLengths.All(CssLength.Px(4))
        };

        var child = new ComputedStyle { Display = Display.Inline, Width = CssLength.Px(1) };
        child.CopyInheritedFrom(parent);

        Assert.Equal(0xFF112233u, child.Color);
        Assert.Equal("serif", child.FontFamily);
        Assert.Equal(CssLength.Em(2), child.FontSize);
        Assert.Equal(700, child.FontWeight);
        Assert.True(child.Italic);
        Assert.Equal(CssLength.Number(1.5f), child.LineHeight);
        Assert.Equal(CssLength.Px(2), child.LetterSpacing);
        Assert.Equal(TextAlign.Center, child.TextAlign);
        Assert.Equal(WhiteSpace.Pre, child.WhiteSpace);
        Assert.Equal(TextOverflow.Ellipsis, child.TextOverflow);
        Assert.Equal(TextTransform.Uppercase, child.TextTransform);
        Assert.Equal(Direction.RightToLeft, child.Direction);
        Assert.Equal(WritingMode.VerticalRightToLeft, child.WritingMode);
        Assert.Equal(Visibility.Hidden, child.Visibility);
        Assert.Equal(BorderCollapse.Collapse, child.BorderCollapse);
        Assert.Equal(CssLength.Px(9), child.BorderSpacing);
        Assert.Equal(ObjectFit.Cover, child.ObjectFit);

        // Non-inherited values stay with the child.
        Assert.Equal(Display.Inline, child.Display);
        Assert.Equal(CssLength.Px(1), child.Width);
        Assert.Equal(CssLength4.Zero, child.Margin);
    }

    [Fact]
    public void CopyInheritedFromDeepCopiesCustomProperties()
    {
        var parent = new ComputedStyle
        {
            CustomProperties = new Dictionary<string, string> { ["--accent"] = "red" }
        };

        var child = new ComputedStyle();
        child.CopyInheritedFrom(parent);

        Assert.NotNull(child.CustomProperties);
        Assert.Equal("red", child.CustomProperties!["--accent"]);
        Assert.NotSame(parent.CustomProperties, child.CustomProperties);

        child.CustomProperties!["--accent"] = "blue";
        Assert.Equal("red", parent.CustomProperties!["--accent"]);
    }

    [Fact]
    public void CopyInheritedFromLeavesCustomPropertiesUnsetWhenParentHasNone()
    {
        var child = new ComputedStyle();
        child.CopyInheritedFrom(new ComputedStyle());
        Assert.Null(child.CustomProperties);
    }

    [Fact]
    public void ResolveFontSizeUsesTheParentFontSizeAsTheEmBasis()
    {
        var context = new LengthContext(fontSize: 10, percentBasis: 200);

        Assert.Equal(16, new ComputedStyle().ResolveFontSize(context), 3);
        Assert.Equal(20, new ComputedStyle { FontSize = CssLength.Em(2) }
            .ResolveFontSize(context), 3);
        Assert.Equal(5, new ComputedStyle { FontSize = CssLength.Percent(50) }
            .ResolveFontSize(context), 3);
        Assert.Equal(16, new ComputedStyle { FontSize = CssLength.Auto }
            .ResolveFontSize(context), 3);
        Assert.Equal(18, new ComputedStyle { FontSize = CssLength.Px(18) }
            .ResolveFontSize(context), 3);
    }

    [Fact]
    public void ResolveFontSizeUsesTheRootFontSizeForRem()
    {
        var context = new LengthContext(fontSize: 10, rootFontSize: 8);
        var style = new ComputedStyle { FontSize = CssLength.Rem(2) };
        Assert.Equal(16, style.ResolveFontSize(context), 3);
    }

    [Fact]
    public void ResolveLineHeightMultipliesUnitlessValuesByTheFontSize()
    {
        var context = new LengthContext(fontSize: 20, percentBasis: 200);

        Assert.Equal(19.2f, new ComputedStyle().ResolveLineHeight(context), 3);
        Assert.Equal(24, new ComputedStyle { LineHeight = CssLength.Number(1.5f) }
            .ResolveLineHeight(context), 3);
    }

    [Fact]
    public void ResolveLineHeightResolvesLengthsAndFallsBackForAuto()
    {
        var context = new LengthContext(fontSize: 20, percentBasis: 200);

        Assert.Equal(35, new ComputedStyle { LineHeight = CssLength.Px(35) }
            .ResolveLineHeight(context), 3);
        Assert.Equal(32, new ComputedStyle { LineHeight = CssLength.Percent(200) }
            .ResolveLineHeight(context), 3);
        Assert.Equal(19.2f, new ComputedStyle { LineHeight = CssLength.Auto }
            .ResolveLineHeight(context), 3);
    }

    [Fact]
    public void ResolveBorderAndOutlineColorReplaceTheSentinel()
    {
        var style = new ComputedStyle { Color = 0xFF00FF00 };

        Assert.Equal(0xFF00FF00u, style.ResolveBorderColor(ComputedStyle.CurrentColor));
        Assert.Equal(0xFF123456u, style.ResolveBorderColor(0xFF123456));
        Assert.Equal(0xFF00FF00u, style.ResolveOutlineColor());

        style.OutlineColor = 0xFFABCDEF;
        Assert.Equal(0xFFABCDEFu, style.ResolveOutlineColor());
    }

    [Fact]
    public void ToTextStylePacksTheResolvedTextProperties()
    {
        var style = new ComputedStyle
        {
            FontFamily = "serif",
            FontWeight = 600,
            Italic = true,
            TextAlign = TextAlign.Right,
            TextDecoration = TextDecoration.Underline | TextDecoration.LineThrough,
            WhiteSpace = WhiteSpace.NoWrap,
            Direction = Direction.RightToLeft,
            LineHeight = CssLength.Number(1.5f),
            LetterSpacing = CssLength.Px(3)
        };

        var context = new LengthContext(fontSize: 20, percentBasis: 200);
        var text = style.ToTextStyle(context, 20, 0xFF010203);

        Assert.Equal("serif", text.FontFamily);
        Assert.Equal(20, text.FontSize, 3);
        Assert.Equal(600, text.FontWeight);
        Assert.True(text.Italic);
        Assert.Equal(0xFF010203u, text.Color);
        Assert.Equal(3, text.LetterSpacing, 3);
        Assert.Equal(24, text.LineHeight, 3);
        Assert.Equal(TextAlign.Right, text.Align);
        Assert.Equal(TextDecoration.Underline | TextDecoration.LineThrough, text.Decoration);
        Assert.Equal(WhiteSpace.NoWrap, text.WhiteSpace);
        Assert.Equal(Direction.RightToLeft, text.Direction);
    }

    [Fact]
    public void CloneCopiesEveryValueButDeepCopiesCustomProperties()
    {
        var style = new ComputedStyle
        {
            Display = Display.Grid,
            Width = CssLength.Px(120),
            ZIndex = 4,
            HasZIndex = true,
            CustomProperties = new Dictionary<string, string> { ["--x"] = "1" }
        };

        var clone = style.Clone();

        Assert.NotSame(style, clone);
        Assert.Equal(Display.Grid, clone.Display);
        Assert.Equal(CssLength.Px(120), clone.Width);
        Assert.Equal(4, clone.ZIndex);
        Assert.True(clone.HasZIndex);
        Assert.NotSame(style.CustomProperties, clone.CustomProperties);
        Assert.Equal("1", clone.CustomProperties!["--x"]);

        clone.CustomProperties!["--x"] = "2";
        Assert.Equal("1", style.CustomProperties!["--x"]);
    }

    [Fact]
    public void CopyAllFromTransfersEveryComputedField()
    {
        var source = new ComputedStyle
        {
            Display = Display.Flex,
            Position = Position.Absolute,
            Float = Float.Right,
            Clear = Clear.Both,
            BoxSizing = BoxSizing.BorderBox,
            Visibility = Visibility.Collapse,
            HasZIndex = true,
            ZIndex = 9,
            Opacity = 0.25f,
            Width = CssLength.Px(10),
            Height = CssLength.Percent(50),
            MinWidth = CssLength.Px(1),
            MinHeight = CssLength.Px(2),
            MaxWidth = CssLength.Px(3),
            MaxHeight = CssLength.Px(4),
            AspectRatio = 1.5f,
            Top = CssLength.Px(5),
            Right = CssLength.Px(6),
            Bottom = CssLength.Px(7),
            Left = CssLength.Px(8),
            Margin = CssLengths.All(CssLength.Px(11)),
            Padding = CssLengths.All(CssLength.Px(12)),
            BorderWidth = CssLengths.All(CssLength.Px(13)),
            BorderStyleTop = BorderStyle.Dashed,
            BorderStyleRight = BorderStyle.Dotted,
            BorderStyleBottom = BorderStyle.Double,
            BorderStyleLeft = BorderStyle.Solid,
            BorderColorTop = 1,
            BorderColorRight = 2,
            BorderColorBottom = 3,
            BorderColorLeft = 4,
            Color = 0xFFAABBCC,
            BackgroundColor = 0xFF000011,
            FontFamily = "mono",
            FontSize = CssLength.Px(22),
            FontWeight = 800,
            Italic = true,
            LineHeight = CssLength.Number(2),
            LetterSpacing = CssLength.Px(3),
            TextAlign = TextAlign.Justify,
            WhiteSpace = WhiteSpace.PreWrap,
            TextOverflow = TextOverflow.Ellipsis,
            TextDecoration = TextDecoration.Overline,
            TextTransform = TextTransform.Lowercase,
            Direction = Direction.RightToLeft,
            WritingMode = WritingMode.VerticalLeftToRight,
            WebkitLineClamp = 3,
            OverflowWrap = OverflowWrap.Anywhere,
            FlexDirection = FlexDirection.ColumnReverse,
            FlexWrap = FlexWrap.WrapReverse,
            JustifyContent = JustifyContent.SpaceEvenly,
            AlignItems = AlignItems.Center,
            AlignContent = AlignContent.SpaceAround,
            AlignSelf = AlignSelf.FlexEnd,
            FlexGrow = 2,
            FlexShrink = 3,
            FlexBasis = CssLength.Px(14),
            Order = 5,
            RowGap = CssLength.Px(15),
            ColumnGap = CssLength.Px(16),
            GridAutoFlow = GridAutoFlow.Column | GridAutoFlow.Dense,
            GridColumn = new GridPlacement(1, 3, false, false),
            GridRow = new GridPlacement(2, 4, false, false),
            GridAreaName = "hero",
            JustifyItems = JustifyItems.Center,
            JustifySelf = JustifySelf.End,
            JustifyItemsBlock = AlignItems.Center,
            OverflowX = Overflow.Hidden,
            OverflowY = Overflow.Auto,
            ScrollBehavior = ScrollBehavior.Smooth,
            ScrollbarTrackColor = 5,
            ScrollbarThumbColor = 6,
            ShowScrollbars = true,
            TransformOriginX = CssLength.Percent(10),
            TransformOriginY = CssLength.Percent(20),
            MixBlendMode = MixBlendMode.Multiply,
            Isolate = true,
            OutlineWidth = CssLength.Px(17),
            OutlineStyle = BorderStyle.Groove,
            OutlineColor = 7,
            OutlineOffset = CssLength.Px(18),
            ObjectFit = ObjectFit.Contain,
            TableLayout = TableLayout.Fixed,
            BorderCollapse = BorderCollapse.Collapse,
            BorderSpacing = CssLength.Px(19),
            ColumnCount = 2,
            ColumnGapFallback = CssLength.Px(20),
            CustomProperties = new Dictionary<string, string> { ["--a"] = "b" },
            UnsupportedProperties = [1, 2]
        };

        source.BorderRadius = new CssCornerRadius4(
            CssLength.Px(1), CssLength.Px(2), CssLength.Px(3), CssLength.Px(4));
        source.GridTemplateColumns = new GridTrackList([GridTrack.Fraction(1)]);
        source.GridTemplateAreas = new GridTemplateAreas([], 1, 1);
        source.Transform = TransformSpec.Identity;
        source.BoxShadows = [new ShadowSpec(1, 2, 3, 4, 5, false)];
        source.Filter = FilterSpec.None;
        source.BackdropFilter = FilterSpec.None;
        source.ClipPath = ClipPathSpec.None;
        source.Mask = MaskSpec.None;

        var target = new ComputedStyle();
        target.CopyAllFrom(source);

        Assert.Equal(source.Display, target.Display);
        Assert.Equal(source.Position, target.Position);
        Assert.Equal(source.Float, target.Float);
        Assert.Equal(source.Clear, target.Clear);
        Assert.Equal(source.BoxSizing, target.BoxSizing);
        Assert.Equal(source.Visibility, target.Visibility);
        Assert.Equal(source.HasZIndex, target.HasZIndex);
        Assert.Equal(source.ZIndex, target.ZIndex);
        Assert.Equal(source.Opacity, target.Opacity);
        Assert.Equal(source.Width, target.Width);
        Assert.Equal(source.Height, target.Height);
        Assert.Equal(source.MinWidth, target.MinWidth);
        Assert.Equal(source.MinHeight, target.MinHeight);
        Assert.Equal(source.MaxWidth, target.MaxWidth);
        Assert.Equal(source.MaxHeight, target.MaxHeight);
        Assert.Equal(source.AspectRatio, target.AspectRatio);
        Assert.Equal(source.Top, target.Top);
        Assert.Equal(source.Right, target.Right);
        Assert.Equal(source.Bottom, target.Bottom);
        Assert.Equal(source.Left, target.Left);
        Assert.Equal(source.Margin, target.Margin);
        Assert.Equal(source.Padding, target.Padding);
        Assert.Equal(source.BorderWidth, target.BorderWidth);
        Assert.Equal(source.BorderStyleTop, target.BorderStyleTop);
        Assert.Equal(source.BorderStyleRight, target.BorderStyleRight);
        Assert.Equal(source.BorderStyleBottom, target.BorderStyleBottom);
        Assert.Equal(source.BorderStyleLeft, target.BorderStyleLeft);
        Assert.Equal(source.BorderColorTop, target.BorderColorTop);
        Assert.Equal(source.BorderColorRight, target.BorderColorRight);
        Assert.Equal(source.BorderColorBottom, target.BorderColorBottom);
        Assert.Equal(source.BorderColorLeft, target.BorderColorLeft);
        Assert.Equal(source.BorderRadius, target.BorderRadius);
        Assert.Equal(source.Color, target.Color);
        Assert.Equal(source.BackgroundColor, target.BackgroundColor);
        Assert.Equal(source.BackgroundImages, target.BackgroundImages);
        Assert.Equal(source.FontFamily, target.FontFamily);
        Assert.Equal(source.FontSize, target.FontSize);
        Assert.Equal(source.FontWeight, target.FontWeight);
        Assert.Equal(source.Italic, target.Italic);
        Assert.Equal(source.LineHeight, target.LineHeight);
        Assert.Equal(source.LetterSpacing, target.LetterSpacing);
        Assert.Equal(source.TextAlign, target.TextAlign);
        Assert.Equal(source.WhiteSpace, target.WhiteSpace);
        Assert.Equal(source.TextOverflow, target.TextOverflow);
        Assert.Equal(source.TextDecoration, target.TextDecoration);
        Assert.Equal(source.TextTransform, target.TextTransform);
        Assert.Equal(source.Direction, target.Direction);
        Assert.Equal(source.WritingMode, target.WritingMode);
        Assert.Equal(source.WebkitLineClamp, target.WebkitLineClamp);
        Assert.Equal(source.OverflowWrap, target.OverflowWrap);
        Assert.Equal(source.FlexDirection, target.FlexDirection);
        Assert.Equal(source.FlexWrap, target.FlexWrap);
        Assert.Equal(source.JustifyContent, target.JustifyContent);
        Assert.Equal(source.AlignItems, target.AlignItems);
        Assert.Equal(source.AlignContent, target.AlignContent);
        Assert.Equal(source.AlignSelf, target.AlignSelf);
        Assert.Equal(source.FlexGrow, target.FlexGrow);
        Assert.Equal(source.FlexShrink, target.FlexShrink);
        Assert.Equal(source.FlexBasis, target.FlexBasis);
        Assert.Equal(source.Order, target.Order);
        Assert.Equal(source.RowGap, target.RowGap);
        Assert.Equal(source.ColumnGap, target.ColumnGap);
        Assert.Same(source.GridTemplateColumns, target.GridTemplateColumns);
        Assert.Same(source.GridTemplateAreas, target.GridTemplateAreas);
        Assert.Equal(source.GridAutoFlow, target.GridAutoFlow);
        Assert.Equal(source.GridColumn, target.GridColumn);
        Assert.Equal(source.GridRow, target.GridRow);
        Assert.Equal(source.GridAreaName, target.GridAreaName);
        Assert.Equal(source.JustifyItems, target.JustifyItems);
        Assert.Equal(source.JustifySelf, target.JustifySelf);
        Assert.Equal(source.JustifyItemsBlock, target.JustifyItemsBlock);
        Assert.Equal(source.OverflowX, target.OverflowX);
        Assert.Equal(source.OverflowY, target.OverflowY);
        Assert.Equal(source.ScrollBehavior, target.ScrollBehavior);
        Assert.Equal(source.ScrollbarTrackColor, target.ScrollbarTrackColor);
        Assert.Equal(source.ScrollbarThumbColor, target.ScrollbarThumbColor);
        Assert.Equal(source.ShowScrollbars, target.ShowScrollbars);
        Assert.Same(source.Transform, target.Transform);
        Assert.Equal(source.TransformOriginX, target.TransformOriginX);
        Assert.Equal(source.TransformOriginY, target.TransformOriginY);
        Assert.Same(source.BoxShadows, target.BoxShadows);
        Assert.Same(source.Filter, target.Filter);
        Assert.Same(source.BackdropFilter, target.BackdropFilter);
        Assert.Same(source.ClipPath, target.ClipPath);
        Assert.Same(source.Mask, target.Mask);
        Assert.Equal(source.MixBlendMode, target.MixBlendMode);
        Assert.Equal(source.Isolate, target.Isolate);
        Assert.Equal(source.OutlineWidth, target.OutlineWidth);
        Assert.Equal(source.OutlineStyle, target.OutlineStyle);
        Assert.Equal(source.OutlineColor, target.OutlineColor);
        Assert.Equal(source.OutlineOffset, target.OutlineOffset);
        Assert.Equal(source.ObjectFit, target.ObjectFit);
        Assert.Equal(source.TableLayout, target.TableLayout);
        Assert.Equal(source.BorderCollapse, target.BorderCollapse);
        Assert.Equal(source.BorderSpacing, target.BorderSpacing);
        Assert.Equal(source.ColumnCount, target.ColumnCount);
        Assert.Equal(source.ColumnGapFallback, target.ColumnGapFallback);
        Assert.Same(source.CustomProperties, target.CustomProperties);
        Assert.Same(source.UnsupportedProperties, target.UnsupportedProperties);
    }

    [Fact]
    public void GridPlacementAutoCoversOneTrackAndHasNoNames()
    {
        var auto = GridPlacement.Auto;

        Assert.True(auto.IsAuto);
        Assert.False(auto.HasNames);
        Assert.Equal(1, auto.Span);
        Assert.Equal(0, auto.Start);
        Assert.Equal(0, auto.End);
    }

    [Theory]
    [InlineData(0, 0, true, true, 3, 3)]
    [InlineData(0, 0, true, true, 0, 1)]
    [InlineData(2, 5, false, false, 0, 3)]
    [InlineData(2, 5, false, false, 10, 3)]
    [InlineData(0, 4, true, false, 2, 2)]
    [InlineData(4, 0, false, true, 2, 2)]
    [InlineData(1, 1, false, false, 0, 1)]
    public void GridPlacementSpanPrefersTheExplicitSpanWhileASideIsAuto(
        int start, int end, bool startAuto, bool endAuto, int spanCount, int expected)
    {
        var placement = new GridPlacement(start, end, startAuto, endAuto, spanCount);
        Assert.Equal(expected, placement.Span);
    }

    [Fact]
    public void GridPlacementNamesAndAutoAreReported()
    {
        var named = new GridPlacement(0, 0, false, true, 0, "content");
        Assert.True(named.HasNames);
        Assert.False(named.IsAuto);

        var spanned = new GridPlacement(0, 0, true, true, 2);
        Assert.True(spanned.IsAuto);
    }
}
