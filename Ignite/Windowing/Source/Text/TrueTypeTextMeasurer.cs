using Ignite.LayoutLab;

namespace Ignite.Windowing;

/// <summary>Bridges the layout engine to <see cref="FontManager"/> metrics.</summary>
public sealed class TrueTypeTextMeasurer : ITextMeasurer
{
    private readonly FontManager _fonts;

    public TrueTypeTextMeasurer(FontManager fonts)
    {
        _fonts = fonts;
    }

    public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style) => _fonts.Measure(text, in style);

    public TextMetrics Measure(string text, in TextStyle style) => _fonts.Measure(text, in style);
}
