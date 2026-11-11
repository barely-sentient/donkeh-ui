using BenchmarkDotNet.Attributes;
using Donkeh.Data;
using Donkeh.Parser;
using Donkeh.Parser.Objects;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.Data.Benchmarks;

/// <summary>
/// Parses the real Bootstrap 5.3.8 CDN stylesheet. This is the build side of the
/// pipeline: tokenization, selector interning, compound planning and flattening
/// into the <see cref="DataTable"/>.
/// </summary>
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class ParseBootstrapBenchmarks
{
    private string _css = null!;
    private Engine _engine = null!;
    private List<Token> _tokens = null!;

    [GlobalSetup]
    public void Setup()
    {
        _css = BootstrapCss.Load();
        _engine = new Engine(Condition.AlwaysTrueCompiler);

        // Pre-tokenize once so the engine-only build path starts from a warm
        // intern table and a stable token list.
        _tokens = Tokenizer.Parse(_css);
    }

    [Benchmark(Baseline = true)]
    public int Tokenize()
    {
        return Tokenizer.Parse(_css).Count;
    }

    [Benchmark]
    public int Build_FromString()
    {
        return _engine.Build(_css).RuleCount;
    }

    [Benchmark]
    public int Build_FromTokens()
    {
        return _engine.Build(_tokens).RuleCount;
    }
}
