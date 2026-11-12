using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for table layout: spanning cells, row heights, border
/// spacing and the collapsed-border approximation.
/// </summary>
public class TableLayoutTests
{
    private static LayoutResult Lay(string css, IgniteSupportedDomNode root,
        float width = 1000, float height = 800)
    {
        var options = TestSupport.Options(width, height);
        options.TextMeasurer = TestSupport.FixedMeasurer.Instance;
        return TestSupport.Layout(TestSupport.Build(css), root, options);
    }

    private static IgniteSupportedDomNode Row(params IgniteSupportedDomNode[] cells)
    {
        var row = TestSupport.Node("tr");
        row.Children = cells;
        return row;
    }

    private static IgniteSupportedDomNode Cell(string id, string? text = null)
    {
        return TestSupport.Node("td", id, text);
    }

    private const string TableCss =
        "#root { display: table; width: 300px; border-spacing: 0; }"
        + " tr { display: table-row; }"
        + " td { display: table-cell; }";

    [Fact]
    public void ColspanSharesTheSpannedColumns()
    {
        var root = TestSupport.Node("div", "root");
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        var d = Cell("d");
        var e = Cell("e");
        a.SetAttribute("colspan", "2");
        root.Children = [Row(a, b), Row(c, d, e)];

        var result = Lay(
            TableCss
            + " #a { width: 200px; height: 20px; }"
            + " #b, #c, #d, #e { width: 100px; height: 20px; }",
            root);

        TestSupport.AssertRect(TestSupport.Box(result, a).BorderBox, 0, 0, 200, 20);
        TestSupport.AssertRect(TestSupport.Box(result, b).BorderBox, 200, 0, 100, 20);
        TestSupport.AssertRect(TestSupport.Box(result, c).BorderBox, 0, 20, 100, 20);
        TestSupport.AssertRect(TestSupport.Box(result, d).BorderBox, 100, 20, 100, 20);
        TestSupport.AssertRect(TestSupport.Box(result, e).BorderBox, 200, 20, 100, 20);
    }

    [Fact]
    public void RowspanSkipsOccupiedSlotsAndStretchesAcrossRows()
    {
        var root = TestSupport.Node("div", "root");
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        a.SetAttribute("rowspan", "2");
        root.Children = [Row(a, b), Row(c)];

        var result = Lay(
            TableCss
            + " #root { width: 200px; }"
            + " #a { width: 100px; height: 10px; }"
            + " #b { width: 100px; height: 20px; }"
            + " #c { width: 100px; height: 30px; }",
            root);

        var aBox = TestSupport.Box(result, a);
        Assert.Equal(100, aBox.BorderBox.Width, 3);
        Assert.Equal(50, aBox.BorderBox.Height, 3);

        // The second row's only cell skips the slot held by A.
        Assert.Equal(100, TestSupport.Box(result, c).BorderBox.X, 3);
        Assert.Equal(20, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void BorderSpacingSeparatesRowsAndColumns()
    {
        var root = TestSupport.Node("div", "root");
        var a = Cell("a");
        var b = Cell("b");
        var c = Cell("c");
        var d = Cell("d");
        root.Children = [Row(a, b), Row(c, d)];

        var result = Lay(
            TableCss
            + " #root { width: 210px; border-spacing: 10px; }"
            + " #a, #b, #c, #d { width: 100px; height: 20px; }",
            root);

        Assert.Equal(110, TestSupport.Box(result, b).BorderBox.X, 3);
        Assert.Equal(30, TestSupport.Box(result, c).BorderBox.Y, 3);
    }

    [Fact]
    public void CollapsedBordersIgnoreSpacing()
    {
        var root = TestSupport.Node("div", "root");
        var a = Cell("a");
        var b = Cell("b");
        root.Children = [Row(a, b)];

        var result = Lay(
            TableCss
            + " #root { width: 208px; border-collapse: collapse; border-spacing: 10px; }"
            + " #a, #b { width: 100px; height: 20px; border: 2px solid #000; }",
            root);

        // Shared internal edges are painted once by the following cell, so the
        // full 2px border remains and the next cell starts immediately after it.
        Assert.Equal(104, TestSupport.Box(result, a).BorderBox.Width, 3);
        Assert.Equal(104, TestSupport.Box(result, b).BorderBox.X, 3);
    }

    [Fact]
    public void ZeroWidthTableScalesDownToMinContentInsteadOfMaxContent()
    {
        var root = TestSupport.Node("div", "root");
        var cell = Cell("cell", "aa bb");
        root.Children = [Row(cell)];

        var result = Lay(
            "#root { display: table; width: 0; font-size: 10px; line-height: 20px; }"
            + " tr { display: table-row; } td { display: table-cell; }",
            root);

        // "aa bb" is 50px max-content but 20px min-content; a zero-width table
        // must not keep the max-content width.
        Assert.Equal(20, TestSupport.Box(result, cell).BorderBox.Width, 3);
    }
}
