using BenchmarkDotNet.Running;
using Donkeh.DataEngine.Benchmarks;

if (args.Length > 0 && args[0] == "--profile")
{
    Profile.Run(args);
    return;
}

BenchmarkSwitcher
    .FromAssembly(typeof(ParseAndBuildBenchmarks).Assembly)
    .Run(args);
