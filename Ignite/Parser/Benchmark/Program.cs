using System.Diagnostics;
using System.Globalization;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using Ignite.Parser;
using Ignite.Parser.Builder;
using Ignite.Parser.Compilation;
using Ignite.Parser.Objects;

if (args.Length > 0 && args[0] == "--quick")
{
    QuickBench.Run();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(ExactMatchBenchmarks).Assembly).Run(args);

[Config(typeof(BenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 10, iterationCount: 10)]
public class ExactMatchBenchmarks
{
    private string _css = null!;
    private string[] _tokens = null!;
    private List<Token> _tokenBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _ = Tokenizer.ExactMatch("".AsSpan());

        _tokens = TokenSamples.CommonTokens;
        _css = CssCorpus.BuildFromRules(1_000_000);
        _tokenBuffer = new List<Token>(_css.Length / 3);

        // Grow the buffer during setup so measured runs allocate nothing.
        Tokenizer.Parse(_css.AsSpan(), _tokenBuffer);
    }

    [Benchmark(Baseline = true)]
    public int MatchCommonTokens()
    {
        var tokens = _tokens;
        var acc = 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            acc += Tokenizer.ExactMatch(tokens[i].AsSpan());
        }
        return acc;
    }

    [Benchmark]
    public int ParseCssDocument()
    {
        var tokens = _tokenBuffer;
        tokens.Clear();
        Tokenizer.Parse(_css.AsSpan(), tokens);
        return tokens.Count;
    }
}

internal sealed class BenchmarkConfig : ManualConfig
{
    public BenchmarkConfig()
    {
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        AddColumn(new ThroughputColumn(document: true));
        AddColumn(new ThroughputColumn(document: false));
        AddColumn(new AllocationColumn());
        AddColumn(new GcCollectionsColumn());
    }
}

/// <summary>
/// Gen0/Gen1/Gen2 collections per 1000 operations, shown even when zero.
/// </summary>
internal sealed class GcCollectionsColumn : IColumn
{
    public string Id => "GC/1k ops";
    public string ColumnName => Id;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 2;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Gen0/Gen1/Gen2 garbage collections per 1000 operations";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report is null)
        {
            return "?";
        }

        var gc = report.GcStats;
        var scale = gc.TotalOperations > 0 ? 1000.0 / gc.TotalOperations : 1.0;
        return $"{gc.Gen0Collections * scale:N0}/{gc.Gen1Collections * scale:N0}/{gc.Gen2Collections * scale:N0}";
    }
}

/// <summary>
/// Shows allocation explicitly, including zero. BenchmarkDotNet's built-in
/// Allocated column renders zero as "-", which looks like missing data.
/// </summary>
internal sealed class AllocationColumn : IColumn
{
    public string Id => "Alloc/op";
    public string ColumnName => Id;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Size;
    public string Legend => "Managed bytes allocated per operation";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report is null)
        {
            return "?";
        }

        var bytes = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase);
        if (bytes is null)
        {
            return "?";
        }

        if (bytes.Value == 0)
        {
            return "0 B";
        }

        return bytes.Value < 1024
            ? $"{bytes.Value} B"
            : $"{bytes.Value / 1024.0:N1} KB";
    }
}

/// <summary>
/// Derived throughput columns. Computed from data available in the reporting
/// process, because BenchmarkDotNet runs the benchmarks in a separate process.
/// </summary>
internal sealed class ThroughputColumn : IColumn
{
    private readonly bool _document;

    public ThroughputColumn(bool document)
    {
        _document = document;
    }

    public string Id => _document ? "CSS MB/s" : "M tok/s";
    public string ColumnName => Id;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 0;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => _document
        ? "CSS document megabytes tokenized per second"
        : "Millions of exact-match tokens per second";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var isDocument = benchmarkCase.Descriptor.WorkloadMethod.Name == nameof(ExactMatchBenchmarks.ParseCssDocument);
        if (_document != isDocument)
        {
            return "-";
        }

        var report = summary[benchmarkCase];
        if (report?.ResultStatistics is null)
        {
            return "?";
        }

        var ns = report.ResultStatistics.Mean;
        if (ns <= 0)
        {
            return "?";
        }

        var value = _document
            ? CssInfo.Value.Bytes / ns * 1e3
            : TokenSamples.CommonTokens.Length / ns * 1e3;

