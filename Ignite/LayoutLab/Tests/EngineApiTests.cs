using Ignite.Data;
using Ignite.Data.Dom;

namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Public entry-point tests for <see cref="LayoutEngine"/> and
/// <see cref="LayoutResult"/>: guards, cache ownership and the guarantee that a
/// result is an immutable snapshot of the frame it was produced from.
/// </summary>
public sealed class EngineApiTests
{
    private static LayoutOptions Options(bool optimise = false)
    {
        return new LayoutOptions
        {
            ViewportWidth = 1000,
            ViewportHeight = 800,
            Optimise = optimise,
            TextMeasurer = TestSupport.FixedMeasurer.Instance
        };
    }

    [Fact]
    public void TheConstructorRejectsANullTable()
    {
        Assert.Throws<ArgumentNullException>(() => new LayoutEngine(null!));
    }

    [Fact]
    public void LayoutRejectsANullRoot()
    {
        var engine = new LayoutEngine(TestSupport.Build("div { width: 1px; }"));

        Assert.Throws<ArgumentNullException>(() => engine.Layout(null!, Options()));
    }

    [Fact]
    public void TheValueCacheIsExposed()
    {
        var engine = new LayoutEngine(TestSupport.Build("div { color: #ff0000; }"));
        Assert.NotNull(engine.ValueCache);

        var supplied = new StyleValueCache();
        var engine2 = new LayoutEngine(TestSupport.Build("div { color: #ff0000; }"), supplied);
        Assert.Same(supplied, engine2.ValueCache);
    }

    [Fact]
    public void ARootWithDisplayNoneProducesAnEmptyResult()
    {
        var engine = new LayoutEngine(TestSupport.Build("#root { display: none; }"));
        var root = TestSupport.Node("div", "root");

        var result = engine.Layout(root, Options());

        Assert.Null(result.Root);
        Assert.Empty(result.Opcodes.ToArray());
        Assert.False(result.TryGetBox(root, out _));
    }

    [Fact]
    public void TryGetBoxRejectsUnknownNodes()
    {
        var engine = new LayoutEngine(TestSupport.Build("#root { width: 10px; height: 10px; }"));
        var root = TestSupport.Node("div", "root");
        var stranger = TestSupport.Node("div");

        var result = engine.Layout(root, Options());

        Assert.False(result.TryGetBox(stranger, out _));
    }

    [Fact]
    public void LayoutResultsAreImmutableSnapshots()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 100px; height: 50px; background: #ff0000; }"
            + " #child { width: 10px; height: 10px; background: #0000ff; }"));
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        var options = Options();

        var first = engine.Layout(root, options);
        var firstBox = TestSupport.Box(first, child);
        var firstInstructions = first.Opcodes.ToArray();

        root.Invalidate();
        child.Invalidate();
        var second = engine.Layout(root, options);

        // The second frame must not mutate the first result's box table or
        // instruction buffer through shared engine-owned storage.
        Assert.Same(firstBox, TestSupport.Box(first, child));
        Assert.Equal(firstInstructions, first.Opcodes.ToArray());
        Assert.NotSame(firstBox, TestSupport.Box(second, child));
    }

    [Fact]
    public void StatsAreEmptyWhenOptimisationIsDisabled()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 10px; height: 10px; background: #ff0000; }"));

        var result = engine.Layout(TestSupport.Node("div", "root"), Options(optimise: false));

        Assert.Equal(OptimisationStats.Empty, result.Stats);
        Assert.NotEmpty(result.Opcodes.ToArray());
    }

    [Fact]
    public void StatsCountInputInstructionsWhenOptimisationIsEnabled()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 10px; height: 10px; background: #ff0000; }"));

        var result = engine.Layout(TestSupport.Node("div", "root"), Options(optimise: true));

        Assert.True(result.Stats.InputInstructions > 0);
        Assert.True(result.Stats.OutputInstructions > 0);
        Assert.True(result.Stats.Saved >= 0);
    }

    [Fact]
    public void FrameMarkersCanBeRequestedThroughTheOptions()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 10px; height: 10px; background: #ff0000; }"));
        var options = Options(optimise: true);
        options.IncludeFrameMarkers = true;

        var result = engine.Layout(TestSupport.Node("div", "root"), options);
        var opcodes = result.Opcodes.ToArray();

        Assert.Equal(InstructionOpCode.BeginFrame, opcodes[0].OpCode);
        Assert.Equal(InstructionOpCode.EndFrame, opcodes[^1].OpCode);
    }

    [Fact]
    public void OffscreenDrawsAreCulledOnlyWhenOptimising()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 1000px; height: 100px; }"
            + " #far { position: absolute; left: 3000px; top: 0;"
            + " width: 10px; height: 10px; background: #ff0000; }"));
        var root = TestSupport.Node("div", "root");
        var far = TestSupport.Node("div", "far");
        root.Children = [far];

        var raw = engine.Layout(root, Options(optimise: false));
        var optimised = engine.Layout(root, Options(optimise: true));

        Assert.Single(TestSupport.Instructions(raw, InstructionOpCode.DrawRect));
        Assert.Empty(TestSupport.Instructions(optimised, InstructionOpCode.DrawRect));
    }

    [Fact]
    public void ViewportClippingCanBeDisabled()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 1000px; height: 100px; }"
            + " #far { position: absolute; left: 3000px; top: 0;"
            + " width: 10px; height: 10px; background: #ff0000; }"));
        var root = TestSupport.Node("div", "root");
        var far = TestSupport.Node("div", "far");
        root.Children = [far];
        var options = Options(optimise: true);
        options.ClipToViewport = false;

        var result = engine.Layout(root, options);

        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.DrawRect));
    }

    [Fact]
    public void TheTextMeasurerMeasuresTextThroughTheEngine()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 500px; font-size: 10px; }"));
        var root = TestSupport.Node("div", "root", "abcde");

        var result = engine.Layout(root, Options());
        var text = TestSupport.Box(result, root).Text;

        Assert.NotNull(text);
        Assert.Equal(50, text!.Size.Width, 3);
    }

    [Fact]
    public void TheDefaultMeasurerIsUsedWhenNoneIsSupplied()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 500px; font-size: 10px; }"));
        var root = TestSupport.Node("div", "root", "abcde");
        var options = Options();
        options.TextMeasurer = null;

        var result = engine.Layout(root, options);
        var text = TestSupport.Box(result, root).Text;

        Assert.NotNull(text);
        Assert.True(text!.Size.Width > 0);
    }

    [Fact]
    public void DisplayNoneSubtreeDoesNotConsumeLayoutDepth()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#hidden { display: none; } div { width: 10px; height: 10px; }"));
        var root = TestSupport.Node("div", "root");
        var hidden = TestSupport.Node("div", "hidden");
        var child = TestSupport.Node("div");
        root.Children = [hidden];
        hidden.Children = [child];

        var result = engine.Layout(root, Options());

        Assert.True(result.TryGetBox(root, out _));
        Assert.False(result.TryGetBox(hidden, out _));
        Assert.False(result.TryGetBox(child, out _));
    }

    [Fact]
    public void TheEngineKeepsWorkingAfterClearCaches()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 10px; height: 10px; background: #ff0000; }"));
        var root = TestSupport.Node("div", "root");

        engine.Layout(root, Options());
        engine.ClearCaches();
        var result = engine.Layout(root, Options());

        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.DrawRect));
    }
}
