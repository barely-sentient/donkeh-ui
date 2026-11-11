using System.Diagnostics;
using System.Globalization;
using Donkeh.Data;
using Donkeh.Parser;
using Donkeh.Parser.Objects;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.DataEngine.Benchmarks;

/// <summary>
/// One-shot cold/warm profiling. Useful for seeing first-run interning cost and
/// the actual shape of the built table, which a statistical benchmark hides.
/// Run with <c>--profile</c> or <c>--profile &lt;chars&gt;</c>.
/// </summary>
public static class Profile
{
    private static readonly int[] DefaultSizes = [1_000_000, 4_000_000, 16_000_000];

    public static void Run(string[] args)
    {
        var sizes = DefaultSizes;

        if (args.Length > 1 && int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requested))
        {
            sizes = [requested];
        }

        Console.WriteLine($"Runtime: {Environment.Version}");
        Console.WriteLine($"OS:      {Environment.OSVersion}");
        Console.WriteLine($"CPU:     {Environment.ProcessorCount} logical processors");
        Console.WriteLine();

        foreach (var size in sizes)
        {
            RunCorpus(size, repeated: false);
            RunCorpus(size, repeated: true);
        }
    }

    private static void RunCorpus(int targetChars, bool repeated)
    {
        var label = repeated ? "repeated selectors" : "realistic";
        var source = repeated
            ? CssCorpus.CreateRepeated(targetChars)
            : CssCorpus.Create(targetChars);

        Console.WriteLine($"=== {label}: {source.Length:N0} chars ===");

        var engine = new Engine(Condition.AlwaysTrueCompiler);

        // Cold full pipeline: tokenization, interning and table construction.
        var cold = Measure(() => engine.Build(source));
        var coldTable = (DataTable)cold.Result;
        Print("cold build (string)", cold, source.Length);
        Console.WriteLine(
            $"  table: selectors={coldTable.SelectorCount:N0}, " +
            $"rules={coldTable.RuleCount:N0}, conditions={coldTable.ConditionCount:N0}");
        coldTable = null!;
        cold = default;

        // Tokenizer in isolation, with strings already interned by the cold run.
        List<Token> tokens = null!;
        var tokenize = Measure(() =>
        {
            tokens = Tokenizer.Parse(source);
            return tokens.Count;
        });
        Print("tokenize (warm interns)", tokenize, source.Length);

        // Engine only: the token list is supplied, no lexing or interning of text.
        var engineOnly = Measure(() => engine.Build(tokens));
        var engineTable = (DataTable)engineOnly.Result;
        Print("engine only (tokens)", engineOnly, source.Length);
        engineTable = null!;

        // Steady-state full pipeline.
        var warm = Measure(() => engine.Build(source));
        var warmTable = (DataTable)warm.Result;
        Print("warm build (string)", warm, source.Length);
        Console.WriteLine(
            $"  table: selectors={warmTable.SelectorCount:N0}, " +
            $"rules={warmTable.RuleCount:N0}, conditions={warmTable.ConditionCount:N0}");
        Console.WriteLine($"  tokens: {tokens.Count:N0}");
        Console.WriteLine();
    }

    private static Measurement Measure(Func<object> operation)
    {
        // Touch the delegate before sampling so only the operation is counted.
        var before = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew();
        var result = operation();
        watch.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        return new Measurement(watch.Elapsed.TotalMilliseconds, allocated, result);
    }

    private static void Print(string label, Measurement measurement, int sourceChars)
    {
        var megabytes = sourceChars / 1_000_000.0;
        var throughput = measurement.Milliseconds > 0
            ? megabytes / (measurement.Milliseconds / 1000.0)
            : 0;

        Console.WriteLine(
            $"  {label,-24} {measurement.Milliseconds,10:N2} ms  " +
            $"{throughput,9:N1} MB/s  " +
            $"{measurement.AllocatedBytes / 1_048_576.0,9:N1} MB alloc");
    }

    private readonly record struct Measurement(
        double Milliseconds,
        long AllocatedBytes,
        object Result);
}
