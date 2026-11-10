using BenchmarkDotNet.Attributes;
using Ignite.Data;

namespace Ignite.LayoutLab.Benchmarks;

/// <summary>
/// Display-list optimisation. A raw frame is produced once, then the optimiser
/// runs with each feature toggled, which isolates culling, clip handling, draw
/// merging and constant compaction. <c>Passthrough</c> is the baseline cost of
/// constructing the result without optimisation.
/// </summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class PaintBenchmarks
{
    [Params(500, 2000)]
    public int Nodes;

    [Params(SceneKind.Paint, SceneKind.Mixed)]
    public SceneKind Scene;

    private InstructionBuffer _raw = null!;
    private ConstantPool _constants = null!;
    private Rect _viewport;
    private LayoutOptions _default = null!;
    private LayoutOptions _noCull = null!;
    private LayoutOptions _noMerge = null!;
    private LayoutOptions _noClip = null!;
    private LayoutOptions _compact = null!;
    private LayoutOptions _passthrough = null!;

    [GlobalSetup]
    public void Setup()
    {
        var root = Scenes.Build(Scene, Nodes);
        var rawOptions = Scenes.Options(optimise: false);
        var result = new LayoutEngine(Scenes.Table).Layout(root, rawOptions);

        _raw = result.Instructions;
        _constants = result.Constants;
        _viewport = new Rect(0, 0, rawOptions.ViewportWidth, rawOptions.ViewportHeight);

        _default = Scenes.Options();
        _noCull = Scenes.Options(cull: false);
        _noMerge = Scenes.Options(merge: false);
        _noClip = Scenes.Options(clip: false);
        _compact = Scenes.Options(compact: true);
        _passthrough = Scenes.Options(optimise: false);

        // Warm the optimiser's static state and the JIT before measuring.
        _ = Run(_default);
    }

    [Benchmark(Baseline = true)]
    public int Passthrough()
    {
        return Run(_passthrough);
    }

    [Benchmark]
    public int Optimise()
    {
        return Run(_default);
    }

    [Benchmark]
    public int OptimiseNoCulling()
    {
        return Run(_noCull);
    }

    [Benchmark]
    public int OptimiseNoMerging()
    {
        return Run(_noMerge);
    }

    [Benchmark]
    public int OptimiseNoViewportClip()
    {
        return Run(_noClip);
    }

    [Benchmark]
    public int OptimiseCompactConstants()
    {
        return Run(_compact);
    }

    private int Run(LayoutOptions options)
    {
        var result = Optimiser.Run(_raw, _constants, _viewport, options);

        return result.Instructions.Count
            + result.Constants.Count
            + result.Stats.Culled
            + result.Stats.Merged;
    }
}
