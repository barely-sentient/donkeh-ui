using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for grid layout: explicit/implicit tracks, fr and auto
/// sizing, gaps, placement, spans, named lines and areas, dense flow and item
/// alignment on both axes.
/// </summary>
public class GridLayoutTests
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

    // --------------------------------------------------------- track sizing

    [Fact]
    public void FixedColumnTracksUseTheirLengths()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 200px 300px;"
            + " grid-template-rows: 50px; }"
            + " #a, #b, #c { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 0, 200, 50);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 300, 0, 300, 50);
        Assert.Equal(50, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void FractionTracksShareTheFreeSpace()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 1fr 2fr;"
            + " grid-template-rows: 30px; }"
            + " #a, #b { height: 30px; }",
            root);

        Assert.Equal(1000f / 3f, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(2000f / 3f, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(1000f / 3f, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void PercentTracksUseTheAvailableWidth()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 25% 75%;"
            + " grid-template-rows: 30px; }"
            + " #a, #b { height: 30px; }",
            root);

        Assert.Equal(250, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(750, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(250, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void AutoColumnsGrowToShareFreeSpace()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: auto auto;"
            + " grid-template-rows: 30px; }"
            + " #a { width: 100px; height: 30px; }"
            + " #b { width: 200px; height: 30px; }",
            root);

        // Contributions are 100 and 200, leaving 700px shared equally.
        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(450, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.Width, 3);
    }

    [Fact]
    public void MinContentAndMaxContentDoNotShareFreeSpace()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: min-content max-content;"
            + " grid-template-rows: 30px; }"
            + " #a { width: 100px; height: 30px; }"
            + " #b { width: 200px; height: 30px; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.Width, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void MinContentAndMaxContentUseDifferentContributions()
    {
        var (root, a, b, _) = Tree(2);
        a.TextContent = "aa bb";
        b.TextContent = "cc dd ee";

        var result = Lay(
            "#root { display: grid; grid-template-columns: min-content max-content;"
            + " grid-template-rows: 30px; font-size: 10px; line-height: 20px; }",
            root);

        // "aa bb" is 50px max-content but 20px min-content; "cc dd ee" is 80px
        // max-content. The min-content track takes 20px, so the max-content
        // track starts there.
        Assert.Equal(20, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(80, TestSupport.Box(result, b).BorderBox.Width, 3);
    }

    [Fact]
    public void AutoFitCollapsesEmptyCopiesAndTheirGutters()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; column-gap: 10px;"
            + " grid-template-columns: repeat(auto-fit, 100px);"
            + " grid-template-rows: 20px; }"
            + " #a { height: 20px; }"
            + " #b { grid-column: 9; height: 20px; }",
            root);

        // Nine copies fit. Only the first and last receive items; the seven empty
        // copies collapse, so B moves next to A with a single 10px gap.
        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(110, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void FitContentClampsToItsLimit()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: fit-content(150px) 1fr;"
            + " grid-template-rows: 30px; }"
            + " #a { width: 300px; height: 30px; }"
            + " #b { height: 30px; }",
            root);

        Assert.Equal(150, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(150, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(850, TestSupport.Box(result, b).BorderBox.Width, 3);
    }

    [Fact]
    public void RepeatExpandsToTheRequestedCount()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: repeat(3, 100px);"
            + " grid-template-rows: 20px; }"
            + " #a, #b, #c { height: 20px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(200, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    [Fact]
    public void AutoFillRepeatsAsManyTracksAsFit()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: repeat(auto-fill, 100px);"
            + " grid-template-rows: 20px; }"
            + " #a, #b, #c { height: 20px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(200, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    [Fact]
    public void AutoFillAccountsForColumnGap()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: repeat(auto-fill, 100px);"
            + " column-gap: 10px; grid-template-rows: 20px; }"
            + " #a, #b, #c { height: 20px; }",
            root);

        // 9 tracks fit in 1000px with 10px gaps; positions step by 110px.
        Assert.Equal(110, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(220, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    [Fact]
    public void GridTemplateShorthandSetsRowsAndColumns()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template: 100px 200px / 50px 60px; }"
            + " #a, #b { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 0, 200, 50);
        Assert.Equal(110, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    // ---------------------------------------------------------------- gaps

    [Fact]
    public void RowAndColumnGapsSeparateTracks()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 50px 50px; column-gap: 20px; row-gap: 10px; }"
            + " #a, #b, #c { height: 50px; }"
            + " #c { grid-column: 1; grid-row: 2; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(120, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(60, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(110, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void EmptyGridStillSizesItsExplicitRows()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { display: grid; grid-template-rows: 40px 20px; row-gap: 10px; }",
            root);

        Assert.Equal(70, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void StretchedItemMeasuresAtCellWidthBeforeRowSizing()
    {
        var root = TestSupport.Node("div", "root");
        var a = TestSupport.Node("div", "a");
        var b = TestSupport.Node("div", "b");
        root.Children = [a, b];

        var result = Lay(
            "#root { display: grid; grid-template-columns: 300px; gap: 10px; }"
            + " #a { aspect-ratio: 2; }"
            + " #b { height: 10px; }",
            root);

        // The empty item has zero max-content width but stretches to 300px, so
        // its aspect-ratio height is 150px and the next row starts after that.
        Assert.Equal(150, TestSupport.Box(result, a).BorderBox.Height, 3);
        Assert.Equal(160, TestSupport.Box(result, b).BorderBox.Y, 3);
    }

    [Fact]
    public void EmptyGridCollapsesAutoFitRows()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { display: grid; grid-template-rows: repeat(auto-fit, 50px); }",
            root);

        Assert.Equal(0, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void FrTracksKeepTheirMinimumContribution()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 1fr 1fr;"
            + " grid-template-rows: 30px; }"
            + " #a { width: 300px; height: 30px; }"
            + " #b { height: 30px; }",
            root);

        // A's content floor is 300px; the remaining 700px is split equally.
        Assert.Equal(650, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    // ------------------------------------------------------------ placement

    [Fact]
    public void AutoPlacementIsRowMajor()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 50px 50px; }"
            + " #a, #b, #c { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 0, 50, 100, 50);
    }

    [Fact]
    public void ExplicitRowAndColumnPlaceTheItem()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 50px 50px; }"
            + " #a { height: 50px; }"
            + " #b { grid-column: 2; grid-row: 2; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 50, 100, 50);
    }

    [Fact]
    public void SpanStretchesAcrossTracksAndGaps()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 50px; column-gap: 10px; }"
            + " #a { grid-column: 1 / span 2; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 210, 50);
    }

    [Fact]
    public void ExplicitEndLineFixesTheStartLine()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 50px; }"
            + " #a { grid-column: span 1 / 3; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 0, 100, 50);
    }

    [Fact]
    public void NegativeLineMinusOneEndsAtTheGridEnd()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px 100px;"
            + " grid-template-rows: 50px; }"
            + " #a { grid-column: 1 / -1; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 300, 50);
    }

    [Fact]
    public void NamedAreaStartKeepsAnExplicitEndLine()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-areas: 'a b c';"
            + " grid-template-columns: 100px 100px 100px; grid-template-rows: 50px; }"
            + " #a { grid-column: a / 3; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 200, 50);
    }

    [Fact]
    public void NamedLinesPlaceItemsByLineName()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns:"
            + " [start] 100px [mid] 200px [end]; grid-template-rows: 50px; }"
            + " #a { grid-column: mid / end; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 0, 200, 50);
    }

    [Fact]
    public void NamedAreasPlaceItemsInTheirRectangle()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-areas: 'a b' 'c d';"
            + " grid-template-columns: 100px 100px; grid-template-rows: 50px 50px; }"
            + " #a { grid-area: b; }"
            + " #b { grid-area: d; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 50, 100, 50);
    }

    [Fact]
    public void MultiCellNamedAreaSpansItsTracks()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-areas: 'wide wide' 'x y';"
            + " grid-template-columns: 100px 100px; grid-template-rows: 40px 40px; }"
            + " #a { grid-area: wide; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 200, 40);
    }

    [Fact]
    public void GridAreaShorthandPlacesAcrossRowsAndColumns()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px 100px 100px;"
            + " grid-template-rows: 50px 50px; }"
            + " #a { grid-area: 2 / 2 / 3 / 4; height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 50, 200, 50);
    }

    [Fact]
    public void DenseFlowBackfillsHoles()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-auto-flow: row dense;"
            + " grid-template-columns: 100px 100px; grid-template-rows: 50px 50px; }"
            + " #a { grid-column: 2; height: 50px; }"
            + " #b { height: 50px; } #c { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 0, 50, 100, 50);
    }

    [Fact]
    public void SparseFlowLeavesHoles()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-auto-flow: row;"
            + " grid-template-columns: 100px 100px; grid-template-rows: 50px 50px; }"
            + " #a { grid-column: 2; height: 50px; }"
            + " #b { height: 50px; } #c { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 100, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 0, 50, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 100, 50, 100, 50);
    }

    [Fact]
    public void ColumnFlowFillsColumnsFirst()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-auto-flow: column;"
            + " grid-template-rows: 50px 50px; grid-auto-columns: 100px; }"
            + " #a, #b, #c { height: 50px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 0, 50, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 100, 0, 100, 50);
    }

    [Fact]
    public void OrderPropertyReordersAutoPlacedItems()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: repeat(3, 100px);"
            + " grid-template-rows: 20px; }"
            + " #a { order: 2; height: 20px; }"
            + " #b { order: 1; height: 20px; }"
            + " #c { order: 0; height: 20px; }",
            root);

        Assert.Equal(200, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(0, TestSupport.Box(result, c).BorderBox.X, 3);
    }

    // -------------------------------------------------------------- rows

    [Fact]
    public void AutoRowsGrowToTheirItemHeights()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px; }"
            + " #a { height: 30px; } #b { height: 50px; }",
            root);

        Assert.Equal(0, TestSupport.Box(result, a).BorderBox.Y, 3);
        Assert.Equal(30, TestSupport.Box(result, b).BorderBox.Y, 3);
        Assert.Equal(80, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void FractionRowsShareTheDefiniteHeight()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px;"
            + " grid-template-rows: 1fr 2fr; height: 300px; }"
            + " #a, #b { height: auto; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.Height, 3);
        Assert.Equal(200, TestSupport.Box(result, b).BorderBox.Height, 3);
        Assert.Equal(100, TestSupport.Box(result, b).BorderBox.Y, 3);
    }

    [Fact]
    public void ImplicitRowsUseGridAutoRows()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px;"
            + " grid-auto-rows: 40px; }",
            root);

        Assert.Equal(40, TestSupport.Box(result, a).BorderBox.Height, 3);
        Assert.Equal(40, TestSupport.Box(result, b).BorderBox.Y, 3);
        Assert.Equal(80, TestSupport.Box(result, c).BorderBox.Y, 3);
        Assert.Equal(120, TestSupport.Box(result, root).BorderBox.Height, 3);
    }

    [Fact]
    public void DefiniteRowPlacementCreatesImplicitRows()
    {
        var (root, a, b, _) = Tree(2);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px;"
            + " grid-auto-rows: 40px; }"
            + " #a { height: 40px; }"
            + " #b { grid-row: 2; height: 40px; }",
            root);

        Assert.Equal(40, TestSupport.Box(result, b).BorderBox.Y, 3);
    }

    [Fact]
    public void ImplicitColumnsUseGridAutoColumns()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 100px;"
            + " grid-auto-columns: 50px; grid-template-rows: 30px; }"
            + " #a { grid-column: 3; height: 30px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 150, 0, 50, 30);
    }

    // ------------------------------------------------------------ alignment

    [Fact]
    public void JustifyItemsCenterCentersTheItemHorizontally()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 200px;"
            + " grid-template-rows: 100px; justify-items: center; align-items: center; }"
            + " #a { width: 100px; height: 20px; }",
            root);

        Assert.Equal(50, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(40, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void JustifyItemsEndPushesTheItemToTheRight()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 200px;"
            + " grid-template-rows: 100px; justify-items: end; align-items: end; }"
            + " #a { width: 100px; height: 20px; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(80, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void StretchIsTheDefaultForGridItems()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 200px;"
            + " grid-template-rows: 100px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 200, 100);
    }

    [Fact]
    public void JustifySelfAndAlignSelfOverrideTheContainer()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 200px;"
            + " grid-template-rows: 100px; justify-items: start; align-items: start; }"
            + " #a { width: 100px; height: 20px; justify-self: end; align-self: end; }",
            root);

        Assert.Equal(100, TestSupport.Box(result, a).BorderBox.X, 3);
        Assert.Equal(80, TestSupport.Box(result, a).BorderBox.Y, 3);
    }

    [Fact]
    public void ItemMarginsAreInsetFromTheCell()
    {
        var (root, a, _, _) = Tree(1);
        var result = Lay(
            "#root { display: grid; grid-template-columns: 200px;"
            + " grid-template-rows: 100px; }"
            + " #a { margin: 10px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 10, 10, 180, 80);
    }

    [Fact]
    public void OutOfFlowItemsAreNotGridItems()
    {
        var (root, a, b, c) = Tree();
        var result = Lay(
            "#root { display: grid; position: relative;"
            + " grid-template-columns: 100px 100px; grid-template-rows: 50px; }"
            + " #a { height: 50px; }"
            + " #b { position: absolute; left: 5px; top: 5px; width: 10px; height: 10px; }"
            + " #c { height: 50px; }",
            root);

        // B does not occupy a cell, so C lands in cell two.
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 100, 0, 100, 50);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 5, 5, 10, 10);
    }

    [Fact]
    public void InlineGridShrinkWrapsItsTracks()
    {
        var root = TestSupport.Node("div", "root");
        var inline = TestSupport.Node("span", "inline");
        var a = TestSupport.Node("div", "a");
        var b = TestSupport.Node("div", "b");
        inline.Children = [a, b];
        root.Children = [inline];

        var result = Lay(
            "#inline { display: inline-grid; grid-template-columns: 100px 100px;"
            + " grid-template-rows: 20px; }"
            + " #a, #b { width: 100px; height: 20px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, inline).BorderBox, 0, 0, 200, 20);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 100, 0, 100, 20);
    }
}