        return value.ToString("N1", CultureInfo.InvariantCulture);
    }

    private static readonly Lazy<(long Bytes, int Tokens)> CssInfo = new(static () =>
    {
        var css = CssCorpus.BuildFromRules(1_000_000);
        var tokens = new List<Token>(css.Length / 3);
        Tokenizer.Parse(css.AsSpan(), tokens);
        return (Encoding.UTF8.GetByteCount(css), tokens.Count);
    });
}

internal static class TokenSamples
{
    public static readonly string[] CommonTokens = Build();

    private static string[] Build()
    {
        string[] hot =
        {
            "color", "background", "background-color", "background-image", "display",
            "position", "top", "left", "right", "bottom", "width", "height", "margin",
            "margin-top", "margin-right", "margin-bottom", "margin-left", "padding",
            "padding-top", "padding-right", "padding-bottom", "padding-left", "border",
            "border-color", "border-radius", "border-style", "border-width", "font",
            "font-family", "font-size", "font-weight", "line-height", "text-align",
            "text-decoration", "transform", "transition", "animation", "opacity",
            "overflow", "z-index", "flex", "flex-direction", "align-items",
            "justify-content", "grid-template-columns", "gap", "cursor", "content",
            "visibility", "white-space", "rgba", "rgb", "calc", "var",
            "linear-gradient", "translate", "rotate", "scale", ":hover", ":focus",
            ":active", ":first-child", ":nth-child", "::before", "::after",
            "::placeholder", "nonexistent-property", "not-a-function"
        };

        const int repeats = 64;
        var result = new string[hot.Length * repeats];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = hot[i % hot.Length];
        }

        return result;
    }

    /// <summary>UTF-8 byte count of every sampled token, used for text throughput.</summary>
    public static long TotalCharBytes(string[] tokens)
    {
        var total = 0L;
        for (var i = 0; i < tokens.Length; i++)
        {
            total += Encoding.UTF8.GetByteCount(tokens[i]);
        }
        return total;
    }
}

internal static class CssCorpus
{
    /// <summary>
    /// Builds a deterministic CSS document out of every registered rule and
    /// repeats it until the requested size is reached.
    /// </summary>
    public static string BuildFromRules(int targetBytes)
    {
        _ = Tokenizer.ExactMatch("".AsSpan());

        var sampleBuilder = new StringBuilder(32 * 1024);
        foreach (var pair in RuleBuilder.ExactRules)
        {
            var text = InternTable.GetString(pair.Key);
            if (text is null)
            {
                continue;
            }

            switch (pair.Value)
            {
                case TokenKind.PropertyName:
                    sampleBuilder.Append(".p{").Append(text).Append(":0;}");
                    break;

                case TokenKind.Function:
                    sampleBuilder.Append(".f{filter:").Append(text).Append("(0);}");
                    break;

                case TokenKind.PseudoClass:
                case TokenKind.PseudoElement:
                    sampleBuilder.Append('a').Append(text).Append("{color:red;}");
                    break;
            }
        }

        var sample = sampleBuilder.ToString();
        if (sample.Length == 0)
        {
            sample = "body{margin:0;padding:0;color:red;display:block;}";
        }

        var output = new StringBuilder(targetBytes + sample.Length);
        while (output.Length < targetBytes)
        {
            output.Append(sample);
        }

        return output.ToString();
    }
}

internal static class QuickBench
{
    private readonly record struct Measurement(double NanosecondsPerOp, double AllocatedBytesPerOp);

