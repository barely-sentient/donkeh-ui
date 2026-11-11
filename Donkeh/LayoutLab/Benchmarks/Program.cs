using BenchmarkDotNet.Running;
using Donkeh.LayoutLab.Benchmarks;

if (args.Length > 0 && args[0] == "--profile")
{
    Profile.Run(args);
    return;
}

BenchmarkSwitcher
    .FromAssembly(typeof(LayoutBenchmarks).Assembly)
    .Run(args);
