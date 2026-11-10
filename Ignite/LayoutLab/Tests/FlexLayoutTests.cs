using Ignite.Data.Dom;

namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Specification tests for flexible box layout: flex bases, grow/shrink,
/// wrapping, gaps, ordering, main/cross-axis alignment and direction reversal.
/// </summary>
public class FlexLayoutTests
{
    private static LayoutResult Lay(string css, IgniteSupportedDomNode root,
        float width = 1000, float height = 800)
    {
        var options = TestSupport.Options(width, height);
        options.TextMeasurer = TestSupport.FixedMeasurer.Instance;
        return TestSupport.Layout(TestSupport.Build(css), root, options);
    }

    private static (IgniteSupportedDomNode Root, IgniteSupportedDomNode A,
        IgniteSupportedDomNode B, IgniteSupportedDomNode C) Tree(int count = 3)
    {
        var root = TestSupport.Node("div", "root");
        var a = TestSupport.Node("div", "a");
        var b = TestSupport.Node("div", "b");
        var c = TestSupport.Node("div", "c");
        root.Children = count switch
        {
            1 => [a],
            2 => [a, b],
            _ => [a, b, c]
        };
        return (root, a, b, c);
    }

    // ------------------------------------------------------------- main axis

    [Fact]
    public void RowItemsArePlacedLeftToRight()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; }"
            + " #a { width: 100px; height: 50px; }"
            + " #b { width: 200px; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 0, 200, 50);
        Assert.Equal(50, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void FlexGrowSharesTheFreeSpace()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a { flex: 1; } #b { flex: 1; }",
            root);

        Assert.Equal(500, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(500, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(500, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void FlexGrowIsProportional()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a { width: 100px; flex-grow: 1; }"
            + " #b { width: 100px; flex-grow: 3; }",
            root);

        // Free space is 800px: 200px to A, 600px to B.
        Assert.Equal(300, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(700, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(300, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void FlexBasisOverridesWidth()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a { width: 100px; flex-basis: 300px; }",
            root);

        Assert.Equal(300, TestSupport.Box(result, a).BorderBox.Width, 3);
    }

    [Fact]
    public void PercentageFlexBasisUsesTheContainerWidth()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a { flex-basis: 50%; }",
            root);

        Assert.Equal(500, TestSupport.Box(result, a).BorderBox.Width, 3);
    }

    [Fact]
    public void ZeroFlexBasisWithGrowSplitsTheContainerEvenly()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a, #b { flex: 1 1 0; }",
            root);

        Assert.Equal(500, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(500, TestSupport.Box(result, b).BorderBox.Width, 3);
    }

    [Fact]
    public void ShrinkingIsWeightedByBasis()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; width: 200px; height: 50px; }"
            + " #a { width: 300px; } #b { width: 300px; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void FlexShrinkZeroKeepsTheItemSize()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; width: 200px; height: 50px; }"
            + " #a { width: 300px; flex-shrink: 0; }"
            + " #b { width: 300px; flex-shrink: 1; }",
            root);

        Assert.Equal(300, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(0, TestSupport.Box(result, b).BorderBox.Width, 3);
    }

    [Fact]
    public void MinWidthClampsAFlexItem()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; width: 300px; height: 50px; }"
            + " #a { flex-basis: 50px; min-width: 200px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.Width, 3);
    }

    [Fact]
    public void ColumnGapSeparatesRowItems()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; column-gap: 20px; height: 50px; }"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(120, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void PercentageColumnGapUsesTheContainerWidth()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; column-gap: 10%; height: 50px; }"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void RowReversePlacesItemsFromTheRightEdge()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; flex-direction: row-reverse; width: 300px; height: 50px; }"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void OrderPropertyReordersFlexItems()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; height: 50px; }"
            + " #a { width: 100px; order: 2; }"
            + " #b { width: 100px; order: 1; }"
            + " #c { width: 100px; order: 0; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    [Fact]
    public void ItemMarginsParticipateInMainSizingAndPlacement()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; width: 200px; height: 50px; }"
            + " #a { width: 100px; margin-left: 10px; }",
            root);

        Assert.Equal(10, TestSupport.Box(result, a).BorderBox.X, 3);
    }

    // --------------------------------------------------------- justify

    [Theory]
    [InlineData("center", 400, 500)]
    [InlineData("flex-end", 800, 900)]
    [InlineData("end", 800, 900)]
    public void TwoItemJustifyContentOffsetsBothItems(string value, float ax, float bx)
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            $"#root {{ display: flex; justify-content: {value}; height: 50px; }}"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(ax, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(bx, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void SpaceBetweenDistributesTheGap()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; justify-content: space-between; height: 50px; }"
            + " #a, #b, #c { width: 100px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(450, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(900, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    [Fact]
    public void SpaceAroundAddsHalfGapsAtTheEdges()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; justify-content: space-around; height: 50px; }"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(700, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void SpaceEvenlyUsesEqualGaps()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; justify-content: space-evenly; height: 50px; }"
            + " #a, #b { width: 100px; }",
            root);

        Assert.Equal(800f / 3f, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(800f / 3f + 100 + 800f / 3f,
            TestSupport.Box(result, b).BorderBox.X, 3);
    }

    // ------------------------------------------------------------ cross axis

    [Fact]
    public void AlignItemsCenterCentersTheItemVertically()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; align-items: center; height: 200px; }"
            + " #a { width: 100px; height: 50px; }",
            root);

        Assert.Equal(75, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void AlignItemsFlexEndPushesTheItemDown()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; align-items: flex-end; height: 200px; }"
            + " #a { width: 100px; height: 50px; }",
            root);

        Assert.Equal(150, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void AlignItemsStretchFillsTheCrossAxis()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; align-items: stretch; height: 200px; }"
            + " #a { width: 100px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 200);
    }

    [Fact]
    public void AlignSelfOverridesTheContainerAlignment()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; align-items: flex-end; height: 200px; }"
            + " #a { width: 100px; height: 50px; align-self: center; }",
            root);

        Assert.Equal(75, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void ExplicitHeightPreventsStretching()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; align-items: stretch; height: 200px; }"
            + " #a { width: 100px; height: 40px; }",
            root);

        Assert.Equal(40, TestSupport.Box(result, a).BorderBox.Height, 3);
        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    // ------------------------------------------------------------- column

    [Fact]
    public void ColumnItemsStackAndStretchAcross()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; flex-direction: column; }"
            + " #a { height: 50px; } #b { height: 60px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 1000, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 0, 50, 1000, 60);
        Assert.Equal(110, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void ColumnItemWithExplicitWidthDoesNotStretch()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: flex; flex-direction: column; }"
            + " #a { width: 100px; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
    }

    [Fact]
    public void ColumnJustifyContentCentersOnTheMainAxis()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; flex-direction: column; justify-content: center;"
            + " height: 400px; }"
            + " #a, #b { height: 50px; }",
            root);

        Assert.Equal(150, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.Y, 3);
    }

    [Fact]
    public void RowGapSeparatesColumnItems()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; flex-direction: column; row-gap: 20px; }"
            + " #a, #b { height: 50px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(70, TestSupport.Box(result, b).BorderBox.Y, 3);
        Assert.Equal(120, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void ColumnReversePlacesItemsFromTheBottom()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: flex; flex-direction: column-reverse; height: 300px; }"
            + " #a, #b { height: 50px; }",
            root);

        Assert.Equal(250, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.Y, 3);
    }

    // ------------------------------------------------------------- wrapping

    [Fact]
    public void WrappingMovesItemsToTheNextLine()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; width: 250px; }"
            + " #a, #b, #c { width: 100px; height: 30px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.X, 3);
        Assert.Equal(30, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(60, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void WrappingAccountsForColumnGap()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; column-gap: 20px; width: 250px; }"
            + " #a, #b, #c { width: 100px; height: 30px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(120, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.X, 3);
        Assert.Equal(30, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void RowGapSeparatesWrappedLines()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; row-gap: 10px; width: 250px; }"
            + " #a, #b, #c { width: 100px; height: 30px; }",
            root);

        Assert.Equal(40, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(70, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void AlignContentStretchGrowsTheLines()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; align-content: stretch;"
            + " width: 250px; height: 300px; }"
            + " #a, #b, #c { width: 100px; height: 50px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(150, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void AlignContentCenterCentersTheLineBox()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; align-content: center;"
            + " width: 250px; height: 300px; }"
            + " #a, #b, #c { width: 100px; height: 50px; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(150, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void AlignContentSpaceBetweenPushesLinesApart()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; align-content: space-between;"
            + " width: 250px; height: 300px; }"
            + " #a, #b, #c { width: 100px; height: 50px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(250, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void AlignContentFlexEndPutsLinesAtTheBottom()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: wrap; align-content: flex-end;"
            + " width: 250px; height: 300px; }"
            + " #a, #b, #c { width: 100px; height: 50px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(250, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void NoWrapKeepsItemsOnOneLineAndShrinksThem()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; flex-wrap: nowrap; width: 150px; height: 30px; }"
            + " #a, #b, #c { width: 100px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(0, TestSupport.Box(result, b).BorderBox.Y, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(50, TestSupport.Box(result, a).BorderBox.Width, 3);
    }

    [Fact]
    public void OutOfFlowChildrenAreNotFlexItems()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: flex; position: relative; height: 50px; }"
            + " #a { width: 100px; }"
            + " #b { position: absolute; left: 0; top: 0; width: 100px; height: 20px; }"
            + " #c { width: 100px; }",
            root);

        // Only A and C consume main-axis space, so C starts after A.
        Assert.Equal(100, TestSupport.Box(result, c).BorderBox.X, 3);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 0, 0, 100, 20);
    }

    [Fact]
    public void InlineFlexShrinkWrapsToItsContent()
    {
        var root = TestSupport.Node("div", "root");
        var inline = TestSupport.Node("span", "inline");
        var a = TestSupport.Node("div", "a");
        var b = TestSupport.Node("div", "b");
        inline.Children = [a, b];
        root.Children = [inline];

        var result = Lay(
            "#inline { display: inline-flex; }"
            + " #a, #b { width: 100px; height: 20px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, inline).BorderBox, 0, 0, 200, 20);
    }
}