    public static void Run()
    {
        Console.WriteLine($"Runtime:  {Environment.Version}");
        Console.WriteLine($"OS:       {Environment.OSVersion}");
        Console.WriteLine($"CPU:      {Environment.ProcessorCount} logical processors");
        Console.WriteLine($"GC:       {(System.Runtime.GCSettings.IsServerGC ? "server" : "workstation")} (latency={System.Runtime.GCSettings.LatencyMode})");
        Console.WriteLine();

        _ = Tokenizer.ExactMatch("".AsSpan());

        var tokens = TokenSamples.CommonTokens;
        var tokenBytes = TokenSamples.TotalCharBytes(tokens);

        var css = CssCorpus.BuildFromRules(1_000_000);
        var cssBytes = (long)Encoding.UTF8.GetByteCount(css);
        var tokenBuffer = new List<Token>(Math.Max(16, css.Length / 3));
        Tokenizer.Parse(css.AsSpan(), tokenBuffer);
        var cssTokens = tokenBuffer.Count;

        int ParseCss()
        {
            tokenBuffer.Clear();
            Tokenizer.Parse(css.AsSpan(), tokenBuffer);
            return tokenBuffer.Count;
        }

        for (var i = 0; i < 2_000; i++)
        {
            MatchTokens(tokens);
        }
        for (var i = 0; i < 20; i++)
        {
            ParseCss();
        }

        var sink = 0;
        var match = Measure(() => MatchTokens(tokens), iterations: 10_000, runs: 3, ref sink);
        var scan = Measure(ParseCss, iterations: 100, runs: 3, ref sink);

        var matchNs = match.NanosecondsPerOp;
        var scanNs = scan.NanosecondsPerOp;

        var compileWatch = Stopwatch.StartNew();
        var compileAllocBefore = GC.GetAllocatedBytesForCurrentThread();
        var gc0 = GC.CollectionCount(0);
        var gc1 = GC.CollectionCount(1);
        var gc2 = GC.CollectionCount(2);
        var compiled = new Compiler().Compile(RuleBuilder.ExactRules);
        var compileBytes = GC.GetAllocatedBytesForCurrentThread() - compileAllocBefore;
        compileWatch.Stop();
        var gc0After = GC.CollectionCount(0);
        var gc1After = GC.CollectionCount(1);
        var gc2After = GC.CollectionCount(2);

        Console.WriteLine("Exact matcher (token stream)");
        Console.WriteLine($"  tokens/op:            {tokens.Length:N0}");
        Console.WriteLine($"  time/op:              {matchNs:N0} ns");
        Console.WriteLine($"  time/token:           {matchNs / tokens.Length:F2} ns");
        Console.WriteLine($"  throughput:           {tokens.Length / matchNs * 1e3:N1} M tokens/s");
        Console.WriteLine($"  token-text:           {tokenBytes / matchNs * 1e3:N1} MB/s");
        Console.WriteLine($"  allocations/op:       {match.AllocatedBytesPerOp:N1} B");
        Console.WriteLine();

        Console.WriteLine("CSS parse (Tokenizer.Parse)");
        Console.WriteLine($"  document:             {cssBytes:N0} bytes");
        Console.WriteLine($"  tokens/op:            {cssTokens:N0}");
        Console.WriteLine($"  time/op:              {scanNs:N0} ns");
        Console.WriteLine($"  throughput:           {cssBytes / scanNs * 1e3:N1} MB/s");
        Console.WriteLine($"  token throughput:     {cssTokens / scanNs * 1e3:N1} M tokens/s");
        Console.WriteLine($"  allocations/op:       {scan.AllocatedBytesPerOp:N1} B");
        Console.WriteLine();

        Console.WriteLine("Compilation");
        Console.WriteLine($"  time:                 {compileWatch.Elapsed.TotalMilliseconds:N2} ms");
        Console.WriteLine($"  allocations:          {compileBytes:N0} B");
        Console.WriteLine($"  GC collections:       {gc0After - gc0}/{gc1After - gc1}/{gc2After - gc2} (gen0/1/2)");
        Console.WriteLine();
        Console.WriteLine($"sink={sink & 1}, compiledResult={compiled("color".AsSpan())}");
    }

    private static Measurement Measure(Func<int> operation, int iterations, int runs, ref int sink)
    {
        var bestNs = double.MaxValue;
        var allocatedBytes = 0.0;

        for (var run = 0; run < runs; run++)
        {
            // Start the timer before sampling allocations so the Stopwatch
            // instance itself is not counted as benchmark allocation.
            var sw = Stopwatch.StartNew();
            var allocBefore = GC.GetAllocatedBytesForCurrentThread();
            var local = 0;
            for (var i = 0; i < iterations; i++)
            {
                local += operation();
            }
            sw.Stop();
            var allocAfter = GC.GetAllocatedBytesForCurrentThread();

            sink += local & 1;

            var ns = sw.Elapsed.TotalNanoseconds / iterations;
            var alloc = (allocAfter - allocBefore) / (double)iterations;
            if (ns < bestNs)
            {
                bestNs = ns;
                allocatedBytes = alloc;
            }
        }

        return new Measurement(bestNs, allocatedBytes);
    }

    private static int MatchTokens(string[] tokens)
    {
        var acc = 0;
        for (var i = 0; i < tokens.Length; i++)
        {
            acc += Tokenizer.ExactMatch(tokens[i].AsSpan());
        }
        return acc;
    }
}

