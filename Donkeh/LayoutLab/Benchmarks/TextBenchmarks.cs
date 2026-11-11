using System.Text;
using BenchmarkDotNet.Attributes;
using Donkeh.Data;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>
/// Text measurement, whitespace collapsing and line breaking. The generated
/// text contains newlines and long words so every wrapping mode has work to do.
/// </summary>
[Config(typeof(LayoutLabBenchmarkConfig))]
[MemoryDiagnoser(displayGenColumns: true)]
[SimpleJob(warmupCount: 4, iterationCount: 8)]
public class TextBenchmarks
{
    [Params(256, 2048, 16384)]
    public int Chars;

    [Params(320f, 960f)]
    public float MaxWidth;

    private string _text = string.Empty;
    private string _cjk = string.Empty;
    private TextStyle _style;

    [GlobalSetup]
    public void Setup()
    {
        _text = CreateText(Chars);
        _cjk = CreateCjk(Math.Max(64, Chars / 4));
        _style = TextStyle.Default with { FontSize = 16, LineHeight = 20 };
    }

    [Benchmark(Baseline = true)]
    public int Wrap()
    {
        return Layout(WhiteSpace.Normal, OverflowWrap.Normal).Lines.Length;
    }

    [Benchmark]
    public int WrapBreakWord()
    {
        return Layout(WhiteSpace.Normal, OverflowWrap.BreakWord).Lines.Length;
    }

    [Benchmark]
    public int NoWrap()
    {
        return Layout(WhiteSpace.NoWrap, OverflowWrap.Normal).Lines.Length;
    }

    [Benchmark]
    public int PreWrap()
    {
        return Layout(WhiteSpace.PreWrap, OverflowWrap.Normal).Lines.Length;
    }

    [Benchmark]
    public int Ellipsis()
    {
        return Layout(WhiteSpace.NoWrap, OverflowWrap.Normal, TextOverflow.Ellipsis).Lines.Length;
    }

    [Benchmark]
    public int Measure()
    {
        return (int)SimpleTextMeasurer.Instance.Measure(_text, in _style).Width;
    }

    [Benchmark]
    public int MeasureCjk()
    {
        return (int)SimpleTextMeasurer.Instance.Measure(_cjk, in _style).Width;
    }

    private TextLayout Layout(
        WhiteSpace whiteSpace,
        OverflowWrap overflowWrap,
        TextOverflow overflow = TextOverflow.Clip)
    {
        return TextLayoutEngine.Layout(
            _text,
            in _style,
            SimpleTextMeasurer.Instance,
            MaxWidth,
            whiteSpace,
            overflowWrap,
            lineClamp: 0,
            textOverflow: overflow);
    }

    private static string CreateText(int length)
    {
        var words = new[]
        {
            "layout", "engine", "wraps", "text", "across", "several", "lines",
            "while", "measuring", "each", "word", "and", "space", "quickly",
            "benchmark", "allocation", "instructions", "pixels", "baseline"
        };

        var builder = new StringBuilder(length + 32);
        var index = 0;

        while (builder.Length < length)
        {
            builder.Append(words[index++ % words.Length]);

            if (index % 13 == 0)
            {
                builder.Append('\n');
            }
            else if (index % 17 == 0)
            {
                builder.Append(" supercalifragilisticexpialidocious ");
            }
            else
            {
                builder.Append(' ');
            }
        }

        return builder.ToString(0, length);
    }

    private static string CreateCjk(int length)
    {
        const string phrase = "布局引擎文本测量换行性能";
        var builder = new StringBuilder(length + phrase.Length);

        while (builder.Length < length)
        {
            builder.Append(phrase);
        }

        return builder.ToString(0, length);
    }
}
