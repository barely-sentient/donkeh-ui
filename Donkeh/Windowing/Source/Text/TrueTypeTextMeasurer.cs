using Donkeh.LayoutLab;

namespace Donkeh.Windowing;

/// <summary>Bridges the layout engine to <see cref="FontManager"/> metrics.</summary>
/// <remarks>
/// <para>
/// The layout engine only knows <see cref="ITextMeasurer"/>, so this adapter
/// forwards every measurement to a shared <see cref="FontManager"/>. The same
/// manager is used by the renderer, which guarantees that wrapped line widths
/// and painted advances agree exactly.
/// </para>
/// <para>
/// Measurement is deterministic only while the font set is unchanged: file
/// discovery and metric caching happen on first use, and the manager's caches are
/// mutable. Instances are therefore not thread-safe and must be used from the
/// layout/frame thread that owns the manager.
/// </para>
/// </remarks>
/// <seealso cref="ITextMeasurer"/>
/// <seealso cref="SimpleTextMeasurer"/>
public sealed class TrueTypeTextMeasurer : ITextMeasurer
{
    /// <summary>The font pipeline that supplies all metrics.</summary>
    private readonly FontManager _fonts;

    /// <summary>
    /// Creates an adapter over an existing font manager.
    /// </summary>
    /// <param name="fonts">
    /// The manager to measure with. It must outlive this measurer and be used on a
    /// single thread; no ownership is taken.
    /// </param>
    /// <remarks>
    /// The reference is stored without copying or wrapping, so fonts loaded later
    /// through the same manager are visible immediately.
    /// </remarks>
    public TrueTypeTextMeasurer(FontManager fonts)
    {
        _fonts = fonts;
    }

    /// <summary>Measures a span using real font metrics.</summary>
    /// <param name="text">The characters to measure; may be empty.</param>
    /// <param name="style">The resolved text style.</param>
    /// <returns>The single-line metrics in CSS pixels.</returns>
    /// <remarks>
    /// The result includes font fallback per rune, kerning between adjacent runes
    /// on the same face and the style's letter spacing. See
    /// <see cref="FontManager.Measure(ReadOnlySpan{char}, in TextStyle)"/> for the
    /// exact rules. After the face set and metric bucket are cached, the span path
    /// performs no allocations.
    /// </remarks>
    public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style) => _fonts.Measure(text, in style);

    /// <summary>Measures a string using real font metrics.</summary>
    /// <param name="text">The text to measure; <see langword="null"/> is treated as empty.</param>
    /// <param name="style">The resolved text style.</param>
    /// <returns>The single-line metrics in CSS pixels.</returns>
    /// <remarks>
    /// A convenience overload; the string is measured through the same span path,
    /// so fallback, kerning and letter spacing behave identically.
    /// </remarks>
    public TextMetrics Measure(string text, in TextStyle style) => _fonts.Measure(text, in style);
}
