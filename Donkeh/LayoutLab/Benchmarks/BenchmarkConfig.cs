using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>
/// Shared BenchmarkDotNet configuration. The individual benchmark classes add
/// <c>MemoryDiagnoser</c> (with Gen0/1/2 columns) and a short job.
/// </summary>
internal sealed class LayoutLabBenchmarkConfig : ManualConfig
{
    public LayoutLabBenchmarkConfig()
    {
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        AddColumn(StatisticColumn.P95);
    }
}
