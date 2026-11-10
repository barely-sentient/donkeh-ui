using BenchmarkDotNet.Attributes;
using Ignite.Data;

namespace Ignite.LayoutLab.Benchmarks;

/// <summary>
/// Hot length parsing and resolution. Expressions such as <c>calc()</c> and
/// <c>clamp()</c> allocate a small tree the first time a declaration value is
/// seen, so parsing and resolving are measured separately.
/// </summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class LengthParsingBenchmarks
{
    [Params(
        "1px",
        "1.5em",
        "50%",
        "calc(100% - 2rem)",
        "clamp(1rem, 2.5vw, 3rem)",
        "min(10px, 5%)",
        "max(2em, 10px)")]
    public string Value = "1px";

    private LengthContext _context;
    private CssLength _length;

    [GlobalSetup]
    public void Setup()
    {
        _context = new LengthContext(
            fontSize: 16,
            rootFontSize: 16,
            viewportWidth: 1280,
            viewportHeight: 800,
            percentBasis: 640,
            availableWidth: 1280,
            availableHeight: 800);

        _length = CssValueParser.ParseLength(Value);
    }

    [Benchmark(Baseline = true)]
    public float ParseAndResolve()
    {
        return CssValueParser.ParseLength(Value).Resolve(in _context);
    }

    [Benchmark]
    public float Resolve()
    {
        return _length.Resolve(in _context);
    }
}

/// <summary>CSS colour parsing across the syntaxes the engine accepts.</summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class ColorParsingBenchmarks
{
    [Params("#3b82f6", "#fff", "rgb(59, 130, 246)", "rgba(0, 0, 0, 0.5)",
        "hsl(210, 90%, 55%)", "rebeccapurple")]
    public string Value = "#3b82f6";

    [Benchmark(Baseline = true)]
    public uint ParseOr()
    {
        return CssColor.ParseOr(Value, CssColor.Black);
    }

    [Benchmark]
    public uint TryParse()
    {
        return CssColor.TryParse(Value, CssColor.Black, out var color)
            ? color
            : CssColor.Black;
    }

    [Benchmark]
    public uint Blend()
    {
        return CssColor.Blend(0x7F336699, CssColor.White);
    }
}

/// <summary>Top-level comma splitting used by shadows, gradients and image lists.</summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class ValueListParsingBenchmarks
{
    [Params(
        "0 3px 8px rgba(0, 0, 0, 0.45), inset 0 0 0 1px #ffffff",
        "linear-gradient(135deg, #0ea5e9, #6366f1)",
        "url(a.png) center / cover no-repeat, url(b.png)")]
    public string Value = "0 3px 8px rgba(0, 0, 0, 0.45), inset 0 0 0 1px #ffffff";

    [Benchmark]
    public int SplitTopLevel()
    {
        return CssValueParser.SplitTopLevel(Value, ',').Count;
    }
}

/// <summary>Grid track-list parsing, including auto-repeat and line names.</summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class GridTrackParsingBenchmarks
{
    [Params(
        "repeat(auto-fill, minmax(120px, 1fr)) 2fr [wide-start] 100px [wide-end]",
        "repeat(4, 100px) minmax(0, 1fr)",
        "1fr 2fr 1fr")]
    public string Value = "repeat(4, 100px) minmax(0, 1fr)";

    private GridTrackList _tracks = GridTrackList.Empty;

    [GlobalSetup]
    public void Setup()
    {
        _tracks = GridTrackList.Parse(Value);
    }

    [Benchmark(Baseline = true)]
    public int Parse()
    {
        return GridTrackList.Parse(Value).Count;
    }

    [Benchmark]
    public int Count()
    {
        return _tracks.Count;
    }
}

/// <summary>Grid placement parsing for longhand values and the grid-area shorthand.</summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class GridPlacementParsingBenchmarks
{
    [Params("2 / span 3", "span 2 / -1", "wide-start / wide-end", "auto / auto / 2 / 4")]
    public string Value = "2 / span 3";

    [Benchmark]
    public int Parse()
    {
        return GridPlacementParser.Parse(Value).Start;
    }
}

/// <summary>Named grid-area template parsing.</summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class GridAreaParsingBenchmarks
{
    [Params("'a a b' 'c d b'", "'header header' 'sidebar main'")]
    public string Value = "'a a b' 'c d b'";

    [Benchmark]
    public int Parse()
    {
        return GridTemplateAreas.Parse(Value)?.Areas.Count ?? 0;
    }
}
