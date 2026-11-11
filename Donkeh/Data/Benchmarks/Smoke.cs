using System.Diagnostics;
using Donkeh.Data;
using Donkeh.Data.Dom;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.Data.Benchmarks;

/// <summary>
/// A quick stopwatch harness that validates the corpus and prints approximate
/// numbers without BenchmarkDotNet's process isolation and statistics. Useful
/// to smoke-test a checkout or sanity-check a change before a full run.
/// </summary>
internal static class Smoke
{
    public static void Run(int sections)
    {
        var css = BootstrapCss.Load();
        Console.WriteLine(
            $"Bootstrap {BootstrapCss.Version} CDN stylesheet: {css.Length:N0} chars");

        var buildWatch = Stopwatch.StartNew();
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        buildWatch.Stop();

        Console.WriteLine(
            $"Build: {buildWatch.Elapsed.TotalMilliseconds:N2} ms, "
            + $"{table.SelectorCount:N0} selectors, {table.RuleCount:N0} rules, "
            + $"{table.ConditionCount:N0} conditions");

        var treeWatch = Stopwatch.StartNew();
        var root = PageTree.Build(sections);
        var nodes = PageTree.Flatten(root);
        treeWatch.Stop();

        Console.WriteLine(
            $"Tree: {treeWatch.Elapsed.TotalMilliseconds:N2} ms, "
            + $"{nodes.Length:N0} elements, {sections:N0} sections");

        var buffer = new MatchedRule[512];

        var context = new RendererContext
        {
            ViewportWidth = 1440,
            ViewportHeight = 900
        };

        // Start with single-node measurements. Besides being useful on their
        // own, the 100k iterations promote the matcher out of tier-0 so the
        // full-page numbers below are not dominated by JIT.
        var hot = PageTree.FindLastWithClass(nodes, "btn-primary") ?? root;
        var cold = PageTree.FindFirstTag(nodes, "title") ?? root;

        var (hotNs, hotMatches) = MeasureQuery(table, hot, buffer);
        var (coldNs, coldMatches) = MeasureQuery(table, cold, buffer);

        Console.WriteLine(
            $"Hot leaf: {hotNs:N1} ns/query ({hotMatches:N1} matches), "
            + $"cold leaf: {coldNs:N1} ns/query ({coldMatches:N1} matches)");

        // Warm the matcher's scratch lists, sort buffer and condition cache.
        for (var pass = 0; pass < 3; pass++)
        {
            MatchAll(table, nodes, buffer, null);
        }

        const int passes = 10;
        var watch = Stopwatch.StartNew();
        var total = 0;

        for (var pass = 0; pass < passes; pass++)
        {
            total += MatchAll(table, nodes, buffer, null);
        }

        watch.Stop();

        var perPass = watch.Elapsed.TotalMilliseconds / passes;
        var perNode = watch.Elapsed.TotalNanoseconds / ((double)passes * nodes.Length);

        Console.WriteLine(
            $"Match: {perPass:N2} ms/pass, {perNode:N1} ns/node, "
            + $"{total / passes:N0} matches/pass");

        var conditionWatch = Stopwatch.StartNew();
        var conditionTotal = 0;

        for (var pass = 0; pass < passes; pass++)
        {
            context.Invalidate();
            conditionTotal += MatchAll(table, nodes, buffer, context);
        }

        conditionWatch.Stop();

        Console.WriteLine(
            $"Match with conditions: "
            + $"{conditionWatch.Elapsed.TotalMilliseconds / passes:N2} ms/pass, "
            + $"{conditionTotal / passes:N0} matches/pass");
    }

    private static int MatchAll(
        DataTable table,
        IgniteSupportedDomNode[] nodes,
        MatchedRule[] buffer,
        RendererContext? context)
    {
        var total = 0;

        for (var i = 0; i < nodes.Length; i++)
        {
            total += table.MatchRules(nodes[i], context, buffer);
        }

        return total;
    }

    private static (double Nanoseconds, int Matches) MeasureQuery(
        DataTable table,
        IgniteSupportedDomNode node,
        MatchedRule[] buffer)
    {
        const int iterations = 100_000;
        var total = 0;

        for (var i = 0; i < 1_000; i++)
        {
            total += table.MatchRules(node, null, buffer);
        }

        var watch = Stopwatch.StartNew();

        for (var i = 0; i < iterations; i++)
        {
            total += table.MatchRules(node, null, buffer);
        }

        watch.Stop();

        return (watch.Elapsed.TotalNanoseconds / iterations, total / iterations);
    }
}
