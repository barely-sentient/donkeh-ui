using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Tests for the engine-level caches: style instances are reused across frames
/// when nothing changed, invalidated when a node, the environment or the
/// options change, and text layout is shared for identical runs. These tests
/// drive real CSS through the same engine instance so they exercise the public
/// caching contract rather than private dictionaries.
/// </summary>
public sealed class LayoutCachingTests
{
    private static LayoutOptions Options(float width = 1000)
    {
        return new LayoutOptions
        {
            ViewportWidth = width,
            ViewportHeight = 800,
            Optimise = false,
            TextMeasurer = TestSupport.FixedMeasurer.Instance
        };
    }

    private static LayoutEngine Engine(string css)
    {
        return new LayoutEngine(TestSupport.Build(css));
    }

    [Fact]
    public void StyleInstancesAreReusedWhenNothingChanged()
    {
        var engine = Engine("#root { width: 100px; color: #ff0000; } #child { color: #00ff00; }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        var options = Options();

        var first = engine.Layout(root, options);
        var second = engine.Layout(root, options);

        Assert.Same(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
        Assert.Same(
            TestSupport.Box(first, child).Style,
            TestSupport.Box(second, child).Style);
    }

    [Fact]
    public void InvalidatingANodeRefreshesItsStyle()
    {
        var engine = Engine("#root { color: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var options = Options();

        var first = engine.Layout(root, options);
        root.Invalidate();
        var second = engine.Layout(root, options);

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
        Assert.Equal(0xFFFF0000u, TestSupport.Box(second, root).Style.Color);
    }

    [Fact]
    public void ChangingAClassRefreshesTheStyle()
    {
        var engine = Engine(
            "#root.a { background: #ff0000; }"
            + " #root.b { background: #0000ff; }");
        var root = TestSupport.Node("div", "root", null, "a");
        var options = Options();

        var first = engine.Layout(root, options);
        Assert.Equal(0xFFFF0000u, TestSupport.Box(first, root).Style.BackgroundColor);

        root.AddClass("b");
        var second = engine.Layout(root, options);
        Assert.Equal(0xFF0000FFu, TestSupport.Box(second, root).Style.BackgroundColor);
    }

    [Fact]
    public void ChangingTheViewportInvalidatesStyles()
    {
        var engine = Engine("#root { width: 50%; }");
        var root = TestSupport.Node("div", "root");

        var first = engine.Layout(root, Options(1000));
        var second = engine.Layout(root, Options(400));

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
        Assert.Equal(200, TestSupport.Box(second, root).BorderBox.Width, 3);
    }

    [Fact]
    public void ChangingTheRootFontSizeInvalidatesStyles()
    {
        var engine = Engine("#root { width: 2rem; }");
        var root = TestSupport.Node("div", "root");

        var small = Options();
        var large = Options();
        large.RootFontSize = 32;

        var first = engine.Layout(root, small);
        var second = engine.Layout(root, large);

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
        Assert.Equal(64, TestSupport.Box(second, root).BorderBox.Width, 3);
    }

    [Fact]
    public void ChangingTheRendererContextInvalidatesStyles()
    {
        var engine = Engine("#root { color: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var context = new RendererContext();
        var options = Options();
        options.Context = context;

        var first = engine.Layout(root, options);
        context.ContainerWidth++;
        var second = engine.Layout(root, options);

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
    }

    [Fact]
    public void ClearCachesForcesFreshStyles()
    {
        var engine = Engine("#root { color: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var options = Options();

        var first = engine.Layout(root, options);
        engine.ClearCaches();
        var second = engine.Layout(root, options);

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
        Assert.Equal(0xFFFF0000u, TestSupport.Box(second, root).Style.Color);
    }

    [Fact]
    public void SeparateEnginesDoNotShareStyleInstances()
    {
        const string css = "#root { color: #ff0000; }";
        var root = TestSupport.Node("div", "root");
        var options = Options();

        var first = Engine(css).Layout(root, options);
        var second = Engine(css).Layout(root, options);

        Assert.NotSame(
            TestSupport.Box(first, root).Style,
            TestSupport.Box(second, root).Style);
    }

    // ------------------------------------------------------------- text cache

    [Fact]
    public void TextLayoutIsReusedAcrossFrames()
    {
        var engine = Engine("#root { width: 200px; font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "hello");
        var options = Options();

        var first = engine.Layout(root, options);
        var second = engine.Layout(root, options);

        var firstText = TestSupport.Box(first, root).Text;
        var secondText = TestSupport.Box(second, root).Text;

        Assert.NotNull(firstText);
        Assert.Same(firstText, secondText);
    }

    [Fact]
    public void ClearCachesDropsTextLayouts()
    {
        var engine = Engine("#root { width: 200px; font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "hello");
        var options = Options();

        var first = engine.Layout(root, options);
        engine.ClearCaches();
        var second = engine.Layout(root, options);

        Assert.NotSame(
            TestSupport.Box(first, root).Text,
            TestSupport.Box(second, root).Text);
    }

    [Fact]
    public void ChangingTextContentUpdatesTheLayout()
    {
        var engine = Engine("#root { width: 200px; font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "hi");
        var options = Options();

        var first = engine.Layout(root, options);
        Assert.Equal("hi", TestSupport.Box(first, root).Text!.Text);

        root.TextContent = "a much longer run";
        var second = engine.Layout(root, options);

        Assert.Equal("a much longer run", TestSupport.Box(second, root).Text!.Text);
    }

    [Fact]
    public void ChangingAvailableWidthCreatesANewTextLayout()
    {
        var engine = Engine("#root { font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "hello");

        var first = engine.Layout(root, Options(800));
        var second = engine.Layout(root, Options(400));

        Assert.NotSame(
            TestSupport.Box(first, root).Text,
            TestSupport.Box(second, root).Text);
    }

    [Fact]
    public void TheTextProviderSuppliesNodeText()
    {
        var engine = Engine("#root { width: 200px; font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "from node");
        var options = Options();
        options.TextProvider = node => node.Id == "#root" ? "from provider" : null;

        var result = engine.Layout(root, options);
        var text = TestSupport.Box(result, root).Text;

        Assert.NotNull(text);
        Assert.Equal("from provider", text!.Text);
        var draw = TestSupport.Single(result, InstructionOpCode.DrawText);
        Assert.Equal("from provider", result.Constants.GetText(draw.Payload).Text);
    }

    [Fact]
    public void TextProviderIsReconsultedEachFrame()
    {
        var engine = Engine("#root { width: 200px; font-size: 10px; }");
        var root = TestSupport.Node("div", "root", "node");
        var options = Options();
        var value = "first";
        options.TextProvider = _ => value;

        var first = engine.Layout(root, options);
        value = "second";
        root.Invalidate();
        var second = engine.Layout(root, options);

        Assert.Equal("first", TestSupport.Box(first, root).Text!.Text);
        Assert.Equal("second", TestSupport.Box(second, root).Text!.Text);
    }

    // ------------------------------------------------------------ scroll/limits

    [Fact]
    public void ScrollOffsetsFromOptionsTranslateChildren()
    {
        var engine = Engine(
            "#root { width: 100px; height: 100px; overflow: hidden; }"
            + " #child { width: 100px; height: 200px; background: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        var options = Options();
        options.ScrollOffsets = new Dictionary<IgniteSupportedDomNode, Point>
        {
            [root] = new Point(0, 40)
        };

        var result = engine.Layout(root, options);
        var childDraw = TestSupport.Single(result, InstructionOpCode.DrawRect);

        Assert.Equal(0, childDraw.Rect.X, 3);
        Assert.Equal(-40, childDraw.Rect.Y, 3);
        Assert.Contains(
            result.Opcodes.ToArray(),
            instruction => instruction.OpCode == InstructionOpCode.SetScrollOffset
                && instruction.Y == 40);
    }

    [Fact]
    public void NodeScrollOffsetsAreUsedWhenNoOverrideExists()
    {
        var engine = Engine(
            "#root { width: 100px; height: 100px; overflow: hidden; }"
            + " #child { width: 100px; height: 200px; background: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        root.ScrollOffsetY = 25;

        var result = engine.Layout(root, Options());
        var childDraw = TestSupport.Single(result, InstructionOpCode.DrawRect);

        Assert.Equal(-25, childDraw.Rect.Y, 3);
    }

    [Fact]
    public void ScrollOffsetsCanBeIgnored()
    {
        var engine = Engine(
            "#root { width: 100px; height: 100px; overflow: hidden; }"
            + " #child { width: 100px; height: 200px; background: #ff0000; }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        root.ScrollOffsetY = 25;
        var options = Options();
        options.ApplyScrollOffsets = false;

        var result = engine.Layout(root, options);
        var childDraw = TestSupport.Single(result, InstructionOpCode.DrawRect);

        Assert.Equal(0, childDraw.Rect.Y, 3);
        Assert.DoesNotContain(
            result.Opcodes.ToArray(),
            instruction => instruction.OpCode == InstructionOpCode.SetScrollOffset);
    }

    [Fact]
    public void MaxLayoutDepthStopsBoxCreation()
    {
        var engine = Engine("div { width: 10px; height: 10px; }");
        var root = TestSupport.Node("div", "root");
        var depth1 = TestSupport.Node("div");
        var depth2 = TestSupport.Node("div");
        var depth3 = TestSupport.Node("div");
        root.Children = [depth1];
        depth1.Children = [depth2];
        depth2.Children = [depth3];

        var options = Options();
        options.MaxLayoutDepth = 2;

        var result = engine.Layout(root, options);

        Assert.True(result.TryGetBox(root, out _));
        Assert.True(result.TryGetBox(depth1, out _));
        Assert.True(result.TryGetBox(depth2, out _));
        Assert.False(result.TryGetBox(depth3, out _));
    }

    [Fact]
    public void MaxLayoutDepthIsClampedToAtLeastOne()
    {
        var engine = Engine("div { width: 10px; height: 10px; }");
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div");
        var grandchild = TestSupport.Node("div");
        root.Children = [child];
        child.Children = [grandchild];

        var options = Options();
        options.MaxLayoutDepth = 0;

        var result = engine.Layout(root, options);

        Assert.True(result.TryGetBox(root, out _));
        Assert.True(result.TryGetBox(child, out _));
        Assert.False(result.TryGetBox(grandchild, out _));
    }

    [Fact]
    public void EnableCachingFalseStillProducesTheSameGeometry()
    {
        const string css =
            "#root { width: 100px; height: 50px; background: #ff0000; }"
            + " #child { width: 20px; height: 10px; background: #0000ff; }";
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var enabled = Engine(css);
        var disabled = Engine(css);

        var caching = Options();
        var noCaching = Options();
        noCaching.EnableCaching = false;

        var a = enabled.Layout(root, caching);
        var b = disabled.Layout(root, noCaching);

        Assert.Equal(
            TestSupport.Box(a, child).BorderBox.Width,
            TestSupport.Box(b, child).BorderBox.Width,
            3);
        Assert.Equal(
            TestSupport.Instructions(a, InstructionOpCode.DrawRect).Count,
            TestSupport.Instructions(b, InstructionOpCode.DrawRect).Count);
    }
}
