using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for normal block flow: used sizes, the box model,
/// margin collapsing, floats, relative/absolute positioning and scrolling.
/// All cases run real CSS through the compiler, cascade and layout engine.
/// </summary>
public class BlockLayoutTests
{
    private static LayoutResult Lay(string css, IgniteSupportedDomNode root,
        float width = 1000, float height = 800)
    {
        var options = TestSupport.Options(width, height);
        options.TextMeasurer = TestSupport.FixedMeasurer.Instance;
        return TestSupport.Layout(TestSupport.Build(css), root, options);
    }

    // ------------------------------------------------------------ box model

    [Fact]
    public void RootAutoWidthFillsTheViewport()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { }", root);

        TestSupport.AssertRect(TestSupport.Box(result, root).BorderBox, 0, 0, 1000, 0);
    }

    [Fact]
    public void ExplicitSizePlusPaddingAndBorderUsesContentBoxSizing()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 200px; height: 100px; padding: 10px; border: 5px solid #000; }",
            root);
        var box = TestSupport.Box(result, root);

        TestSupport.AssertRect(box.BorderBox, 0, 0, 230, 130);
        TestSupport.AssertRect(box.PaddingBox, 5, 5, 220, 120);
        TestSupport.AssertRect(box.ContentBox, 15, 15, 200, 100);
    }

    [Fact]
    public void BorderBoxSizingSubtractsPaddingAndBorder()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { box-sizing: border-box; width: 200px; height: 100px;"
            + " padding: 10px; border: 5px solid #000; }",
            root);
        var box = TestSupport.Box(result, root);

        TestSupport.AssertRect(box.BorderBox, 0, 0, 200, 100);
        TestSupport.AssertRect(box.ContentBox, 15, 15, 170, 70);
    }

    [Fact]
    public void BorderlessWidthResolvesToZero()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { border-width: 10px; width: 50px; height: 50px; }", root);
        var box = TestSupport.Box(result, root);

        TestSupport.AssertRect(box.BorderBox, 0, 0, 50, 50);
        Assert.Equal(0, box.Border.Left);
    }

    [Fact]
    public void RootMarginsShiftAndShrinkTheBox()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { margin: 20px; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, root).BorderBox, 20, 20, 960, 0);
    }

    [Fact]
    public void ChildAutoWidthFillsTheParentContentBox()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay("#root { padding: 25px; } #child { height: 10px; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, child).BorderBox, 25, 25, 950, 10);
    }

    [Fact]
    public void PercentageWidthUsesTheContainingBlockWidth()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay("#child { width: 50%; height: 10px; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, child).BorderBox, 0, 0, 500, 10);
    }

    [Fact]
    public void PercentagePaddingResolvesAgainstTheWidth()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay("#child { width: 100px; padding-left: 10%; height: 10px; }", root);
        var box = TestSupport.Box(result, child);

        Assert.Equal(100, box.Padding.Left, 3);
        TestSupport.AssertRect(box.BorderBox, 0, 0, 200, 10);
    }

    [Fact]
    public void AutoMarginsCenterAFixedWidthBlock()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay("#child { width: 200px; height: 10px; margin: 0 auto; }", root);
        var box = TestSupport.Box(result, child);

        Assert.Equal(400, box.Margin.Left, 3);
        Assert.Equal(400, box.Margin.Right, 3);
        TestSupport.AssertRect(box.BorderBox, 400, 0, 200, 10);
    }

    [Fact]
    public void MinAndMaxWidthClampTheUsedWidth()
    {
        var root = TestSupport.Node("div", "root");
        var small = TestSupport.Node("div", "small");
        var large = TestSupport.Node("div", "large");
        root.Children = [small, large];

        var result = Lay(
            "#small { width: 100px; min-width: 150px; height: 10px; }"
            + " #large { width: 300px; max-width: 200px; height: 10px; }",
            root);

        Assert.Equal(150, TestSupport.Box(result, small).BorderBox.Width, 3);
        Assert.Equal(200, TestSupport.Box(result, large).BorderBox.Width, 3);
    }

    [Fact]
    public void ContentBoxMinWidthIncludesPaddingAndBorder()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#child { width: 100px; min-width: 150px; padding: 10px;"
            + " border: 5px solid #000; height: 10px; }",
            root);
        var box = TestSupport.Box(result, child);

        Assert.Equal(180, box.BorderBox.Width, 3);
        Assert.Equal(150, box.ContentBox.Width, 3);
    }

    [Fact]
    public void MinAndMaxHeightClampTheUsedHeight()
    {
        var root = TestSupport.Node("div", "root");
        var shortBox = TestSupport.Node("div", "short");
        var tall = TestSupport.Node("div", "tall");
        root.Children = [shortBox, tall];

        var result = Lay(
            "#short { height: 10px; min-height: 50px; }"
            + " #tall { height: 500px; max-height: 100px; }",
            root);

        Assert.Equal(50, TestSupport.Box(result, shortBox).BorderBox.Height, 3);
        Assert.Equal(100, TestSupport.Box(result, tall).BorderBox.Height, 3);
    }

    [Fact]
    public void AspectRatioDerivesTheMissingDimension()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay("#child { width: 200px; aspect-ratio: 2; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, child).BorderBox, 0, 0, 200, 100);
    }

    [Fact]
    public void ExplicitHeightDrivesTheBoxAndOverflowingChildrenAreNotCounted()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        var grandchild = TestSupport.Node("div", "grandchild");
        root.Children = [child];
        child.Children = [grandchild];

        var result = Lay(
            "#child { height: 60px; } #grandchild { height: 300px; }",
            root);

        Assert.Equal(60, TestSupport.Box(result, child).BorderBox.Height, 3);
        Assert.Equal(300, TestSupport.Box(result, grandchild).BorderBox.Height, 3);
    }

    // ------------------------------------------------------------ block flow

    [Fact]
    public void BlockChildrenStackVerticallyAndGrowTheParent()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var second = TestSupport.Node("div", "second");
        root.Children = [first, second];

        var result = Lay(
            "#first { height: 50px; } #second { height: 30px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, first).BorderBox, 0, 0, 1000, 50);
        TestSupport.AssertRect(TestSupport.Box(result, second).BorderBox, 0, 50, 1000, 30);
        Assert.Equal(80, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void AdjacentPositiveMarginsCollapseToTheLargest()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var second = TestSupport.Node("div", "second");
        root.Children = [first, second];

        var result = Lay(
            "#first { height: 50px; margin-bottom: 20px; }"
            + " #second { height: 10px; margin-top: 30px; }",
            root);

        Assert.Equal(80, TestSupport.Box(result, second).BorderBox.Y, 3);
    }

    [Fact]
    public void OppositeSignMarginsAreSummed()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var second = TestSupport.Node("div", "second");
        root.Children = [first, second];

        var result = Lay(
            "#first { height: 50px; margin-bottom: -10px; }"
            + " #second { height: 10px; margin-top: 5px; }",
            root);

        Assert.Equal(45, TestSupport.Box(result, second).BorderBox.Y, 3);
    }

    [Fact]
    public void ContentsDisplayFlattensTheBoxWithoutChangingFlow()
    {
        var root = TestSupport.Node("div", "root");
        var contents = TestSupport.Node("div", "contents");
        var child = TestSupport.Node("div", "child");
        root.Children = [contents];
        contents.Children = [child];

        var result = Lay(
            "#contents { display: contents; padding: 40px; margin: 40px; }"
            + " #child { height: 50px; }",
            root);

        var contentsBox = TestSupport.Box(result, contents);
        Assert.True(contentsBox.IsContents);
        Assert.Equal(50, contentsBox.BorderBox.Height, 3);
        Assert.Equal(0, contentsBox.Margin.Left, 3);
        Assert.Equal(50, TestSupport.Box(result, child).BorderBox.Height, 3);
        Assert.Equal(50, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    // ---------------------------------------------------------------- floats

    [Fact]
    public void LeftFloatIsPlacedAtTheContentEdge()
    {
        var root = TestSupport.Node("div", "root");
        var floated = TestSupport.Node("div", "float");
        root.Children = [floated];

        var result = Lay("#float { float: left; width: 100px; height: 50px; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, floated).BorderBox, 0, 0, 100, 50);
        Assert.Equal(50, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void RightFloatIsPlacedAtTheRightEdge()
    {
        var root = TestSupport.Node("div", "root");
        var floated = TestSupport.Node("div", "float");
        root.Children = [floated];

        var result = Lay("#float { float: right; width: 100px; height: 50px; }", root);

        TestSupport.AssertRect(TestSupport.Box(result, floated).BorderBox, 900, 0, 100, 50);
    }

    [Fact]
    public void SecondFloatSitsBesideTheFirstWhenItFits()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "a");
        var second = TestSupport.Node("div", "b");
        root.Children = [first, second];

        var result = Lay(
            "#a { float: left; width: 300px; height: 50px; }"
            + " #b { float: left; width: 300px; height: 50px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, first).BorderBox.X, 3);
        Assert.Equal(300, TestSupport.Box(result, second).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, second).BorderBox.Y, 3);
    }

    [Fact]
    public void FloatMovesBelowWhenItDoesNotFit()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "a");
        var second = TestSupport.Node("div", "b");
        root.Children = [first, second];

        var result = Lay(
            "#a { float: left; width: 600px; height: 50px; }"
            + " #b { float: left; width: 600px; height: 40px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, second).BorderBox.X, 3);
        Assert.Equal(50, TestSupport.Box(result, second).BorderBox.Y, 3);
    }

    [Fact]
    public void InlineTextAvoidsAnActiveFloat()
    {
        var root = TestSupport.Node("div", "root");
        var floated = TestSupport.Node("div", "float");
        var text = TestSupport.Node("p", "text", "abcd");
        root.Children = [floated, text];

        var result = Lay(
            "#float { float: left; width: 100px; height: 50px; }"
            + " #text { font-size: 10px; line-height: 20px; }",
            root);

        var box = TestSupport.Box(result, text);
        Assert.NotNull(box.Text);
        Assert.Single(box.Text!.Lines);
        Assert.Equal(100, box.Text.Lines[0].X, 3);
        Assert.Equal(0, box.Text.Lines[0].Y, 3);
    }

    [Fact]
    public void ClearMovesTheBoxBelowFloats()
    {
        var root = TestSupport.Node("div", "root");
        var floated = TestSupport.Node("div", "float");
        var cleared = TestSupport.Node("p", "text", "abcd");
        root.Children = [floated, cleared];

        var result = Lay(
            "#float { float: left; width: 100px; height: 50px; }"
            + " #text { clear: both; font-size: 10px; line-height: 20px; }",
            root);

        var box = TestSupport.Box(result, cleared);
        Assert.Equal(50, box.BorderBox.Y, 3);
        Assert.Equal(0, box.TextOrigin.X, 3);
    }

    [Fact]
    public void FlowRootAvoidsAFloatAndShrinksToTheFreeBand()
    {
        var root = TestSupport.Node("div", "root");
        var floated = TestSupport.Node("div", "float");
        var context = TestSupport.Node("div", "context");
        root.Children = [floated, context];

        var result = Lay(
            "#float { float: left; width: 100px; height: 50px; }"
            + " #context { display: flow-root; height: 20px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, context).BorderBox, 100, 0, 900, 20);
    }

    // ------------------------------------------------------------ positioning

    [Fact]
    public void RelativePositionOffsetsTheBoxWithoutMovingSiblings()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var second = TestSupport.Node("div", "second");
        root.Children = [first, second];

        var result = Lay(
            "#first { position: relative; left: 20px; top: 10px; height: 30px; }"
            + " #second { height: 10px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, first).BorderBox, 20, 10, 1000, 30);
        TestSupport.AssertRect(TestSupport.Box(result, second).BorderBox, 0, 30, 1000, 10);
    }

    [Fact]
    public void AbsoluteBoxUsesThePositionedAncestorPaddingBox()
    {
        var root = TestSupport.Node("div", "root");
        var parent = TestSupport.Node("div", "parent");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [parent];
        parent.Children = [absolute];

        var result = Lay(
            "#parent { position: relative; margin: 10px; padding: 20px;"
            + " width: 300px; height: 200px; }"
            + " #absolute { position: absolute; left: 0; top: 0; width: 10px; height: 10px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, absolute).BorderBox, 10, 10, 10, 10);
    }

    [Fact]
    public void AbsoluteBoxWithLeftAndRightStretches()
    {
        var root = TestSupport.Node("div", "root");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [absolute];

        var result = Lay(
            "#root { position: relative; height: 400px; }"
            + " #absolute { position: absolute; left: 10px; right: 20px;"
            + " top: 0; height: 30px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, absolute).BorderBox, 10, 0, 970, 30);
    }

    [Fact]
    public void AbsoluteBoxAnchorsToItsRightEdge()
    {
        var root = TestSupport.Node("div", "root");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [absolute];

        var result = Lay(
            "#root { position: relative; height: 400px; }"
            + " #absolute { position: absolute; right: 30px; width: 100px; height: 20px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, absolute).BorderBox, 870, 0, 100, 20);
    }

    [Fact]
    public void AbsoluteBoxAnchorsToItsBottomEdge()
    {
        var root = TestSupport.Node("div", "root");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [absolute];

        var result = Lay(
            "#root { position: relative; height: 400px; }"
            + " #absolute { position: absolute; bottom: 40px; width: 10px; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, absolute).BorderBox, 0, 310, 10, 50);
    }

    [Fact]
    public void AbsoluteBoxWithoutInsetsUsesItsStaticPosition()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [first, absolute];

        var result = Lay(
            "#root { position: relative; height: 400px; }"
            + " #first { height: 60px; }"
            + " #absolute { position: absolute; width: 10px; height: 10px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, absolute).BorderBox, 0, 60, 10, 10);
    }

    [Fact]
    public void FixedBoxUsesTheViewportEvenInsideAPositionedAncestor()
    {
        var root = TestSupport.Node("div", "root");
        var parent = TestSupport.Node("div", "parent");
        var fixedBox = TestSupport.Node("div", "fixed");
        root.Children = [parent];
        parent.Children = [fixedBox];

        var result = Lay(
            "#parent { position: relative; margin: 100px; padding: 50px; height: 300px; }"
            + " #fixed { position: fixed; left: 7px; top: 5px; width: 10px; height: 10px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, fixedBox).BorderBox, 7, 5, 10, 10);
    }

    [Fact]
    public void AbsoluteBoxDoesNotContributeToParentHeight()
    {
        var root = TestSupport.Node("div", "root");
        var absolute = TestSupport.Node("div", "absolute");
        root.Children = [absolute];

        var result = Lay(
            "#root { position: relative; }"
            + " #absolute { position: absolute; top: 10px; height: 300px; width: 10px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, root).BorderBox.Height, 3);
        Assert.Equal(300, TestSupport.Box(result, absolute).BorderBox.Height, 3);
    }

    // ---------------------------------------------------------------- inline

    [Fact]
    public void DirectTextIsLaidOutAsLines()
    {
        var root = TestSupport.Node("div", "root", "hello");
        var result = Lay("#root { font-size: 10px; line-height: 20px; }", root);
        var box = TestSupport.Box(result, root);

        Assert.NotNull(box.Text);
        Assert.Single(box.Text!.Lines);
        Assert.Equal(50, box.Text.Size.Width, 3);
        Assert.Equal(20, box.BorderBox.Height, 3);
        TestSupport.AssertRect(new Rect(
            box.TextOrigin.X, box.TextOrigin.Y, box.Text.Size.Width, box.Text.Size.Height), 0, 0, 50, 20);
    }

    [Fact]
    public void TextWrapsAtTheContentWidth()
    {
        var root = TestSupport.Node("div", "root", "abcdefghijkl");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px; overflow-wrap: anywhere; }",
            root);
        var box = TestSupport.Box(result, root);

        Assert.NotNull(box.Text);
        Assert.Equal(2, box.Text!.Lines.Length);
        Assert.Equal(40, box.BorderBox.Height, 3);
    }

    [Fact]
    public void InlineBlockShrinkWrapsToItsText()
    {
        var root = TestSupport.Node("div", "root");
        var inline = TestSupport.Node("span", "inline", "abc");
        root.Children = [inline];

        var result = Lay(
            "#inline { display: inline-block; font-size: 10px; line-height: 20px; }",
            root);

        var box = TestSupport.Box(result, inline);
        TestSupport.AssertRect(box.BorderBox, 0, 0, 30, 20);
        Assert.Equal(20, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void InlineChildrenWrapOntoNewLines()
    {
        var root = TestSupport.Node("div", "root");
        var a = TestSupport.Node("span", "a");
        var b = TestSupport.Node("span", "b");
        var c = TestSupport.Node("span", "c");
        root.Children = [a, b, c];

        var result = Lay(
            "#root { width: 100px; }"
            + " #a, #b, #c { display: inline-block; width: 40px; height: 10px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(40, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.X, 3);
        Assert.Equal(10, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(20, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void TextAndInlineChildShareTheFirstLine()
    {
        var root = TestSupport.Node("div", "root", "ab");
        var inline = TestSupport.Node("span", "inline");
        root.Children = [inline];

        var result = Lay(
            "#root { font-size: 10px; line-height: 20px; }"
            + " #inline { display: inline-block; width: 30px; height: 10px; }",
            root);

        Assert.Equal(20, TestSupport.Box(result, inline).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, inline).BorderBox.Y, 3);
    }

    // -------------------------------------------------------------- scrolling

    [Fact]
    public void ScrollMetricsReflectOverflowingChildren()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #child { height: 300px; }",
            root);
        var box = TestSupport.Box(result, root);

        Assert.True(box.IsScrollContainer);
        Assert.Equal(300, box.ScrollHeight, 3);
        Assert.Equal(1000, box.ScrollWidth, 3);
    }

    [Fact]
    public void ScrollOffsetIsClampedToTheOverflow()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        root.ScrollOffsetY = 50;

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #child { height: 300px; }",
            root);
        var box = TestSupport.Box(result, root);

        Assert.Equal(50, box.ScrollOffsetY, 3);
    }

    [Fact]
    public void ScrollOffsetCannotExceedTheScrollRange()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        root.ScrollOffsetY = 9999;

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #child { height: 300px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, root).ScrollOffsetY, 3);
    }

    [Fact]
    public void NonScrollContainersReportNoOffset()
    {
        var root = TestSupport.Node("div", "root");
        root.ScrollOffsetY = 40;

        var result = Lay("#root { height: 100px; }", root);

        Assert.False(TestSupport.Box(result, root).IsScrollContainer);
        Assert.Equal(0, TestSupport.Box(result, root).ScrollOffsetY, 3);
    }

    // ------------------------------------------------------------ box geometry

    [Fact]
    public void MarginBoxIncludesResolvedMargins()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { padding: 10px; }"
            + " #child { width: 100px; height: 50px; margin: 5px 15px; }",
            root);
        var box = TestSupport.Box(result, child);

        TestSupport.AssertRect(box.BorderBox, 25, 15, 100, 50);
        TestSupport.AssertRect(box.MarginBox, 10, 10, 130, 60);
    }

    [Fact]
    public void BorderRadiiResolveToPixels()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 100px; border-radius: 10px 20px 30px 40px; }",
            root);

        var radius = TestSupport.Box(result, root).Radius;
        Assert.Equal(10, radius.TopLeft, 3);
        Assert.Equal(20, radius.TopRight, 3);
        Assert.Equal(30, radius.BottomRight, 3);
        Assert.Equal(40, radius.BottomLeft, 3);
    }

    [Fact]
    public void PercentageBorderRadiusResolvesAgainstTheSmallerSide()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 200px; height: 100px; border-radius: 25%; }",
            root);

        Assert.Equal(25, TestSupport.Box(result, root).Radius.TopLeft, 3);
    }

    [Fact]
    public void BaselineOfABoxWithChildrenUsesTheFirstChildBottom()
    {
        var root = TestSupport.Node("div", "root");
        var first = TestSupport.Node("div", "first");
        var second = TestSupport.Node("div", "second");
        root.Children = [first, second];

        var result = Lay(
            "#first { height: 30px; } #second { height: 50px; }",
            root);

        Assert.Equal(30, TestSupport.Box(result, root).Baseline, 3);
    }

    [Fact]
    public void BaselineOfATextBoxUsesTheFirstLine()
    {
        var root = TestSupport.Node("div", "root", "hello");
        var result = Lay(
            "#root { font-size: 10px; line-height: 20px; }",
            root);
        var box = TestSupport.Box(result, root);

        Assert.Equal(box.Text!.Lines[0].Y + box.Text.Lines[0].Baseline, box.Baseline, 3);
    }
}
