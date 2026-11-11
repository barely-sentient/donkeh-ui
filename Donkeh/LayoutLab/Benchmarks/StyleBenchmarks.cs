using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using Donkeh.Data;
using Donkeh.Data.Dom;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>
/// Cascade and declaration-application costs. Matching is benchmarked on its
/// own, then combined with <see cref="StyleResolver"/> using a warm and a fresh
/// value cache. The tree has four children per parent, so parent styles are
/// resolved before their descendants in breadth-first order.
/// </summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class StyleBenchmarks
{
    private const int Fanout = 4;

    [Params(100, 500, 2000)]
    public int Nodes;

    private sealed class NodeRecord
    {
        public required IgniteSupportedDomNode Dom { get; init; }
        public int Parent { get; init; }
        public int Depth { get; init; }
        public MatchedRule[] Matches { get; set; } = [];
    }

    private DataTable _table = null!;
    private List<NodeRecord> _nodes = null!;
    private ComputedStyle[] _styles = null!;
    private MatchedRule[] _matchBuffer = null!;
    private StyleResolver _resolver = null!;
    private StyleValueCache _cache = null!;
    private LayoutOptions _options = null!;
    private RendererContext _context = null!;
    private ComputedStyle? _leafParent;
    private NodeRecord _leaf = null!;

    [GlobalSetup]
    public void Setup()
    {
        _table = new Engine(Condition.AlwaysTrueCompiler).Build(StyleCss);
        _options = Scenes.Options();
        _context = new RendererContext();
        _nodes = BuildTree(Nodes);
        _styles = new ComputedStyle[_nodes.Count];
        _cache = new StyleValueCache();
        _resolver = new StyleResolver(_table, _cache, _options);

        var maxMatches = 0;

        foreach (var record in _nodes)
        {
            record.Matches = Match(record.Dom);
            maxMatches = Math.Max(maxMatches, record.Matches.Length);
        }

        _matchBuffer = new MatchedRule[Math.Max(16, maxMatches + 4)];

        // Populate the parent chain and value cache once so the isolated
        // benchmarks start from a realistic warm state.
        _ = ResolveAll(_resolver);

        _leaf = _nodes[^1];
        _leafParent = _leaf.Parent >= 0 ? _styles[_leaf.Parent] : null;
    }

    [Benchmark(Baseline = true)]
    public int MatchRules()
    {
        var checksum = 0;

        for (var i = 0; i < _nodes.Count; i++)
        {
            var count = _table.MatchRules(_nodes[i].Dom, _context, _matchBuffer);
            checksum += count;
        }

        return checksum;
    }

    [Benchmark]
    public int MatchAndResolveWarm()
    {
        var checksum = 0;

        for (var i = 0; i < _nodes.Count; i++)
        {
            var record = _nodes[i];
            var count = _table.MatchRules(record.Dom, _context, _matchBuffer);
            var parent = record.Parent >= 0 ? _styles[record.Parent] : null;
            var style = _resolver.Resolve(record.Dom, parent, _matchBuffer.AsSpan(0, count));
            _styles[i] = style;
            checksum += StyleChecksum(style);
        }

        return checksum;
    }

    [Benchmark]
    public int ResolveWarm()
    {
        return ResolveAll(_resolver);
    }

    [Benchmark]
    public int ResolveCold()
    {
        var cache = new StyleValueCache();
        var resolver = new StyleResolver(_table, cache, _options);
        return ResolveAll(resolver);
    }

    [Benchmark]
    public int ResolveLeaf()
    {
        var style = _resolver.Resolve(_leaf.Dom, _leafParent, _leaf.Matches);
        return StyleChecksum(style);
    }

    [Benchmark]
    public int ResolveRoot()
    {
        var root = _nodes[0];
        var style = _resolver.Resolve(root.Dom, null, root.Matches);
        return StyleChecksum(style);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private int ResolveAll(StyleResolver resolver)
    {
        var checksum = 0;

        for (var i = 0; i < _nodes.Count; i++)
        {
            var record = _nodes[i];
            var parent = record.Parent >= 0 ? _styles[record.Parent] : null;
            var style = resolver.Resolve(record.Dom, parent, record.Matches);
            _styles[i] = style;
            checksum += StyleChecksum(style);
        }

        return checksum;
    }

    private MatchedRule[] Match(IgniteSupportedDomNode node)
    {
        var buffer = new MatchedRule[16];
        var count = _table.MatchRules(node, _context, buffer);

        if (count > buffer.Length)
        {
            Array.Resize(ref buffer, count);
            count = _table.MatchRules(node, _context, buffer);
        }

        return buffer.AsSpan(0, count).ToArray();
    }

    private static int StyleChecksum(ComputedStyle style)
    {
        return (int)style.Color ^ (int)style.BackgroundColor ^ (int)style.Display ^ style.ZIndex;
    }

    private static List<NodeRecord> BuildTree(int target)
    {
        var root = Scenes.Element("div", "root");
        var records = new List<NodeRecord>(target)
        {
            new() { Dom = root, Parent = -1, Depth = 0 }
        };

        var queue = new Queue<int>();
        queue.Enqueue(0);

        while (records.Count < target)
        {
            var parentIndex = queue.Dequeue();
            var parent = records[parentIndex];
            var depth = parent.Depth + 1;
            var children = new List<IgniteSupportedDomNode>(Fanout);

            for (var child = 0; child < Fanout && records.Count < target; child++)
            {
                var index = records.Count;
                var classes = new List<string>(4) { "node", "label" };

                if (index % 5 == 0)
                {
                    classes.Add("accent");
                }

                if (depth >= 3)
                {
                    classes.Add("deep");
                }

                var node = Scenes.Element("div", null, $"Node {index}", [.. classes]);
                records.Add(new NodeRecord { Dom = node, Parent = parentIndex, Depth = depth });
                children.Add(node);
                queue.Enqueue(index);
            }

            parent.Dom.Children = [.. children];
        }

        return records;
    }

    private const string StyleCss = """
        #root {
            font-size: 16px;
            color: #111827;
            background-color: #ffffff;
        }

        .node {
            margin: 1px;
            padding: 2px;
            border: 1px solid #e5e7eb;
            background-color: #f9fafb;
        }

        .label {
            color: #374151;
        }

        .node.label {
            line-height: 20px;
        }

        .node.accent {
            border-color: #c7d2fe;
            background-color: #eef2ff;
        }

        #root .node {
            padding: 3px;
        }

        #root .node.label {
            font-size: 14px;
        }

        #root .node.accent .label {
            font-weight: 600;
        }

        #root .node .deep {
            color: #6b7280;
        }

        #root .node.deep.label {
            letter-spacing: 0.2px;
        }
        """;
}
