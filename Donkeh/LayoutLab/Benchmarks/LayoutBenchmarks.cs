using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Donkeh.Data;
using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>
/// End-to-end layout frames. Every case compiles real CSS through the data
/// engine, builds a deterministic DOM tree and lays it out with
/// <see cref="LayoutEngine"/>.
/// </summary>
/// <remarks>
/// <para><c>WarmFrame</c> reuses the engine's style and text caches across
/// frames. <c>ColdFrame</c> drops them every operation, which is the cost of a
/// first frame or a frame after a global invalidation. <c>NewEngineFrame</c>
/// also includes engine construction, matching the cost of spinning up a new
/// renderer.</para>
/// <para>Allocated bytes are reported by the memory diagnoser, including the
/// immutable <see cref="LayoutResult"/> snapshot.</para>
/// </remarks>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class LayoutBenchmarks
{
    [Params(250, 1000, 4000)]
    public int Nodes;

    [Params(SceneKind.Block, SceneKind.Flex, SceneKind.Grid, SceneKind.Paint, SceneKind.Mixed)]
    public SceneKind Scene;

    private DataTable _table = null!;
    private IgniteSupportedDomNode _root = null!;
    private LayoutEngine _engine = null!;
    private LayoutOptions _warm = null!;
    private LayoutOptions _plain = null!;
    private LayoutOptions _compact = null!;
    private LayoutOptions _noCache = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = Scenes.Table;
        _root = Scenes.Build(Scene, Nodes);
        _engine = new LayoutEngine(_table);
        _warm = Scenes.Options();
        _plain = Scenes.Options(optimise: false);
        _compact = Scenes.Options(compact: true);
        _noCache = Scenes.Options(caching: false);

        // Warm the style and text caches; not part of the measured operations.
        _ = Measure(_engine.Layout(_root, _warm));
    }

    [Benchmark(Baseline = true)]
    public int WarmFrame()
    {
        return Measure(_engine.Layout(_root, _warm));
    }

    [Benchmark]
    public int ColdFrame()
    {
        _engine.ClearCaches();
        return Measure(_engine.Layout(_root, _warm));
    }

    [Benchmark]
    public int NoCacheFrame()
    {
        return Measure(_engine.Layout(_root, _noCache));
    }

    [Benchmark]
    public int NoOptimiseFrame()
    {
        return Measure(_engine.Layout(_root, _plain));
    }

    [Benchmark]
    public int CompactConstantsFrame()
    {
        return Measure(_engine.Layout(_root, _compact));
    }

    [Benchmark]
    public int NewEngineFrame()
    {
        var engine = new LayoutEngine(_table);
        return Measure(engine.Layout(_root, _warm));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Measure(LayoutResult result)
    {
        var root = result.Root;
        var width = root is null ? 0 : (int)root.BorderBox.Width;
        var height = root is null ? 0 : (int)root.BorderBox.Height;

        return result.Instructions.Count
            + width
            + height
            + result.Stats.InputInstructions
            + result.Stats.OutputInstructions;
    }
}
