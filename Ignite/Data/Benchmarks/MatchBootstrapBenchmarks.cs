using BenchmarkDotNet.Attributes;
using Ignite.Data;
using Ignite.Data.Dom;
using Engine = Ignite.DataEngine.DataEngine;

namespace Ignite.Data.Benchmarks;

/// <summary>
/// Matches every element of a Bootstrap-shaped page against the parsed CDN
/// stylesheet, which is what a renderer does on a style recalculation. The
/// <see cref="Sections"/> parameter scales the document from a handful of
/// components to a stress-test page with thousands of them.
/// </summary>
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 3, iterationCount: 5)]
public class MatchBootstrapBenchmarks
{
    /// <summary>
    /// Number of repeated component sections (card, list group, table, form) in
    /// the generated page. Each section adds roughly sixty elements.
    /// </summary>
    [Params(8, 512, 4096)]
    public int Sections;

    private DataTable _table = null!;
    private IgniteSupportedDomNode[] _nodes = null!;
    private IgniteSupportedDomNode _hotLeaf = null!;
    private IgniteSupportedDomNode _coldLeaf = null!;
    private MatchedRule[] _buffer = null!;
    private RendererContext _context = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = new Engine(Condition.AlwaysTrueCompiler).Build(BootstrapCss.Load());

        var root = PageTree.Build(Sections);
        _nodes = PageTree.Flatten(root);

        // A button is one of the most-matched elements in the page; the title
        // element in <head> should match almost nothing.
        _hotLeaf = PageTree.FindLastWithClass(_nodes, "btn-primary") ?? root;
        _coldLeaf = PageTree.FindFirstTag(_nodes, "title") ?? root;

        // Large enough for the deepest matching node in this document. The span
        // overload still reports the true total when it is too small.
        _buffer = new MatchedRule[512];

        _context = new RendererContext
        {
            ViewportWidth = 1440,
            ViewportHeight = 900
        };
    }

    /// <summary>Full-page pass using the allocating convenience overload.</summary>
    [Benchmark(Baseline = true)]
    public int MatchAll_Array()
    {
        var total = 0;

        for (var i = 0; i < _nodes.Length; i++)
        {
            total += _table.MatchRules(_nodes[i]).Length;
        }

        return total;
    }

    /// <summary>
    /// Full-page pass using the span overload and a reusable buffer, the
    /// allocation-free path a renderer would use.
    /// </summary>
    [Benchmark]
    public int MatchAll_Span()
    {
        var total = 0;

        for (var i = 0; i < _nodes.Length; i++)
        {
            total += _table.MatchRules(_nodes[i], null, _buffer);
        }

        return total;
    }

    /// <summary>
    /// Full-page pass with a renderer context. Bootstrap's responsive rules are
    /// gated by media queries, so this exercises condition evaluation as well.
    /// </summary>
    [Benchmark]
    public int MatchAll_Span_WithConditions()
    {
        var total = 0;

        for (var i = 0; i < _nodes.Length; i++)
        {
            total += _table.MatchRules(_nodes[i], _context, _buffer);
        }

        return total;
    }

    /// <summary>
    /// Full-page pass after invalidating the condition cache, which is what
    /// happens when the viewport or environment changes.
    /// </summary>
    [Benchmark]
    public int MatchAll_Span_ConditionsInvalidated()
    {
        _context.Invalidate();

        var total = 0;

        for (var i = 0; i < _nodes.Length; i++)
        {
            total += _table.MatchRules(_nodes[i], _context, _buffer);
        }

        return total;
    }

    /// <summary>Hot single-element lookup for a heavily matched button.</summary>
    [Benchmark]
    public int MatchHotLeaf_Span()
    {
        return _table.MatchRules(_hotLeaf, null, _buffer);
    }

    /// <summary>
    /// Single-element lookup that should reject nearly the whole table, which
    /// measures candidate gathering and the fast miss path.
    /// </summary>
    [Benchmark]
    public int MatchColdLeaf_Span()
    {
        return _table.MatchRules(_coldLeaf, null, _buffer);
    }
}
