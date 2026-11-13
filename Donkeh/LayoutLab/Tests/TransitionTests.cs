namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification-style tests for the CSS transition runtime. Stylesheets are
/// compiled from real CSS so shorthand parsing, cascade and interpolation are
/// exercised together.
/// </summary>
public class TransitionTests
{
    [Fact]
    public void ParsesTransitionShorthandWithCubicBezier()
    {
        var style = TestSupport.Style("""
            transition:
                height 0.24s cubic-bezier(0.2, 0.8, 0.2, 1),
                padding 0.24s cubic-bezier(0.2, 0.8, 0.2, 1);
            """);

        Assert.NotNull(style.Transitions);
        Assert.Equal(2, style.Transitions!.Count);

        var height = style.Transitions[0];
        Assert.Equal(CssProperty.Height, height.Property);
        Assert.Equal(0.24f, height.Duration, 3);
        Assert.Equal(0f, height.Delay, 3);
        Assert.Equal("cubic-bezier(0.2, 0.8, 0.2, 1)", height.TimingFunction);

        var padding = style.Transitions[1];
        Assert.Equal(CssProperty.Padding, padding.Property);
        Assert.Equal(0.24f, padding.Duration, 3);
        Assert.Equal("cubic-bezier(0.2, 0.8, 0.2, 1)", padding.TimingFunction);
    }

    [Fact]
    public void TransitionShorthandLonghandsOverlayTheLayers()
    {
        var style = TestSupport.Style("""
            transition: height 0.5s linear, padding 0.25s ease-in;
            transition-delay: 100ms;
            """);

        Assert.NotNull(style.Transitions);
        Assert.Equal(2, style.Transitions!.Count);
        Assert.Equal(0.1f, style.Transitions[0].Delay, 3);
        Assert.Equal(0.1f, style.Transitions[1].Delay, 3);
        Assert.Equal("linear", style.Transitions[0].TimingFunction);
        Assert.Equal("ease-in", style.Transitions[1].TimingFunction);
    }

    [Fact]
    public void TransitionLonghandOrderDoesNotDropLayerValues()
    {
        var style = TestSupport.Style("""
            transition-duration: 1s;
            transition-property: height, padding;
            """);

        Assert.NotNull(style.Transitions);
        Assert.Equal(2, style.Transitions!.Count);
        Assert.Equal(CssProperty.Height, style.Transitions[0].Property);
        Assert.Equal(CssProperty.Padding, style.Transitions[1].Property);
        Assert.Equal(1f, style.Transitions[0].Duration, 3);
        Assert.Equal(1f, style.Transitions[1].Duration, 3);
    }

    [Fact]
    public void HeightAndPaddingInterpolateTowardsTheTarget()
    {
        const string css = """
            .box { width: 100px; height: 10px; padding: 0px; transition: height 1s linear, padding 1s linear; }
            .box.open { height: 110px; padding: 20px; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root", null, "box");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        var first = engine.Layout(root, options);
        Assert.Equal(10f, TestSupport.Box(first, root).Style.Height.Value, 3);

        root.AddClass("open");
        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 0.5;
        var midway = engine.Layout(root, options);
        var midwayBox = TestSupport.Box(midway, root);

        Assert.True(midway.HasAnimations);
        Assert.True(midway.HasLayoutAnimations);
        Assert.True(midwayBox.IsAnimated);
        Assert.Equal(60f, midwayBox.Style.Height.Value, 3);
        Assert.Equal(10f, midwayBox.Style.Padding.Top.Value, 3);
        Assert.Equal(10f, midwayBox.Style.Padding.Right.Value, 3);
        Assert.Equal(10f, midwayBox.Style.Padding.Bottom.Value, 3);
        Assert.Equal(10f, midwayBox.Style.Padding.Left.Value, 3);

        options.AnimationTimeSeconds = 1;
        var finished = engine.Layout(root, options);
        var finishedBox = TestSupport.Box(finished, root);

        Assert.False(finished.HasAnimations);
        Assert.Equal(110f, finishedBox.Style.Height.Value, 3);
        Assert.Equal(20f, finishedBox.Style.Padding.Top.Value, 3);
    }

    [Fact]
    public void CubicBezierTimingFunctionShapesTheTransition()
    {
        const string css = """
            .box { width: 100px; height: 0px; transition: height 1s cubic-bezier(0.2, 0.8, 0.2, 1); }
            .box.open { height: 100px; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root", null, "box");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        root.AddClass("open");
        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 0.5;
        var midway = engine.Layout(root, options);
        var height = TestSupport.Box(midway, root).Style.Height.Value;

        // The curve front-loads its progress, so halfway in time is already
        // substantially past the linear midpoint.
        Assert.True(height > 60f, $"Expected eased height above 60px but was {height}px.");
        Assert.True(height <= 100f, $"Expected eased height at most 100px but was {height}px.");
    }

    [Fact]
    public void TransitionDoesNotRunOnInitialStyle()
    {
        const string css = """
            .box { width: 100px; height: 50px; transition: height 1s linear; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root", null, "box");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        var result = engine.Layout(root, options);

        Assert.False(result.HasAnimations);
        Assert.False(TestSupport.Box(result, root).IsAnimated);
        Assert.Equal(50f, TestSupport.Box(result, root).Style.Height.Value, 3);
    }

    [Fact]
    public void TransitionStateIsReleasedWhenTheNodeUnmounts()
    {
        const string css = """
            .box { width: 100px; height: 10px; transition: height 1s linear; }
            .box.open { height: 110px; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", null, null, "box");
        root.Children = [child];

        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        engine.Layout(root, options);
        child.AddClass("open");
        engine.Layout(root, options);

        Assert.Equal(1, engine.AnimationStateCount);

        root.Children = [];
        engine.Layout(root, options);

        Assert.Equal(0, engine.AnimationStateCount);
    }
}
