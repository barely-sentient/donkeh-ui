using BenchmarkDotNet.Running;
using Ignite.Data.Benchmarks;

if (args.Length > 0 && args[0] == "--smoke")
{
    var sections = args.Length > 1 ? int.Parse(args[1]) : 64;
    Smoke.Run(sections);
    return;
}

BenchmarkSwitcher
    .FromAssembly(typeof(ParseBootstrapBenchmarks).Assembly)
    .Run(args);
