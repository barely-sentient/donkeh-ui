using System.Globalization;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Donkeh.DataEngine.Benchmarks;

internal sealed class DataEngineBenchmarkConfig : ManualConfig
{
    public DataEngineBenchmarkConfig()
    {
        AddColumnProvider(DefaultColumnProviders.Instance);
        AddLogger(ConsoleLogger.Default);
        AddColumn(new RateColumn(RateKind.Document));
        AddColumn(new RateColumn(RateKind.Tokens));
        AddColumn(new RateColumn(RateKind.Rules));
        AddColumn(new AllocationColumn());
        AddColumn(new GcCollectionsColumn());
    }
}

internal enum RateKind
{
    Document,
    Tokens,
    Rules
}

/// <summary>
/// Derived throughput based on the exact corpus shape. The shape is computed
/// from small samples, so the reporting process never materialises the full
/// multi-megabyte document.
/// </summary>
internal sealed class RateColumn : IColumn
{
    private readonly RateKind _kind;

    public RateColumn(RateKind kind)
    {
        _kind = kind;
    }

    public string Id => _kind switch
    {
        RateKind.Document => "CSS MB/s",
        RateKind.Tokens => "M tok/s",
        RateKind.Rules => "K rules/s",
        _ => throw new ArgumentOutOfRangeException()
    };

    public string ColumnName => Id;

    public bool AlwaysShow => true;

    public ColumnCategory Category => ColumnCategory.Custom;

    public int PriorityInCategory => _kind switch
    {
        RateKind.Document => 0,
        RateKind.Tokens => 1,
        RateKind.Rules => 2,
        _ => 3
    };

    public bool IsNumeric => true;

    public UnitType UnitType => UnitType.Dimensionless;

    public string Legend => _kind switch
    {
        RateKind.Document => "CSS input megabytes processed per second",
        RateKind.Tokens => "Millions of tokens processed per second",
        RateKind.Rules => "Thousands of table rules produced per second",
        _ => string.Empty
    };

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        if (!Applies(benchmarkCase))
        {
            return "-";
        }

        var report = summary[benchmarkCase];
        if (report?.ResultStatistics is null)
        {
            return "?";
        }

        var ns = report.ResultStatistics.Mean;
        if (ns <= 0 || !TryGetCorpus(benchmarkCase, out var size, out var repeated))
        {
            return "?";
        }

        var shape = CssCorpus.GetShape(size, repeated);

        var value = _kind switch
        {
            RateKind.Document => shape.SourceBytes / ns * 1e3,
            RateKind.Tokens => shape.Tokens / ns * 1e3,
            RateKind.Rules => shape.Rules / ns * 1e6,
            _ => 0.0
        };

        var format = _kind == RateKind.Document ? "N1" : "N2";
        return value.ToString(format, CultureInfo.InvariantCulture);
    }

    private bool Applies(BenchmarkCase benchmarkCase)
    {
        var type = benchmarkCase.Descriptor.Type;
        if (type != typeof(ParseAndBuildBenchmarks)
            && type != typeof(RepeatedSelectorBenchmarks))
        {
            return false;
        }

        var method = benchmarkCase.Descriptor.WorkloadMethod.Name;

        return _kind switch
        {
            // The tokenizer pass always covers the whole document.
            RateKind.Document => true,

            // Build_FromTokens starts from an already tokenized list.
            RateKind.Tokens => method != "Build_FromTokens",

            // Tokenize does not produce table rules.
            RateKind.Rules => method != "Tokenize",

            _ => false
        };
    }

    private static bool TryGetCorpus(
        BenchmarkCase benchmarkCase,
        out int size,
        out bool repeated)
    {
        repeated = benchmarkCase.Descriptor.Type == typeof(RepeatedSelectorBenchmarks);

        foreach (var parameter in benchmarkCase.Parameters.Items)
        {
            if (parameter.Name == "Size" && parameter.Value is not null)
            {
                size = Convert.ToInt32(parameter.Value, CultureInfo.InvariantCulture);
                return true;
            }
        }

        size = 0;
        return false;
    }
}

/// <summary>
/// Shows allocation explicitly, including zero. BenchmarkDotNet's built-in
/// Allocated column renders zero as "-", which looks like missing data.
/// </summary>
internal sealed class AllocationColumn : IColumn
{
    public string Id => "Alloc/op";
    public string ColumnName => Id;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 1;
    public bool IsNumeric => true;
    public UnitType UnitType => UnitType.Size;
    public string Legend => "Managed bytes allocated per operation";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report is null)
        {
            return "?";
        }

        var bytes = report.GcStats.GetBytesAllocatedPerOperation(benchmarkCase);
        if (bytes is null)
        {
            return "?";
        }

        if (bytes.Value == 0)
        {
            return "0 B";
        }

        return bytes.Value < 1024
            ? $"{bytes.Value} B"
            : $"{bytes.Value / 1024.0:N1} KB";
    }
}

/// <summary>
/// Gen0/Gen1/Gen2 collections per 1000 operations, shown even when zero.
/// </summary>
internal sealed class GcCollectionsColumn : IColumn
{
    public string Id => "GC/1k ops";
    public string ColumnName => Id;
    public bool AlwaysShow => true;
    public ColumnCategory Category => ColumnCategory.Custom;
    public int PriorityInCategory => 2;
    public bool IsNumeric => false;
    public UnitType UnitType => UnitType.Dimensionless;
    public string Legend => "Gen0/Gen1/Gen2 garbage collections per 1000 operations";

    public bool IsAvailable(Summary summary) => true;

    public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
    {
        return GetValue(summary, benchmarkCase, SummaryStyle.Default);
    }

    public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style)
    {
        var report = summary[benchmarkCase];
        if (report is null)
        {
            return "?";
        }

        var gc = report.GcStats;
        var scale = gc.TotalOperations > 0 ? 1000.0 / gc.TotalOperations : 1.0;
        return $"{gc.Gen0Collections * scale:N0}/{gc.Gen1Collections * scale:N0}/{gc.Gen2Collections * scale:N0}";
    }
}
