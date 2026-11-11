using System.Diagnostics;
using System.Globalization;
using Donkeh.Data;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>
/// One-shot cold/warm profiling. BenchmarkDotNet is the source of truth for
/// statistical measurements; this mode is a faster way to see the shape of a
/// frame and the cost of individual stages.
/// </summary>
/// <remarks>
/// Run with <c>--profile</c>, <c>--profile grid</c> or
/// <c>--profile grid 4000</c>.
/// </remarks>
public static class Profile
{
    private const int Warmup = 3;
    private const int Iterations = 10;

    public static void Run(string[] args)
    {
        var scenes = Enum.GetValues<SceneKind>();
        var nodes = 1000;

        if (args.Length > 1)
        {
            if (int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedNodes))
            {
                nodes = requestedNodes;
            }
            else if (Enum.TryParse<SceneKind>(args[1], ignoreCase: true, out var requestedScene))
            {
                scenes = [requestedScene];
            }
        }

        if (args.Length > 2
            && int.TryParse(args[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var explicitNodes))
        {
            nodes = explicitNodes;
        }

        Console.WriteLine($"Runtime: {Environment.Version}");
        Console.WriteLine($"OS:      {Environment.OSVersion}");
        Console.WriteLine($"CPU:     {Environment.ProcessorCount} logical processors");
        Console.WriteLine($"Target:  {nodes:N0} nodes per scene");
        Console.WriteLine();
        Console.WriteLine(
            $"{"Scene",-6} {"Nodes",8} {"Cold",24} {"Warm",24} {"Optimise",24} {"Instructions",22}");
        Console.WriteLine(new string('-', 110));

        var table = Scenes.Table;

        foreach (var scene in scenes)
        {
            var root = Scenes.Build(scene, nodes);
            var options = Scenes.Options();
            var rawOptions = Scenes.Options(optimise: false);
            var engine = new LayoutEngine(table);

            // Warm the process-wide tables and caches before measuring.
            _ = engine.Layout(root, options);

            var cold = Measure(() => new LayoutEngine(table).Layout(root, options)).Summary;
            var warm = Measure(() => engine.Layout(root, options)).Summary;
            var raw = engine.Layout(root, rawOptions);
            var viewport = new Rect(0, 0, options.ViewportWidth, options.ViewportHeight);
            var optimise = Measure(() => Optimiser.Run(raw.Instructions, raw.Constants, viewport, options));

            Console.WriteLine(
                $"{scene,-6} {Scenes.Count(root),8:N0} "
                + $"{Format(cold),24} {Format(warm),24} {Format(optimise.Summary),24} "
                + $"{raw.Instructions.Count,8:N0} -> {optimise.Value.Instructions.Count,7:N0}  "
                + $"culled {optimise.Value.Stats.Culled,6:N0} merged {optimise.Value.Stats.Merged,6:N0}");
        }
    }

    private static string Format(Measurement measurement)
    {
        return $"{measurement.Milliseconds,7:F3} ms {measurement.Kilobytes,6:F0} KiB";
    }

    private static (Measurement Summary, T Value) Measure<T>(Func<T> action)
    {
        for (var i = 0; i < Warmup; i++)
        {
            _ = action();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        T value = default!;

        for (var i = 0; i < Iterations; i++)
        {
            value = action();
        }

        stopwatch.Stop();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;

        return (
            new Measurement(
                stopwatch.Elapsed.TotalMilliseconds / Iterations,
                allocated / 1024.0 / Iterations),
            value);
    }

    private readonly record struct Measurement(double Milliseconds, double Kilobytes);
}
