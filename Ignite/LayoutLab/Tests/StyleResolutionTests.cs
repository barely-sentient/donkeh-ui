using Ignite.Data.Dom;

namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Specification tests for the cascade and property parsing. Every case drives
/// real CSS text through the matcher, the cascade and the value parsers before
/// asserting the resolved <see cref="ComputedStyle"/>.
/// </summary>
public class StyleResolutionTests
{
    private static ComputedStyle Child(string parentDeclarations, string childDeclarations)
    {
        var table = TestSupport.Build(
            "#root { " + parentDeclarations + " } #child { " + childDeclarations + " }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = TestSupport.Layout(table, root);
        return TestSupport.Box(result, child).Style;
    }

    // ------------------------------------------------------------- keywords

    [Theory]
    [InlineData("block", Display.Block)]
    [InlineData("inline", Display.Inline)]
    [InlineData("inline-block", Display.InlineBlock)]
    [InlineData("flex", Display.Flex)]
    [InlineData("inline-flex", Display.InlineFlex)]
    [InlineData("grid", Display.Grid)]
    [InlineData("inline-grid", Display.InlineGrid)]
    [InlineData("table", Display.Table)]
    [InlineData("inline-table", Display.InlineTable)]
    [InlineData("table-row-group", Display.TableRowGroup)]
    [InlineData("table-header-group", Display.TableHeaderGroup)]
    [InlineData("table-footer-group", Display.TableFooterGroup)]
    [InlineData("table-row", Display.TableRow)]
    [InlineData("table-cell", Display.TableCell)]
    [InlineData("table-column", Display.TableColumn)]
    [InlineData("table-column-group", Display.TableColumnGroup)]
    [InlineData("table-caption", Display.TableCaption)]
    [InlineData("list-item", Display.ListItem)]
    [InlineData("contents", Display.Contents)]
    [InlineData("flow-root", Display.FlowRoot)]
    [InlineData("none", Display.None)]
    public void DisplayKeywordsMapToTheEnum(string value, Display expected)
    {
        // Resolved without layout so display:none (which produces no box) works.
        Assert.Equal(expected, TestSupport.ResolveStyle(
            $"#root {{ display: {value} }}", TestSupport.Node("div", "root")).Display);
    }

    [Theory]
    [InlineData("static", Position.Static)]
    [InlineData("relative", Position.Relative)]
    [InlineData("absolute", Position.Absolute)]
    [InlineData("fixed", Position.Fixed)]
    [InlineData("sticky", Position.Sticky)]
    [InlineData("unknown", Position.Static)]
    public void PositionKeywordsMapToTheEnum(string value, Position expected)
    {
        Assert.Equal(expected, TestSupport.Style($"position: {value}").Position);
    }

    [Theory]
    [InlineData("left", Float.Left)]
    [InlineData("right", Float.Right)]
    [InlineData("none", Float.None)]
    public void FloatKeywordsMapToTheEnum(string value, Float expected)
    {
        Assert.Equal(expected, TestSupport.Style($"float: {value}").Float);
    }

    [Theory]
    [InlineData("left", Clear.Left)]
    [InlineData("right", Clear.Right)]
    [InlineData("both", Clear.Both)]
    [InlineData("none", Clear.None)]
    public void ClearKeywordsMapToTheEnum(string value, Clear expected)
    {
        Assert.Equal(expected, TestSupport.Style($"clear: {value}").Clear);
    }

    [Theory]
    [InlineData("border-box", BoxSizing.BorderBox)]
    [InlineData("content-box", BoxSizing.ContentBox)]
    public void BoxSizingKeywordsMapToTheEnum(string value, BoxSizing expected)
    {
        Assert.Equal(expected, TestSupport.Style($"box-sizing: {value}").BoxSizing);
    }

    [Theory]
    [InlineData("visible", Visibility.Visible)]
    [InlineData("hidden", Visibility.Hidden)]
    [InlineData("collapse", Visibility.Collapse)]
    public void VisibilityKeywordsMapToTheEnum(string value, Visibility expected)
    {
        Assert.Equal(expected, TestSupport.Style($"visibility: {value}").Visibility);
    }

    [Theory]
    [InlineData("1", 1f)]
    [InlineData("0.5", 0.5f)]
    [InlineData("0", 0f)]
    [InlineData("2", 1f)]
    [InlineData("-1", 0f)]
    public void OpacityIsClampedToZeroOne(string value, float expected)
    {
        Assert.Equal(expected, TestSupport.Style($"opacity: {value}").Opacity, 3);
    }

    [Fact]
    public void ZIndexAutoClearsTheFlagAndNumbersSetIt()
    {
        var auto = TestSupport.Style("z-index: auto");
        Assert.False(auto.HasZIndex);
        Assert.Equal(0, auto.ZIndex);

        var number = TestSupport.Style("z-index: -3");
        Assert.True(number.HasZIndex);
        Assert.Equal(-3, number.ZIndex);

        var rounded = TestSupport.Style("z-index: 4.6");
        Assert.Equal(5, rounded.ZIndex);
    }

    // --------------------------------------------------------------- lengths

    [Theory]
    [InlineData("width: 50%", LengthUnit.Percent, 50f)]
    [InlineData("width: 12.5px", LengthUnit.Px, 12.5f)]
    [InlineData("width: 2em", LengthUnit.Em, 2f)]
    [InlineData("width: 3rem", LengthUnit.Rem, 3f)]
    [InlineData("width: 10vw", LengthUnit.Vw, 10f)]
    [InlineData("width: auto", LengthUnit.Auto, 0f)]
    [InlineData("width: min-content", LengthUnit.MinContent, 0f)]
    [InlineData("width: max-content", LengthUnit.MaxContent, 0f)]
    [InlineData("width: fit-content", LengthUnit.FitContent, 0f)]
    [InlineData("width: stretch", LengthUnit.Stretch, 0f)]
    public void WidthKeepsTheParsedUnit(string declaration, LengthUnit unit, float value)
    {
        var style = TestSupport.Style(declaration);
        Assert.Equal(unit, style.Width.Unit);
        Assert.Equal(value, style.Width.Value, 3);
    }

    [Fact]
    public void EveryBoxSideAcceptsAnIndependentLength()
    {
        var style = TestSupport.Style(
            "top: 1px; right: 2px; bottom: 3px; left: 4px;"
            + "min-width: 5px; min-height: 6px; max-width: 7px; max-height: 8px;");

        Assert.Equal(1, style.Top.Value, 3);
        Assert.Equal(2, style.Right.Value, 3);
        Assert.Equal(3, style.Bottom.Value, 3);
        Assert.Equal(4, style.Left.Value, 3);
        Assert.Equal(5, style.MinWidth.Value, 3);
        Assert.Equal(6, style.MinHeight.Value, 3);
        Assert.Equal(7, style.MaxWidth.Value, 3);
        Assert.Equal(8, style.MaxHeight.Value, 3);
    }

    [Fact]
    public void MaxWidthNoneResetsToAuto()
    {
        var style = TestSupport.Style("max-width: 100px; max-height: 200px; max-width: none; max-height: none;");
        Assert.True(style.MaxWidth.IsAuto);
        Assert.True(style.MaxHeight.IsAuto);
    }

    [Fact]
    public void InsetShorthandExpandsTheFourOffsets()
    {
        var style = TestSupport.Style("inset: 1px 2px 3px 4px");

        Assert.Equal(1, style.Top.Value, 3);
        Assert.Equal(2, style.Right.Value, 3);
        Assert.Equal(3, style.Bottom.Value, 3);
        Assert.Equal(4, style.Left.Value, 3);
    }

    [Fact]
    public void MarginAndPaddingShorthandsAndLonghandsCompose()
    {
        var style = TestSupport.Style(
            "margin: 1px 2px 3px 4px; margin-left: 9px;"
            + "padding: 5px; padding-top: 8px;");

        Assert.Equal(1, style.Margin.Top.Value, 3);
        Assert.Equal(2, style.Margin.Right.Value, 3);
        Assert.Equal(3, style.Margin.Bottom.Value, 3);
        Assert.Equal(9, style.Margin.Left.Value, 3);
        Assert.Equal(8, style.Padding.Top.Value, 3);
        Assert.Equal(5, style.Padding.Right.Value, 3);
        Assert.Equal(5, style.Padding.Bottom.Value, 3);
        Assert.Equal(5, style.Padding.Left.Value, 3);
    }

    [Fact]
    public void AutoMarginsSurviveAsAutoLengths()
    {
        var style = TestSupport.Style("margin: auto 0;");
        Assert.True(style.Margin.Top.IsAuto);
        Assert.True(style.Margin.Bottom.IsAuto);
        Assert.Equal(0, style.Margin.Right.Value, 3);
        Assert.Equal(0, style.Margin.Left.Value, 3);
    }

    [Fact]
    public void AspectRatioAcceptsANumberOrASlashForm()
    {
        Assert.Equal(1.5f, TestSupport.Style("aspect-ratio: 1.5").AspectRatio);
        Assert.Equal(16f / 9f, TestSupport.Style("aspect-ratio: 16 / 9").AspectRatio);
        Assert.Null(TestSupport.Style("aspect-ratio: auto").AspectRatio);
        Assert.Null(TestSupport.Style("aspect-ratio: 1 / 0").AspectRatio);
    }

    // ---------------------------------------------------------------- borders

    [Fact]
    public void BorderShorthandSetsWidthStyleAndColorOnEverySide()
    {
        var style = TestSupport.Style("border: 2px solid #ff0000");

        Assert.Equal(2, style.BorderWidth.Top.Value, 3);
        Assert.Equal(2, style.BorderWidth.Right.Value, 3);
        Assert.Equal(2, style.BorderWidth.Bottom.Value, 3);
        Assert.Equal(2, style.BorderWidth.Left.Value, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleRight);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleBottom);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleLeft);
        Assert.Equal(0xFFFF0000u, style.BorderColorTop);
        Assert.Equal(0xFFFF0000u, style.BorderColorRight);
        Assert.Equal(0xFFFF0000u, style.BorderColorBottom);
        Assert.Equal(0xFFFF0000u, style.BorderColorLeft);
    }

    [Fact]
    public void BorderWidthAndStyleShorthandsExpandOneToFourValues()
    {
        var style = TestSupport.Style(
            "border-width: 1px 2px 3px 4px; border-style: solid dashed dotted double;");

        Assert.Equal(1, style.BorderWidth.Top.Value, 3);
        Assert.Equal(2, style.BorderWidth.Right.Value, 3);
        Assert.Equal(3, style.BorderWidth.Bottom.Value, 3);
        Assert.Equal(4, style.BorderWidth.Left.Value, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(BorderStyle.Dashed, style.BorderStyleRight);
        Assert.Equal(BorderStyle.Dotted, style.BorderStyleBottom);
        Assert.Equal(BorderStyle.Double, style.BorderStyleLeft);
    }

    [Fact]
    public void BorderColorExpandsAndCurrentColorStaysASentinel()
    {
        var explicitColors = TestSupport.Style("border-color: red green blue #112233");
        Assert.Equal(0xFFFF0000u, explicitColors.BorderColorTop);
        Assert.Equal(0xFF008000u, explicitColors.BorderColorRight);
        Assert.Equal(0xFF0000FFu, explicitColors.BorderColorBottom);
        Assert.Equal(0xFF112233u, explicitColors.BorderColorLeft);

        var current = TestSupport.Style("color: red; border-color: currentColor");
        Assert.Equal(ComputedStyle.CurrentColor, current.BorderColorTop);
        Assert.Equal(0xFFFF0000u, current.ResolveBorderColor(current.BorderColorTop));
    }

    [Fact]
    public void IndividualBorderSidesOnlyTouchTheirOwnSide()
    {
        var style = TestSupport.Style(
            "border: 1px solid red;"
            + "border-left: 4px dashed blue;"
            + "border-bottom-width: 7px;"
            + "border-right-style: dotted;"
            + "border-top-color: #abcdef;");

        Assert.Equal(1, style.BorderWidth.Top.Value, 3);
        Assert.Equal(1, style.BorderWidth.Right.Value, 3);
        Assert.Equal(7, style.BorderWidth.Bottom.Value, 3);
        Assert.Equal(4, style.BorderWidth.Left.Value, 3);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleTop);
        Assert.Equal(BorderStyle.Dotted, style.BorderStyleRight);
        Assert.Equal(BorderStyle.Solid, style.BorderStyleBottom);
        Assert.Equal(BorderStyle.Dashed, style.BorderStyleLeft);
        Assert.Equal(0xFFABCDEFu, style.BorderColorTop);
        Assert.Equal(0xFF0000FFu, style.BorderColorLeft);
    }

    [Fact]
    public void BorderRadiusShorthandExpandsAndLonghandsOverride()
    {
        var style = TestSupport.Style(
            "border-radius: 1px 2px 3px 4px; border-top-left-radius: 10px;");

        Assert.Equal(10, style.BorderRadius.TopLeft.Value, 3);
        Assert.Equal(2, style.BorderRadius.TopRight.Value, 3);
        Assert.Equal(3, style.BorderRadius.BottomRight.Value, 3);
        Assert.Equal(4, style.BorderRadius.BottomLeft.Value, 3);
    }

    // ------------------------------------------------------------- background

    [Fact]
    public void BackgroundColorAndImageAreSeparateLonghands()
    {
        var color = TestSupport.Style("background-color: #123456");
        Assert.Equal(0xFF123456u, color.BackgroundColor);

        var image = TestSupport.Style("background-image: url(a.png)");
        Assert.NotNull(image.BackgroundImages);
        Assert.Single(image.BackgroundImages!);
        Assert.Equal(ImageKind.Url, image.BackgroundImages![0].Kind);
        Assert.Equal("a.png", image.BackgroundImages[0].Url);
    }

    [Fact]
    public void BackgroundShorthandResetsAndThenAppliesItsParts()
    {
        var style = TestSupport.Style(
            "background: #00ff00 url(a.png) no-repeat fixed center / cover;");

        Assert.Equal(0xFF00FF00u, style.BackgroundColor);
        Assert.NotNull(style.BackgroundImages);
        Assert.Single(style.BackgroundImages!);
        Assert.Equal(BackgroundRepeat.NoRepeat, style.BackgroundRepeat);
        Assert.True(style.BackgroundFixed);
        Assert.Equal(BackgroundSizeMode.Cover, style.BackgroundSizeMode);
        Assert.Equal(50, style.BackgroundPositionX.Value, 3);
        Assert.Equal(50, style.BackgroundPositionY.Value, 3);
    }

    [Fact]
    public void BackgroundSizeAcceptsCoverContainAndExplicitLengths()
    {
        Assert.Equal(BackgroundSizeMode.Cover, TestSupport.Style("background-size: cover").BackgroundSizeMode);
        Assert.Equal(BackgroundSizeMode.Contain, TestSupport.Style("background-size: contain").BackgroundSizeMode);

        var explicitSize = TestSupport.Style("background-size: 50% 25px");
        Assert.Equal(BackgroundSizeMode.Explicit, explicitSize.BackgroundSizeMode);
        Assert.Equal(LengthUnit.Percent, explicitSize.BackgroundWidth.Unit);
        Assert.Equal(50, explicitSize.BackgroundWidth.Value, 3);
        Assert.Equal(LengthUnit.Px, explicitSize.BackgroundHeight.Unit);
        Assert.Equal(25, explicitSize.BackgroundHeight.Value, 3);
    }

    [Fact]
    public void BackgroundPositionMapsKeywordsToPercentages()
    {
        var style = TestSupport.Style("background-position: right bottom");
        Assert.Equal(100, style.BackgroundPositionX.Value, 3);
        Assert.Equal(100, style.BackgroundPositionY.Value, 3);

        var centered = TestSupport.Style("background-position: center");
        Assert.Equal(50, centered.BackgroundPositionX.Value, 3);
        Assert.Equal(50, centered.BackgroundPositionY.Value, 3);
    }

    [Theory]
    [InlineData("repeat-x", BackgroundRepeat.RepeatX)]
    [InlineData("repeat-y", BackgroundRepeat.RepeatY)]
    [InlineData("no-repeat", BackgroundRepeat.NoRepeat)]
    [InlineData("space", BackgroundRepeat.Space)]
    [InlineData("round", BackgroundRepeat.Round)]
    public void BackgroundRepeatKeywordsMapToTheEnum(string value, BackgroundRepeat expected)
    {
        Assert.Equal(expected, TestSupport.Style($"background-repeat: {value}").BackgroundRepeat);
    }

    [Fact]
    public void BackgroundClipAndOriginAcceptEveryBox()
    {
        var style = TestSupport.Style(
            "background-clip: content-box; background-origin: border-box;");
        Assert.Equal(BackgroundBox.ContentBox, style.BackgroundClip);
        Assert.Equal(BackgroundBox.BorderBox, style.BackgroundOrigin);
    }

    // ------------------------------------------------------------------ text

    [Fact]
    public void FontSizeKeywordsMapToTheCssTable()
    {
        Assert.Equal(16, TestSupport.Style("font-size: medium").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(13, TestSupport.Style("font-size: small").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(10, TestSupport.Style("font-size: x-small").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(9, TestSupport.Style("font-size: xx-small").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(18, TestSupport.Style("font-size: large").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(24, TestSupport.Style("font-size: x-large").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(32, TestSupport.Style("font-size: xx-large").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(1.2f * 16, TestSupport.Style("font-size: larger").ResolveFontSize(new LengthContext()), 3);
        Assert.Equal(0.8f * 16, TestSupport.Style("font-size: smaller").ResolveFontSize(new LengthContext()), 3);
    }

    [Fact]
    public void RelativeFontSizesAreResolvedAgainstTheParent()
    {
        var child = Child("font-size: 20px;", "font-size: 2em;");
        Assert.Equal(40, child.ResolveFontSize(new LengthContext(fontSize: 20)), 3);

        var percent = Child("font-size: 20px;", "font-size: 50%;");
        Assert.Equal(10, percent.ResolveFontSize(new LengthContext(fontSize: 20)), 3);
    }

    [Fact]
    public void FontWeightKeywordsAndNumbersAreSupported()
    {
        Assert.Equal(400, TestSupport.Style("font-weight: normal").FontWeight);
        Assert.Equal(700, TestSupport.Style("font-weight: bold").FontWeight);
        Assert.Equal(700, TestSupport.Style("font-weight: bolder").FontWeight);
        Assert.Equal(300, TestSupport.Style("font-weight: lighter").FontWeight);
        Assert.Equal(600, TestSupport.Style("font-weight: 600").FontWeight);
    }

    [Theory]
    [InlineData("italic", true)]
    [InlineData("oblique", true)]
    [InlineData("normal", false)]
    public void FontStyleSetsTheItalicFlag(string value, bool expected)
    {
        Assert.Equal(expected, TestSupport.Style($"font-style: {value}").Italic);
    }

    [Fact]
    public void FontFamilyIsUnquoted()
    {
        Assert.Equal("Open Sans", TestSupport.Style("font-family: 'Open Sans'").FontFamily);
    }

    [Fact]
    public void FontShorthandParsesStyleWeightSizeLineHeightAndFamily()
    {
        var style = TestSupport.Style("font: italic bold 20px/1.5 serif");

        Assert.True(style.Italic);
        Assert.Equal(700, style.FontWeight);
        Assert.Equal(20, style.FontSize.Resolve(new LengthContext()), 3);
        Assert.Equal(CssLength.Number(1.5f), style.LineHeight);
        Assert.Equal("serif", style.FontFamily);
    }

    [Theory]
    [InlineData("normal", 1.2f)]
    [InlineData("1.8", 1.8f)]
    public void LineHeightAcceptsNormalAndUnitlessNumbers(string value, float expected)
    {
        var style = TestSupport.Style($"line-height: {value}");
        Assert.Equal(LengthUnit.Number, style.LineHeight.Unit);
        Assert.Equal(expected, style.LineHeight.Value, 3);
    }

    [Fact]
    public void LineHeightAcceptsLengthsAndPercentages()
    {
        Assert.Equal(LengthUnit.Px, TestSupport.Style("line-height: 24px").LineHeight.Unit);
        Assert.Equal(LengthUnit.Percent, TestSupport.Style("line-height: 150%").LineHeight.Unit);
    }

    [Fact]
    public void LetterSpacingNormalIsZeroAndLengthsAreKept()
    {
        Assert.Equal(CssLength.Zero, TestSupport.Style("letter-spacing: normal").LetterSpacing);
        Assert.Equal(2.5f, TestSupport.Style("letter-spacing: 2.5px").LetterSpacing.Value, 3);
    }

    [Theory]
    [InlineData("left", TextAlign.Left)]
    [InlineData("right", TextAlign.Right)]
    [InlineData("center", TextAlign.Center)]
    [InlineData("justify", TextAlign.Justify)]
    [InlineData("end", TextAlign.End)]
    [InlineData("match-parent", TextAlign.MatchParent)]
    [InlineData("start", TextAlign.Start)]
    public void TextAlignKeywordsMapToTheEnum(string value, TextAlign expected)
    {
        Assert.Equal(expected, TestSupport.Style($"text-align: {value}").TextAlign);
    }

    [Fact]
    public void TextDecorationCombinesEveryKeyword()
    {
        Assert.Equal(
            TextDecoration.Underline | TextDecoration.Overline
                | TextDecoration.LineThrough | TextDecoration.Blink,
            TestSupport.Style("text-decoration: underline overline line-through blink").TextDecoration);
        Assert.Equal(TextDecoration.None, TestSupport.Style("text-decoration: none").TextDecoration);
    }

    [Theory]
    [InlineData("uppercase", TextTransform.Uppercase)]
    [InlineData("lowercase", TextTransform.Lowercase)]
    [InlineData("capitalize", TextTransform.Capitalize)]
    [InlineData("none", TextTransform.None)]
    public void TextTransformKeywordsMapToTheEnum(string value, TextTransform expected)
    {
        Assert.Equal(expected, TestSupport.Style($"text-transform: {value}").TextTransform);
    }

    [Theory]
    [InlineData("normal", WhiteSpace.Normal)]
    [InlineData("nowrap", WhiteSpace.NoWrap)]
    [InlineData("pre", WhiteSpace.Pre)]
    [InlineData("pre-wrap", WhiteSpace.PreWrap)]
    [InlineData("pre-line", WhiteSpace.PreLine)]
    [InlineData("break-spaces", WhiteSpace.BreakSpaces)]
    public void WhiteSpaceKeywordsMapToTheEnum(string value, WhiteSpace expected)
    {
        Assert.Equal(expected, TestSupport.Style($"white-space: {value}").WhiteSpace);
    }

    [Fact]
    public void OverflowWrapAndWordBreakAreSupported()
    {
        Assert.Equal(OverflowWrap.BreakWord, TestSupport.Style("overflow-wrap: break-word").OverflowWrap);
        Assert.Equal(OverflowWrap.Anywhere, TestSupport.Style("overflow-wrap: anywhere").OverflowWrap);
        Assert.Equal(OverflowWrap.Normal, TestSupport.Style("overflow-wrap: normal").OverflowWrap);
        Assert.Equal(OverflowWrap.Anywhere, TestSupport.Style("word-break: break-all").OverflowWrap);
    }

    [Fact]
    public void TextOverflowEllipsisIsSupported()
    {
        Assert.Equal(TextOverflow.Ellipsis, TestSupport.Style("text-overflow: ellipsis").TextOverflow);
        Assert.Equal(TextOverflow.Clip, TestSupport.Style("text-overflow: clip").TextOverflow);
    }

    [Fact]
    public void DirectionWritingModeAndLineClampAreSupported()
    {
        Assert.Equal(Direction.RightToLeft, TestSupport.Style("direction: rtl").Direction);
        Assert.Equal(WritingMode.VerticalRightToLeft,
            TestSupport.Style("writing-mode: vertical-rl").WritingMode);
        Assert.Equal(WritingMode.VerticalLeftToRight,
            TestSupport.Style("writing-mode: vertical-lr").WritingMode);
        Assert.Equal(3, TestSupport.Style("-webkit-line-clamp: 3").WebkitLineClamp);
        Assert.Equal(0, TestSupport.Style("-webkit-line-clamp: -2").WebkitLineClamp);
    }

    // ------------------------------------------------------------------ flex

    [Fact]
    public void FlexDirectionAndWrapKeywordsMap()
    {
        Assert.Equal(FlexDirection.RowReverse, TestSupport.Style("flex-direction: row-reverse").FlexDirection);
        Assert.Equal(FlexDirection.Column, TestSupport.Style("flex-direction: column").FlexDirection);
        Assert.Equal(FlexDirection.ColumnReverse, TestSupport.Style("flex-direction: column-reverse").FlexDirection);
        Assert.Equal(FlexWrap.Wrap, TestSupport.Style("flex-wrap: wrap").FlexWrap);
        Assert.Equal(FlexWrap.WrapReverse, TestSupport.Style("flex-wrap: wrap-reverse").FlexWrap);
    }

    [Fact]
    public void FlexFlowSetsDirectionAndWrapAtOnce()
    {
        var style = TestSupport.Style("flex-flow: column wrap");
        Assert.Equal(FlexDirection.Column, style.FlexDirection);
        Assert.Equal(FlexWrap.Wrap, style.FlexWrap);
    }

    [Theory]
    [InlineData("flex-start", JustifyContent.FlexStart)]
    [InlineData("flex-end", JustifyContent.FlexEnd)]
    [InlineData("center", JustifyContent.Center)]
    [InlineData("space-between", JustifyContent.SpaceBetween)]
    [InlineData("space-around", JustifyContent.SpaceAround)]
    [InlineData("space-evenly", JustifyContent.SpaceEvenly)]
    [InlineData("start", JustifyContent.Start)]
    [InlineData("end", JustifyContent.End)]
    [InlineData("left", JustifyContent.Left)]
    [InlineData("right", JustifyContent.Right)]
    [InlineData("stretch", JustifyContent.Stretch)]
    public void JustifyContentKeywordsMapToTheEnum(string value, JustifyContent expected)
    {
        Assert.Equal(expected, TestSupport.Style($"justify-content: {value}").JustifyContent);
    }

    [Theory]
    [InlineData("stretch", AlignItems.Stretch)]
    [InlineData("flex-start", AlignItems.FlexStart)]
    [InlineData("flex-end", AlignItems.FlexEnd)]
    [InlineData("center", AlignItems.Center)]
    [InlineData("baseline", AlignItems.Baseline)]
    [InlineData("start", AlignItems.Start)]
    [InlineData("end", AlignItems.End)]
    [InlineData("self-start", AlignItems.SelfStart)]
    [InlineData("self-end", AlignItems.SelfEnd)]
    public void AlignItemsKeywordsMapToTheEnum(string value, AlignItems expected)
    {
        Assert.Equal(expected, TestSupport.Style($"align-items: {value}").AlignItems);
    }

    [Theory]
    [InlineData("stretch", AlignContent.Stretch)]
    [InlineData("flex-start", AlignContent.FlexStart)]
    [InlineData("flex-end", AlignContent.FlexEnd)]
    [InlineData("center", AlignContent.Center)]
    [InlineData("space-between", AlignContent.SpaceBetween)]
    [InlineData("space-around", AlignContent.SpaceAround)]
    [InlineData("space-evenly", AlignContent.SpaceEvenly)]
    [InlineData("start", AlignContent.Start)]
    [InlineData("end", AlignContent.End)]
    public void AlignContentKeywordsMapToTheEnum(string value, AlignContent expected)
    {
        Assert.Equal(expected, TestSupport.Style($"align-content: {value}").AlignContent);
    }

    [Theory]
    [InlineData("auto", AlignSelf.Auto)]
    [InlineData("stretch", AlignSelf.Stretch)]
    [InlineData("flex-start", AlignSelf.FlexStart)]
    [InlineData("flex-end", AlignSelf.FlexEnd)]
    [InlineData("center", AlignSelf.Center)]
    [InlineData("baseline", AlignSelf.Baseline)]
    [InlineData("start", AlignSelf.Start)]
    [InlineData("end", AlignSelf.End)]
    [InlineData("self-start", AlignSelf.SelfStart)]
    [InlineData("self-end", AlignSelf.SelfEnd)]
    public void AlignSelfKeywordsMapToTheEnum(string value, AlignSelf expected)
    {
        Assert.Equal(expected, TestSupport.Style($"align-self: {value}").AlignSelf);
    }

    [Fact]
    public void FlexShorthandHandlesNoneAutoAndNumericForms()
    {
        var none = TestSupport.Style("flex: none");
        Assert.Equal(0, none.FlexGrow, 3);
        Assert.Equal(0, none.FlexShrink, 3);
        Assert.True(none.FlexBasis.IsAuto);

        var auto = TestSupport.Style("flex: auto");
        Assert.Equal(1, auto.FlexGrow, 3);
        Assert.Equal(1, auto.FlexShrink, 3);
        Assert.True(auto.FlexBasis.IsAuto);

        var one = TestSupport.Style("flex: 2");
        Assert.Equal(2, one.FlexGrow, 3);
        Assert.Equal(1, one.FlexShrink, 3);
        Assert.False(one.FlexBasis.IsAuto);
        Assert.Equal(0, one.FlexBasis.Value, 3);

        var full = TestSupport.Style("flex: 1 0 100px");
        Assert.Equal(1, full.FlexGrow, 3);
        Assert.Equal(0, full.FlexShrink, 3);
        Assert.Equal(100, full.FlexBasis.Value, 3);
    }

    [Fact]
    public void FlexLonghandsClampNegativeValues()
    {
        var style = TestSupport.Style("flex-grow: -2; flex-shrink: -3; order: 2.6");
        Assert.Equal(0, style.FlexGrow, 3);
        Assert.Equal(0, style.FlexShrink, 3);
        Assert.Equal(3, style.Order);
    }

    [Fact]
    public void FlexBasisContentBecomesMaxContent()
    {
        Assert.Equal(CssLength.MaxContent, TestSupport.Style("flex-basis: content").FlexBasis);
    }

    [Fact]
    public void GapShorthandSetsBothAxes()
    {
        var style = TestSupport.Style("gap: 10px 20px");
        Assert.Equal(10, style.RowGap.Value, 3);
        Assert.Equal(20, style.ColumnGap.Value, 3);
    }

    [Fact]
    public void RowAndColumnGapNormaliseToZero()
    {
        var style = TestSupport.Style("row-gap: normal; column-gap: 4px");
        Assert.Equal(CssLength.Zero, style.RowGap);
        Assert.Equal(4, style.ColumnGap.Value, 3);
    }

    // ------------------------------------------------------------------ grid

    [Fact]
    public void GridTemplateTracksParseIntoTheTrackList()
    {
        var style = TestSupport.Style("grid-template-columns: 100px 1fr auto;"
            + "grid-template-rows: repeat(2, 50px);");

        Assert.NotNull(style.GridTemplateColumns);
        Assert.Equal(3, style.GridTemplateColumns!.Count);
        Assert.Equal(GridTrackKind.Fixed, style.GridTemplateColumns.Tracks[0].Kind);
        Assert.Equal(GridTrackKind.Fr, style.GridTemplateColumns.Tracks[1].Kind);
        Assert.Equal(GridTrackKind.Auto, style.GridTemplateColumns.Tracks[2].Kind);

        Assert.NotNull(style.GridTemplateRows);
        Assert.Equal(2, style.GridTemplateRows!.Count);
    }

    [Fact]
    public void GridAutoTracksAndFlowAreSupported()
    {
        var style = TestSupport.Style(
            "grid-auto-columns: 30px; grid-auto-rows: 40px; grid-auto-flow: column dense;");

        Assert.NotNull(style.GridAutoColumns);
        Assert.Equal(30, style.GridAutoColumns!.Tracks[0].Minimum.Value, 3);
        Assert.NotNull(style.GridAutoRows);
        Assert.Equal(40, style.GridAutoRows!.Tracks[0].Minimum.Value, 3);
        Assert.Equal(GridAutoFlow.Column | GridAutoFlow.Dense, style.GridAutoFlow);
    }

    [Fact]
    public void GridTemplateShorthandSetsColumnsAndRows()
    {
        var style = TestSupport.Style("grid-template: 1fr 2fr / 3fr");
        Assert.NotNull(style.GridTemplateColumns);
        Assert.Equal(2, style.GridTemplateColumns!.Count);
        Assert.NotNull(style.GridTemplateRows);
        Assert.Single(style.GridTemplateRows!.Tracks);
    }

    [Fact]
    public void GridPlacementLonghandsAreParsed()
    {
        var style = TestSupport.Style("grid-column: 1 / span 2; grid-row: 3 / 5;");

        Assert.Equal(1, style.GridColumn.Start);
        Assert.True(style.GridColumn.EndAuto);
        Assert.Equal(2, style.GridColumn.SpanCount);
        Assert.Equal(3, style.GridRow.Start);
        Assert.Equal(5, style.GridRow.End);
    }

    [Fact]
    public void GridLineLonghandsUpdateOneSide()
    {
        var style = TestSupport.Style(
            "grid-column-start: 2; grid-column-end: span 3;"
            + "grid-row-start: 4; grid-row-end: 6;");

        Assert.Equal(2, style.GridColumn.Start);
        Assert.False(style.GridColumn.StartAuto);
        Assert.True(style.GridColumn.EndAuto);
        Assert.Equal(4, style.GridRow.Start);
        Assert.Equal(6, style.GridRow.End);
    }

    [Fact]
    public void GridAreaShorthandSetsBothAxes()
    {
        var style = TestSupport.Style("grid-area: 1 / 2 / 3 / 4");

        Assert.Equal(1, style.GridRow.Start);
        Assert.Equal(3, style.GridRow.End);
        Assert.Equal(2, style.GridColumn.Start);
        Assert.Equal(4, style.GridColumn.End);
    }

    [Fact]
    public void GridAreaNamedFormSetsTheAreaName()
    {
        var style = TestSupport.Style("grid-area: header");
        Assert.Equal("header", style.GridAreaName);
        Assert.True(style.GridRow.IsAuto);
        Assert.True(style.GridColumn.IsAuto);
    }

    [Fact]
    public void GridTemplateAreasAreParsed()
    {
        var style = TestSupport.Style("grid-template-areas: 'a a' 'b c'");

        Assert.NotNull(style.GridTemplateAreas);
        Assert.Equal(2, style.GridTemplateAreas!.RowCount);
        Assert.Equal(2, style.GridTemplateAreas.ColumnCount);
        Assert.True(style.GridTemplateAreas.Areas.ContainsKey("a"));
    }

    [Fact]
    public void JustifyItemsAndSelfKeywordsMap()
    {
        Assert.Equal(JustifyItems.Center, TestSupport.Style("justify-items: center").JustifyItems);
        Assert.Equal(JustifyItems.SelfEnd, TestSupport.Style("justify-items: self-end").JustifyItems);
        Assert.Equal(JustifySelf.Stretch, TestSupport.Style("justify-self: stretch").JustifySelf);
        Assert.Equal(JustifySelf.SelfStart, TestSupport.Style("justify-self: self-start").JustifySelf);
    }

    [Fact]
    public void PlaceItemsSetsAlignAndJustify()
    {
        var style = TestSupport.Style("place-items: center end");
        Assert.Equal(AlignItems.Center, style.AlignItems);
        Assert.Equal(JustifyItems.End, style.JustifyItems);
    }

    [Fact]
    public void PlaceContentSetsAlignContent()
    {
        Assert.Equal(AlignContent.SpaceBetween,
            TestSupport.Style("place-content: space-between").AlignContent);
    }

    // -------------------------------------------------------------- overflow

    [Fact]
    public void OverflowSingleValueAppliesToBothAxes()
    {
        var style = TestSupport.Style("overflow: hidden");
        Assert.Equal(Overflow.Hidden, style.OverflowX);
        Assert.Equal(Overflow.Hidden, style.OverflowY);
    }

    [Fact]
    public void OverflowTwoValuesApplyPerAxis()
    {
        var style = TestSupport.Style("overflow: hidden auto");
        Assert.Equal(Overflow.Hidden, style.OverflowX);
        Assert.Equal(Overflow.Auto, style.OverflowY);

        var clip = TestSupport.Style("overflow-x: clip; overflow-y: scroll;");
        Assert.Equal(Overflow.Clip, clip.OverflowX);
        Assert.Equal(Overflow.Scroll, clip.OverflowY);
    }

    // ------------------------------------------------------------- paint-ish

    [Fact]
    public void TransformParsesItsFunctionList()
    {
        var style = TestSupport.Style("transform: translateX(10px) rotate(45deg)");
        Assert.NotNull(style.Transform);
        Assert.False(style.Transform!.IsIdentity);
        Assert.Equal(2, style.Transform.Operations.Length);

        Assert.Null(TestSupport.Style("transform: none").Transform);
    }

    [Fact]
    public void TransformOriginMapsKeywordsAndLengths()
    {
        var keywords = TestSupport.Style("transform-origin: left top");
        Assert.Equal(0, keywords.TransformOriginX.Value, 3);
        Assert.Equal(0, keywords.TransformOriginY.Value, 3);

        var lengths = TestSupport.Style("transform-origin: 10px 20px");
        Assert.Equal(10, lengths.TransformOriginX.Value, 3);
        Assert.Equal(20, lengths.TransformOriginY.Value, 3);
    }

    [Fact]
    public void BoxShadowParsesEachLayer()
    {
        var style = TestSupport.Style(
            "box-shadow: 1px 2px 3px 4px #112233, inset 5px 6px red");

        Assert.NotNull(style.BoxShadows);
        Assert.Equal(2, style.BoxShadows!.Length);
        Assert.Equal(1, style.BoxShadows[0].OffsetX, 3);
        Assert.Equal(2, style.BoxShadows[0].OffsetY, 3);
        Assert.Equal(3, style.BoxShadows[0].Blur, 3);
        Assert.Equal(4, style.BoxShadows[0].Spread, 3);
        Assert.Equal(0xFF112233u, style.BoxShadows[0].Color);
        Assert.False(style.BoxShadows[0].Inset);
        Assert.True(style.BoxShadows[1].Inset);
    }

    [Fact]
    public void OutlineShorthandSetsItsPartsAndLonghandsOverride()
    {
        var style = TestSupport.Style(
            "outline: 2px dashed red; outline-offset: 3px; outline-color: #00ff00;");

        Assert.Equal(2, style.OutlineWidth.Value, 3);
        Assert.Equal(BorderStyle.Dashed, style.OutlineStyle);
        Assert.Equal(0xFF00FF00u, style.OutlineColor);
        Assert.Equal(3, style.OutlineOffset.Value, 3);
    }

    [Fact]
    public void FilterAndBackdropFilterParseChains()
    {
        var filter = TestSupport.Style("filter: blur(2px) brightness(1.5)");
        Assert.NotNull(filter.Filter);
        Assert.Equal(2, filter.Filter!.Functions.Length);
        Assert.Equal(FilterFunctionKind.Blur, filter.Filter.Functions[0].Kind);
        Assert.Equal(2, filter.Filter.Functions[0].Amount, 3);
        Assert.Equal(FilterFunctionKind.Brightness, filter.Filter.Functions[1].Kind);
        Assert.Equal(1.5f, filter.Filter.Functions[1].Amount, 3);

        var backdrop = TestSupport.Style("backdrop-filter: grayscale(50%)");
        Assert.NotNull(backdrop.BackdropFilter);
        Assert.Equal(FilterFunctionKind.Grayscale, backdrop.BackdropFilter!.Functions[0].Kind);
    }

    [Fact]
    public void ClipPathParsesItsShape()
    {
        var circle = TestSupport.Style("clip-path: circle(40%)");
        Assert.NotNull(circle.ClipPath);
        Assert.Equal(ClipPathKind.Circle, circle.ClipPath!.Kind);
        Assert.Equal(40, circle.ClipPath.RadiusX.Value, 3);

        var inset = TestSupport.Style("clip-path: inset(10px 20px)");
        Assert.Equal(ClipPathKind.Inset, inset.ClipPath!.Kind);
        Assert.Equal(10, inset.ClipPath.InsetTop.Value, 3);
        Assert.Equal(20, inset.ClipPath.InsetRight.Value, 3);

        Assert.True(TestSupport.Style("clip-path: none").ClipPath!.IsNone);
    }

    [Fact]
    public void MaskParsesAnImage()
    {
        var style = TestSupport.Style("mask: url(mask.svg)");
        Assert.NotNull(style.Mask);
        Assert.Equal(ImageKind.Url, style.Mask!.Kind);
        Assert.Equal("mask.svg", style.Mask.Source);
    }

    [Theory]
    [InlineData("multiply", MixBlendMode.Multiply)]
    [InlineData("screen", MixBlendMode.Screen)]
    [InlineData("overlay", MixBlendMode.Overlay)]
    [InlineData("darken", MixBlendMode.Darken)]
    [InlineData("lighten", MixBlendMode.Lighten)]
    [InlineData("color-dodge", MixBlendMode.ColorDodge)]
    [InlineData("color-burn", MixBlendMode.ColorBurn)]
    [InlineData("hard-light", MixBlendMode.HardLight)]
    [InlineData("soft-light", MixBlendMode.SoftLight)]
    [InlineData("difference", MixBlendMode.Difference)]
    [InlineData("exclusion", MixBlendMode.Exclusion)]
    [InlineData("hue", MixBlendMode.Hue)]
    [InlineData("saturation", MixBlendMode.Saturation)]
    [InlineData("color", MixBlendMode.Color)]
    [InlineData("luminosity", MixBlendMode.Luminosity)]
    [InlineData("plus-lighter", MixBlendMode.PlusLighter)]
    public void MixBlendModeKeywordsMapToTheEnum(string value, MixBlendMode expected)
    {
        Assert.Equal(expected, TestSupport.Style($"mix-blend-mode: {value}").MixBlendMode);
    }

    [Fact]
    public void IsolationSetsTheFlag()
    {
        Assert.True(TestSupport.Style("isolation: isolate").Isolate);
        Assert.False(TestSupport.Style("isolation: auto").Isolate);
    }

    // ------------------------------------------------------------- misc css

    [Theory]
    [InlineData("fill", ObjectFit.Fill)]
    [InlineData("contain", ObjectFit.Contain)]
    [InlineData("cover", ObjectFit.Cover)]
    [InlineData("none", ObjectFit.None)]
    [InlineData("scale-down", ObjectFit.ScaleDown)]
    public void ObjectFitKeywordsMapToTheEnum(string value, ObjectFit expected)
    {
        Assert.Equal(expected, TestSupport.Style($"object-fit: {value}").ObjectFit);
    }

    [Fact]
    public void TablePropertiesAreSupported()
    {
        var style = TestSupport.Style(
            "table-layout: fixed; border-collapse: collapse; border-spacing: 4px;");

        Assert.Equal(TableLayout.Fixed, style.TableLayout);
        Assert.Equal(BorderCollapse.Collapse, style.BorderCollapse);
        Assert.Equal(4, style.BorderSpacing.Value, 3);
    }

    [Fact]
    public void ColumnCountIsClampedToOne()
    {
        Assert.Equal(3, TestSupport.Style("column-count: 3").ColumnCount);
        Assert.Equal(1, TestSupport.Style("column-count: 0").ColumnCount);
    }

    [Fact]
    public void ScrollbarColorAndWidthAreSupported()
    {
        var color = TestSupport.Style("scrollbar-color: red blue");
        Assert.Equal(0xFFFF0000u, color.ScrollbarThumbColor);
        Assert.Equal(0xFF0000FFu, color.ScrollbarTrackColor);

        Assert.True(TestSupport.Style("scrollbar-width: thin").ShowScrollbars);
        Assert.False(TestSupport.Style("scrollbar-width: none").ShowScrollbars);
    }

    [Fact]
    public void ScrollBehaviorIsSupported()
    {
        Assert.Equal(ScrollBehavior.Auto, new ComputedStyle().ScrollBehavior);
        Assert.Equal(ScrollBehavior.Smooth, TestSupport.Style("scroll-behavior: smooth").ScrollBehavior);
        Assert.Equal(ScrollBehavior.Auto, TestSupport.Style("scroll-behavior: auto").ScrollBehavior);
    }

    [Fact]
    public void IgnoredPropertiesDoNotBecomeUnsupported()
    {
        var style = TestSupport.Style(
            "pointer-events: none; cursor: pointer; user-select: none;"
            + " will-change: transform; contain: layout; content: 'x'; object-position: center;");
        Assert.Null(style.UnsupportedProperties);
    }

    [Fact]
    public void UnknownPropertiesAreRecorded()
    {
        var style = TestSupport.Style("-webkit-magic: 1; some-future-property: on;");
        Assert.NotNull(style.UnsupportedProperties);
        Assert.Equal(2, style.UnsupportedProperties!.Count);
    }

    // --------------------------------------------------------- wide keywords

    [Fact]
    public void InitialResetsAPropertyToItsInitialValue()
    {
        var style = TestSupport.Style("width: 100px; margin: 5px; width: initial; margin: initial;");
        Assert.True(style.Width.IsAuto);
        Assert.Equal(CssLength4.Zero, style.Margin);
    }

    [Fact]
    public void InheritPullsTheParentValueForANonInheritedProperty()
    {
        var child = Child("width: 123px; display: flex;", "width: inherit; display: inherit;");
        Assert.Equal(123, child.Width.Value, 3);
        Assert.Equal(Display.Flex, child.Display);
    }

    [Fact]
    public void UnsetUsesInheritanceForInheritedPropertiesOnly()
    {
        var inherited = Child("color: red; width: 50px;", "color: unset; width: unset;");
        Assert.Equal(0xFFFF0000u, inherited.Color);
        Assert.True(inherited.Width.IsAuto);

        var explicitInitial = Child("color: red; font-weight: 700;",
            "color: initial; font-weight: initial;");
        Assert.Equal(CssColor.Black, explicitInitial.Color);
        Assert.Equal(400, explicitInitial.FontWeight);
    }

    [Fact]
    public void RevertBehavesLikeUnset()
    {
        var style = Child("width: 50px; color: red;", "width: revert; color: revert;");
        Assert.True(style.Width.IsAuto);
        Assert.Equal(0xFFFF0000u, style.Color);
    }

    [Fact]
    public void AllInheritCopiesTheParentStyle()
    {
        var style = Child("display: flex; width: 80px; color: red;", "all: inherit;");

        Assert.Equal(Display.Flex, style.Display);
        Assert.Equal(80, style.Width.Value, 3);
        Assert.Equal(0xFFFF0000u, style.Color);
    }

    [Fact]
    public void AllInitialResetsEverything()
    {
        var style = Child("", "display: flex; width: 80px; margin: 4px; all: initial;");
        Assert.Equal(Display.Block, style.Display);
        Assert.True(style.Width.IsAuto);
        Assert.Equal(CssLength4.Zero, style.Margin);
    }

    // ------------------------------------------------------- cascade extras

    [Fact]
    public void ImportantDeclarationsWinOverLaterNormalOnes()
    {
        var style = TestSupport.Style("color: red !important; color: blue;");
        Assert.Equal(0xFFFF0000u, style.Color);
    }

    [Fact]
    public void LaterNormalDeclarationsOverrideEarlierOnes()
    {
        var style = TestSupport.Style("color: red; color: blue;");
        Assert.Equal(0xFF0000FFu, style.Color);
    }

    [Fact]
    public void IdSelectorsOutrankTypeSelectorsRegardlessOfOrder()
    {
        var style = TestSupport.StyleOf(
            "div { color: red; } #root { color: blue; }", "root");
        Assert.Equal(0xFF0000FFu, style.Color);
    }

    [Fact]
    public void CustomPropertiesResolveThroughVar()
    {
        var style = TestSupport.Style("--accent: #ff0000; color: var(--accent);");
        Assert.Equal(0xFFFF0000u, style.Color);
    }

    [Fact]
    public void MissingVariablesUseTheFallback()
    {
        var style = TestSupport.Style("color: var(--missing, #00ff00);");
        Assert.Equal(0xFF00FF00u, style.Color);
    }

    [Fact]
    public void InheritedCustomPropertiesAreAvailableToChildren()
    {
        var style = Child("--accent: #0000ff;", "color: var(--accent);");
        Assert.Equal(0xFF0000FFu, style.Color);
    }

    [Fact]
    public void InheritedTextPropertiesFlowToChildren()
    {
        var style = Child(
            "color: #123456; font-family: serif; font-size: 18px;"
            + " font-weight: 700; text-align: center; white-space: pre;",
            "display: block;");

        Assert.Equal(0xFF123456u, style.Color);
        Assert.Equal("serif", style.FontFamily);
        Assert.Equal(18, style.FontSize.Resolve(new LengthContext()), 3);
        Assert.Equal(700, style.FontWeight);
        Assert.Equal(TextAlign.Center, style.TextAlign);
        Assert.Equal(WhiteSpace.Pre, style.WhiteSpace);
    }
}
