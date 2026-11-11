namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Tests for the derived state exposed by <see cref="LayoutBox"/>: ancestry,
/// stacking-context rules, flow participation and the client/scroll metrics the
/// host uses to drive scrolling.
/// </summary>
public sealed class LayoutBoxTests
{
    private static LayoutBox Lay(string declarations)
    {
        var root = TestSupport.Node("div", "root");
        var result = TestSupport.Layout(
            "#root { " + declarations + " }", root);
        return TestSupport.Box(result, root);
    }

    [Fact]
    public void DepthAndParentFollowTheLayoutTree()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        var grandchild = TestSupport.Node("div", "grandchild");
        root.Children = [child];
        child.Children = [grandchild];

        var result = TestSupport.Layout(
            "#root { width: 100px; }"
            + " #child { width: 50px; height: 10px; }"
            + " #grandchild { width: 10px; height: 10px; }",
            root);

        var rootBox = TestSupport.Box(result, root);
        var childBox = TestSupport.Box(result, child);
        var grandchildBox = TestSupport.Box(result, grandchild);

        Assert.Equal(0, rootBox.Depth);
        Assert.Equal(1, childBox.Depth);
        Assert.Equal(2, grandchildBox.Depth);
        Assert.Same(rootBox, childBox.Parent);
        Assert.Same(childBox, grandchildBox.Parent);
        Assert.Same(rootBox, result.Root);
    }

    [Fact]
    public void ContentsBoxIsNotPainted()
    {
        var box = Lay("display: contents; width: 10px; height: 10px;");

        Assert.True(box.IsContents);
        Assert.False(box.IsPainted);
    }

    [Fact]
    public void AnOrdinaryBoxIsPainted()
    {
        var box = Lay("width: 10px; height: 10px;");

        Assert.False(box.IsContents);
        Assert.True(box.IsPainted);
    }

    [Fact]
    public void AbsoluteAndFixedBoxesAreOutOfFlow()
    {
        Assert.True(Lay("position: absolute; width: 10px; height: 10px;").IsOutOfFlow);
        Assert.True(Lay("position: fixed; width: 10px; height: 10px;").IsOutOfFlow);
        Assert.False(Lay("position: relative; width: 10px; height: 10px;").IsOutOfFlow);
        Assert.False(Lay("width: 10px; height: 10px;").IsOutOfFlow);
    }

    [Fact]
    public void OnlyFixedBoxesReportIsFixed()
    {
        Assert.True(Lay("position: fixed; width: 10px; height: 10px;").IsFixed);
        Assert.False(Lay("position: absolute; width: 10px; height: 10px;").IsFixed);
    }

    [Fact]
    public void AbsoluteBoxesResolveTheirContainingBlock()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = TestSupport.Layout(
            "#root { width: 100px; height: 100px; position: relative; }"
            + " #child { position: absolute; width: 10px; height: 10px; }",
            root);

        Assert.Same(TestSupport.Box(result, root), TestSupport.Box(result, child).ContainingBlock);
    }

    [Fact]
    public void AutoZIndexHasNoStackingContext()
    {
        var box = Lay("width: 10px; height: 10px;");

        Assert.False(box.HasZIndex);
        Assert.Equal(0, box.EffectiveZIndex);
        Assert.False(box.CreatesStackingContext);
    }

    [Fact]
    public void ANumericZIndexCreatesAStackingContext()
    {
        var box = Lay("z-index: -3; width: 10px; height: 10px;");

        Assert.True(box.HasZIndex);
        Assert.Equal(-3, box.EffectiveZIndex);
        Assert.True(box.CreatesStackingContext);
    }

    [Theory]
    [InlineData("opacity: 0.5;")]
    [InlineData("transform: translateX(1px);")]
    [InlineData("position: fixed;")]
    [InlineData("filter: blur(1px);")]
    [InlineData("backdrop-filter: blur(1px);")]
    [InlineData("clip-path: circle(50%);")]
    [InlineData("mask: url(m.png);")]
    [InlineData("mix-blend-mode: multiply;")]
    [InlineData("isolation: isolate;")]
    public void StyleFeaturesCreateStackingContexts(string declaration)
    {
        var box = Lay(declaration + " width: 10px; height: 10px;");

        Assert.True(box.CreatesStackingContext);
    }

    [Fact]
    public void ClientSizeIsThePaddingBox()
    {
        var box = Lay(
            "width: 100px; height: 50px; padding: 5px; border: 2px solid #000;");

        Assert.Equal(box.PaddingBox.Width, box.ClientWidth, 3);
        Assert.Equal(box.PaddingBox.Height, box.ClientHeight, 3);
        Assert.Equal(110, box.ClientWidth, 3);
        Assert.Equal(60, box.ClientHeight, 3);
    }

    [Fact]
    public void ScrollSizeCoversOverflowingChildren()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = TestSupport.Layout(
            "#root { width: 100px; height: 100px; overflow: auto; }"
            + " #child { width: 300px; height: 250px; }",
            root);
        var box = TestSupport.Box(result, root);

        Assert.True(box.IsScrollContainer);
        Assert.Equal(100, box.ClientWidth, 3);
        Assert.Equal(100, box.ClientHeight, 3);
        Assert.True(box.ScrollWidth >= 300);
        Assert.True(box.ScrollHeight >= 250);
    }

    [Fact]
    public void OverflowHiddenIsAScrollContainerButVisibleIsNot()
    {
        Assert.True(Lay("overflow: hidden; width: 10px; height: 10px;").IsScrollContainer);
        Assert.False(Lay("overflow: visible; width: 10px; height: 10px;").IsScrollContainer);
    }

    [Fact]
    public void MarginBoxIncludesEveryResolvedSide()
    {
        var box = Lay(
            "width: 100px; height: 50px;"
            + " margin: 1px 2px 3px 4px; padding: 5px; border: 1px solid #000;");

        Assert.Equal(4, box.Margin.Left, 3);
        Assert.Equal(2, box.Margin.Right, 3);
        Assert.Equal(1, box.Margin.Top, 3);
        Assert.Equal(3, box.Margin.Bottom, 3);
        Assert.Equal(box.BorderBox.Width + 6, box.MarginBox.Width, 3);
        Assert.Equal(box.BorderBox.Height + 4, box.MarginBox.Height, 3);
    }
}
