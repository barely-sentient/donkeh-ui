namespace Ignite.LayoutLab.Tests;

public class AnimationTests
{
    [Fact]
    public void AnimatesToOnlyRotateKeyframe()
    {
        const string css = """
            @keyframes spin { to { transform: rotate(360deg); } }
            .spin { width: 20px; height: 20px; animation: 1s linear infinite spin; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root", null, "spin");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 0.5;
        var result = engine.Layout(root, options);
        var box = TestSupport.Box(result, root);

        Assert.True(result.HasAnimations);
        Assert.True(box.IsAnimated);
        Assert.NotNull(box.Style.Transform);

        var context = options.CreateRootLengthContext();
        var matrix = box.Style.Transform!.Resolve(
            box.BorderBox.Width,
            box.BorderBox.Height,
            in context);

        Assert.Equal(-1, matrix.M11, 2);
        Assert.Equal(0, matrix.M12, 2);
    }

    [Fact]
    public void InterpolatesOpacityBetweenKeyframes()
    {
        const string css = """
            @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
            .fade { animation: 1s linear 1 fade; }
            """;

        var table = TestSupport.Build(css);
        Assert.Equal(2, table.Keyframes.Count);
        Assert.Equal("fade", Ignite.Parser.InternTable.GetString(table.Keyframes[0].NameId));
        Assert.Equal("opacity", Ignite.Parser.InternTable.GetString(table.Keyframes[0].PropertyId));
        Assert.Equal("0", Ignite.Parser.InternTable.GetString(table.Keyframes[0].ValueId));
        Assert.True(Ignite.Parser.InternTable.TryGetId("fade", out var nameId));
        Assert.Equal(table.Keyframes[0].NameId, nameId);
        var root = TestSupport.Node("div", "root", null, "fade");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 0.5;
        var result = engine.Layout(root, options);
        var box = TestSupport.Box(result, root);

        Assert.NotNull(box.Style.Animations);
        var spec = Assert.Single(box.Style.Animations);
        Assert.Equal("fade", spec.Name);
        Assert.Equal(1f, spec.Duration);
        Assert.Equal(1f, spec.IterationCount);
        Assert.Equal("linear", spec.TimingFunction);
        Assert.True(box.IsAnimated);
        Assert.Equal(0.5f, box.Style.Opacity, 2);
    }

    [Fact]
    public void ForwardsFillKeepsFinalValueWithoutRunning()
    {
        const string css = """
            @keyframes fadeout { from { opacity: 1; } to { opacity: 0; } }
            .fade { animation: 1s linear 1 fadeout forwards; }
            """;

        var table = TestSupport.Build(css);
        var root = TestSupport.Node("div", "root", null, "fade");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 2;
        var result = engine.Layout(root, options);
        var box = TestSupport.Box(result, root);

        Assert.True(box.IsAnimated);
        Assert.False(result.HasAnimations);
        Assert.Equal(0f, box.Style.Opacity, 2);
    }

    [Fact]
    public void FiniteAnimationStopsWithoutFill()
    {
        const string css = """
            @keyframes fade { from { opacity: 0; } to { opacity: 1; } }
            .fade { animation: 1s linear 1 fade; }
            """;

        var table = TestSupport.Build(css);
        Assert.Equal(2, table.Keyframes.Count);
        Assert.Equal("fade", Ignite.Parser.InternTable.GetString(table.Keyframes[0].NameId));
        Assert.Equal("opacity", Ignite.Parser.InternTable.GetString(table.Keyframes[0].PropertyId));
        Assert.Equal("0", Ignite.Parser.InternTable.GetString(table.Keyframes[0].ValueId));
        Assert.True(Ignite.Parser.InternTable.TryGetId("fade", out var nameId));
        Assert.Equal(table.Keyframes[0].NameId, nameId);
        var root = TestSupport.Node("div", "root", null, "fade");
        var options = TestSupport.Options();
        var engine = new LayoutEngine(table);

        options.AnimationTimeSeconds = 0;
        engine.Layout(root, options);

        options.AnimationTimeSeconds = 2;
        var result = engine.Layout(root, options);
        var box = TestSupport.Box(result, root);

        Assert.False(box.IsAnimated);
        Assert.False(result.HasAnimations);
        Assert.Equal(1f, box.Style.Opacity, 2);
    }
}
