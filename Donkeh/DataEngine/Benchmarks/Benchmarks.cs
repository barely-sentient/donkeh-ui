using System.Text;
using BenchmarkDotNet.Attributes;
using Donkeh.Data;
using Donkeh.Parser;
using Donkeh.Parser.Objects;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.DataEngine.Benchmarks;

/// <summary>
/// End-to-end pipeline on a large, realistic stylesheet: tokenization, token
/// buffering, and flattening into the interning <see cref="DataTable"/>.
/// </summary>
[Config(typeof(DataEngineBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 6)]
public class ParseAndBuildBenchmarks
{
    [Params(1_000_000, 4_000_000, 16_000_000)]
    public int Size;

    private string _css = null!;
    private Engine _engine = null!;
    private List<Token> _tokens = null!;
    private List<Token> _tokenBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _css = CssCorpus.Create(Size);
        _engine = new Engine(Condition.AlwaysTrueCompiler);

        // Pre-tokenize once. This warms the process-wide intern table and gives
        // the engine-only benchmark an input token list.
        _tokens = Tokenizer.Parse(_css);

        // Tokenizer.Parse reserves roughly source.Length / 2 entries. Match that
        // capacity so the measured tokenizer pass never grows the list.
        _tokenBuffer = new List<Token>(Math.Max(_tokens.Count, (_css.Length / 2) + 16));
    }

    [Benchmark(Baseline = true)]
    public int Tokenize()
    {
        _tokenBuffer.Clear();
        Tokenizer.Parse(_css.AsSpan(), _tokenBuffer);
        return _tokenBuffer.Count;
    }

    [Benchmark]
    public int Build_FromString()
    {
        var table = _engine.Build(_css);
        return table.RuleCount;
    }

    [Benchmark]
    public int Build_FromTokens()
    {
        var table = _engine.Build(_tokens);
        return table.RuleCount;
    }
}

/// <summary>
/// Worst-case intern/dedup workload: the same selectors and conditions are
/// repeated for the whole document, so the engine has to do a full identity
/// lookup for every selector and condition while still appending every rule.
/// </summary>
[Config(typeof(DataEngineBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 6)]
public class RepeatedSelectorBenchmarks
{
    [Params(4_000_000, 16_000_000)]
    public int Size;

    private string _css = null!;
    private Engine _engine = null!;
    private List<Token> _tokens = null!;
    private List<Token> _tokenBuffer = null!;

    [GlobalSetup]
    public void Setup()
    {
        _css = CssCorpus.CreateRepeated(Size);
        _engine = new Engine(Condition.AlwaysTrueCompiler);
        _tokens = Tokenizer.Parse(_css);
        _tokenBuffer = new List<Token>(Math.Max(_tokens.Count, (_css.Length / 2) + 16));
    }

    [Benchmark(Baseline = true)]
    public int Tokenize()
    {
        _tokenBuffer.Clear();
        Tokenizer.Parse(_css.AsSpan(), _tokenBuffer);
        return _tokenBuffer.Count;
    }

    [Benchmark]
    public int Build_FromString()
    {
        var table = _engine.Build(_css);
        return table.RuleCount;
    }

    [Benchmark]
    public int Build_FromTokens()
    {
        var table = _engine.Build(_tokens);
        return table.RuleCount;
    }
}

/// <summary>
/// Consumes a built table the way a renderer would: discriminator candidate
/// lookups, condition-gated rule scans, and a cold condition evaluation sweep
/// after invalidating the context version.
/// </summary>
[Config(typeof(DataEngineBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 6)]
public class QueryBenchmarks
{
    [Params(4_000_000)]
    public int Size;

    private DataTable _table = null!;
    private Token[] _discriminators = null!;
    private RendererContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = new Engine(Condition.AlwaysTrueCompiler).Build(CssCorpus.Create(Size));

        // Query every discriminator group once. Querying one selector at a time
        // would repeatedly materialise the same large pseudo-class groups and
        // measure O(n^2) work that a renderer never performs.
        var seen = new HashSet<(TokenKind Kind, int InternedId)>();
        var discriminators = new List<Token>(_table.DiscriminatorCount);

        for (var i = 0; i < _table.SelectorCount; i++)
        {
            var discriminator = _table.GetSelector(i).PrimaryDiscriminator;
            if (seen.Add((discriminator.Kind, discriminator.InternedId)))
            {
                discriminators.Add(discriminator);
            }
        }

        _discriminators = discriminators.ToArray();

        _context = new RendererContext
        {
            ViewportWidth = 1920,
            ViewportHeight = 1080
        };
    }

    [Benchmark(Baseline = true)]
    public int RuleScan()
    {
        var total = 0;
        for (var i = 0; i < _table.RuleCount; i++)
        {
            var rule = _table.GetRule(i);
            total += rule.SelectorId + rule.PropertyId + rule.ValueId + rule.ConditionId;
        }

        return total;
    }

    [Benchmark]
    public int SelectorLookup()
    {
        var total = 0;
        for (var i = 0; i < _discriminators.Length; i++)
        {
            total += _table.GetSelectorIds(_discriminators[i]).Length;
        }

        return total;
    }

    [Benchmark]
    public int ConditionMatch_Cached()
    {
        var matched = 0;
        for (var i = 0; i < _table.RuleCount; i++)
        {
            if (_table.MatchesRule(_table.GetRule(i), _context))
            {
                matched++;
            }
        }

        return matched;
    }

    [Benchmark]
    public int ConditionMatch_AfterInvalidate()
    {
        _context.Invalidate();

        var matched = 0;
        for (var i = 0; i < _table.RuleCount; i++)
        {
            if (_table.MatchesRule(_table.GetRule(i), _context))
            {
                matched++;
            }
        }

        return matched;
    }
}
