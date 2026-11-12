using System.Globalization;
using System.Text;

namespace Donkeh.LayoutLab;

/// <summary>Measures a text run for layout. The default implementation is deterministic and font-file free.</summary>
/// <remarks>
/// <para>
/// All metrics are in CSS pixels and describe a single line: <c>Width</c> is
/// the advance width including per-character letter spacing, while the ascent
/// and descent are measured from the text baseline. Implementations must be
/// deterministic because <see cref="TextLayoutEngine"/> results are cached by
/// text, style and available width only.
/// </para>
/// <para>
/// The engine calls the measurer from the layout thread; implementations do not
/// need to be thread-safe for that single-threaded use, but shared instances
/// such as <see cref="SimpleTextMeasurer.Instance"/> must be stateless.
/// </para>
/// </remarks>
/// <seealso cref="SimpleTextMeasurer"/>
public interface ITextMeasurer
{
    /// <summary>Measures a span of characters in the supplied style.</summary>
    /// <param name="text">The characters to measure; may be empty.</param>
    /// <param name="style">The resolved text style supplying the font size, weight and letter spacing.</param>
    /// <returns>The single-line metrics for the run.</returns>
    TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style);

    /// <summary>Measures a string in the supplied style.</summary>
    /// <param name="text">The text to measure; may be empty or <see langword="null"/>.</param>
    /// <param name="style">The resolved text style supplying the font size, weight and letter spacing.</param>
    /// <returns>The single-line metrics for the run.</returns>
    TextMetrics Measure(string text, in TextStyle style);
}

/// <summary>Single-line text metrics: advance width plus baseline ascent and descent.</summary>
/// <param name="Width">The advance width in CSS pixels, including letter spacing.</param>
/// <param name="Ascent">The distance from the baseline to the top of the line, in CSS pixels.</param>
/// <param name="Descent">The distance from the baseline to the bottom of the line, in CSS pixels.</param>
/// <remarks>
/// <see cref="Height"/> is the metric height, not the CSS <c>line-height</c>;
/// the layout engine uses the style's line height for line boxes and only the
/// metrics for glyph extents.
/// </remarks>
public readonly record struct TextMetrics(float Width, float Ascent, float Descent)
{
    /// <summary>Zero width, ascent and descent.</summary>
    public static readonly TextMetrics Empty = new(0, 0, 0);

    /// <summary>Gets the combined ascent and descent, in CSS pixels.</summary>
    public float Height => Ascent + Descent;
}

/// <summary>
/// Fast approximate metrics. A real renderer can plug in a font-backed
/// implementation; the rest of the layout engine never assumes glyph shapes.
/// </summary>
/// <remarks>
/// <para>
/// Widths come from a small per-character table relative to the font size:
/// digits 0.56 em, uppercase 0.65 em, lowercase 0.51 em, space 0.26 em, tab
/// 1.04 em, CJK full width 1 em and other non-ASCII 0.58 em. Newlines have a
/// zero base advance.
/// </para>
/// <para>
/// Narrow punctuation and <c>i</c>/<c>l</c>/<c>I</c> use 0.28 em, and
/// <c>m</c>/<c>w</c>/<c>M</c>/<c>W</c> use 0.82 em. These exceptions are
/// checked before the generic ASCII letter ranges.
/// </para>
/// <para>
/// Each rune adds the style's letter spacing. Runs at font weight 600 or above
/// are widened by 3 percent and italic runs by 1 percent. Ascent is always
/// 0.8 em and descent 0.2 em, including for empty text. No shaping, kerning or
/// font fallback is performed.
/// </para>
/// <para>The class is stateless, so <see cref="Instance"/> is safe to share.</para>
/// </remarks>
public sealed class SimpleTextMeasurer : ITextMeasurer
{
    /// <summary>The shared, stateless instance used when <see cref="LayoutOptions.TextMeasurer"/> is not set.</summary>
    public static readonly SimpleTextMeasurer Instance = new();

    /// <summary>Measures a string by delegating to the span overload.</summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="style">The resolved text style.</param>
    /// <returns>The approximate single-line metrics.</returns>
    public TextMetrics Measure(string text, in TextStyle style) => Measure(text.AsSpan(), in style);

    /// <summary>Measures a run using the approximate per-character advance table.</summary>
    /// <param name="text">The characters to measure; empty text still produces ascent and descent.</param>
    /// <param name="style">The resolved text style supplying the font size and letter spacing.</param>
    /// <returns>The approximate single-line metrics.</returns>
    /// <remarks>
    /// Letter spacing is added once per Unicode scalar value, including for
    /// spaces. The returned width is clamped to zero.
    /// </remarks>
    public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style)
    {
        if (text.IsEmpty)
        {
            return new TextMetrics(0, style.FontSize * 0.8f, style.FontSize * 0.2f);
        }

        var width = 0f;

        foreach (var rune in text.EnumerateRunes())
        {
            width += Advance(rune, style.FontSize) + style.LetterSpacing;
        }

        if (style.FontWeight >= 600)
        {
            width *= 1.03f;
        }

        if (style.Italic)
        {
            width *= 1.01f;
        }

        return new TextMetrics(
            MathF.Max(0, width),
            style.FontSize * 0.8f,
            style.FontSize * 0.2f);
    }

    /// <summary>Returns the approximate advance width of one scalar value.</summary>
    /// <param name="rune">The scalar value to measure.</param>
    /// <param name="fontSize">The font size in CSS pixels used as the em basis.</param>
    /// <returns>The advance in CSS pixels, before letter spacing.</returns>
    private static float Advance(Rune rune, float fontSize)
    {
        var value = rune.Value;

        if (value == ' ')
        {
            return fontSize * 0.26f;
        }

        if (value == '\t')
        {
            return fontSize * 1.04f;
        }

        if (value is '\n' or '\r')
        {
            return 0;
        }

        if (value < 128)
        {
            // Narrow and wide exceptions must be checked before the generic
            // ASCII letter ranges, otherwise i/l/I and m/w/M/W never reach them.
            if (value is '.' or ',' or ':' or ';' or '!' or '|' or 'i' or 'l' or 'I' or '\'')
            {
                return fontSize * 0.28f;
            }

            if (value is 'm' or 'w' or 'M' or 'W')
            {
                return fontSize * 0.82f;
            }

            if (value is >= '0' and <= '9')
            {
                return fontSize * 0.56f;
            }

            if (value is >= 'A' and <= 'Z')
            {
                return fontSize * 0.65f;
            }

            if (value is >= 'a' and <= 'z')
            {
                return fontSize * 0.51f;
            }

            return fontSize * 0.48f;
        }

        // CJK and most full-width scripts.
        if (value is >= 0x2E80 and <= 0x9FFF or >= 0xAC00 and <= 0xD7AF or >= 0xF900 and <= 0xFAFF)
        {
            return fontSize;
        }

        return fontSize * 0.58f;
    }
}

/// <summary>
/// One positioned line produced by <see cref="TextLayoutEngine"/>.
/// </summary>
/// <remarks>
/// <see cref="X"/> and <see cref="Y"/> are relative to the text layout origin,
/// with <see cref="Y"/> at the top of the line box. <see cref="Baseline"/> is
/// relative to the same top edge, so the absolute baseline is
/// <c>origin.Y + Y + Baseline</c>.
/// </remarks>
public sealed class TextLine
{
    /// <summary>Gets or initialises the line's text after wrapping, trimming and any ellipsis.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets or initialises the horizontal offset from the layout origin, in CSS pixels.</summary>
    public float X { get; init; }

    /// <summary>Gets or initialises the first-line indent applied before alignment, in CSS pixels.</summary>
    /// <remarks>Zero for every line except the first line of an indented block.</remarks>
    public float Indent { get; init; }

    /// <summary>Gets or initialises the top offset from the layout origin, in CSS pixels.</summary>
    public float Y { get; init; }

    /// <summary>Gets or initialises the measured line width, in CSS pixels.</summary>
    public float Width { get; init; }

    /// <summary>Gets or initialises the line box height, in CSS pixels; this is the resolved line height.</summary>
    public float Height { get; init; }

    /// <summary>Gets or initialises the baseline offset from the top of the line box, in CSS pixels.</summary>
    public float Baseline { get; init; }

    /// <summary>Gets or initialises a value indicating whether this line was clipped or ellipsised.</summary>
    /// <remarks>
    /// The flag is sticky: once line clamping or an ellipsis truncates a line,
    /// every subsequent line in the same layout also reports
    /// <see langword="true"/>.
    /// </remarks>
    public bool IsTruncated { get; init; }

    /// <summary>Gets the line's rectangle in layout-local coordinates.</summary>
    public Rect Bounds => new(X, Y, Width, Height);
}

/// <summary>The wrapped, aligned result of laying out one text run.</summary>
/// <remarks>
/// <see cref="Lines"/> is never empty for a non-empty input; empty input
/// returns <see cref="Empty"/>. <see cref="Size"/> spans the widest line and
/// the stacked line heights.
/// </remarks>
public sealed class TextLayout
{
    /// <summary>Gets or initialises the full text that was laid out, after whitespace processing.</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Gets or initialises the style used for every line.</summary>
    public TextStyle Style { get; init; }

    /// <summary>Gets or initialises the positioned lines, top to bottom.</summary>
    public TextLine[] Lines { get; init; } = [];

    /// <summary>Gets or initialises the overall size in CSS pixels.</summary>
    /// <value>The widest line width and the sum of the line heights.</value>
    public Size Size { get; init; }

    /// <summary>Gets or initialises a value indicating whether any line was clamped or ellipsised.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>An empty layout with <see cref="TextStyle.Default"/> and no lines.</summary>
    public static readonly TextLayout Empty = new()
    {
        Text = string.Empty,
        Style = TextStyle.Default,
        Lines = []
    };
}

/// <summary>
/// Wraps and aligns text. It deliberately works on a single text run; inline
/// children are laid out as separate inline-level boxes, matching the engine's
/// element-only DOM.
/// </summary>
/// <remarks>
/// The engine is stateless and allocates a line list per call; callers that
/// lay out the same text repeatedly should cache the result by text, style and
/// available width, as <see cref="LayoutEngine"/> does. Measurements are
/// always taken through the supplied <see cref="ITextMeasurer"/>.
/// </remarks>
public static class TextLayoutEngine
{
    /// <summary>Lays out a single text run into wrapped, aligned lines.</summary>
    /// <param name="text">The source text. Empty input returns <see cref="TextLayout.Empty"/>.</param>
    /// <param name="style">The resolved style supplying the font, line height, alignment and direction.</param>
    /// <param name="measurer">The measurer used for every width query.</param>
    /// <param name="maxWidth">
    /// The available width in CSS pixels. A value that is zero, negative or not
    /// finite disables wrapping and makes alignment a no-op.
    /// </param>
    /// <param name="whiteSpace">Controls whitespace collapsing, preserved newlines and whether wrapping is enabled.</param>
    /// <param name="overflowWrap">Whether an overlong word may be broken between characters.</param>
    /// <param name="lineClamp">The maximum number of lines; values of zero or less mean unlimited.</param>
    /// <param name="textOverflow">Whether an overflowing line is clipped or shortened with an ellipsis.</param>
    /// <param name="firstLineIndent">
    /// Extra inset applied to the first emitted line, in CSS pixels. It reduces
    /// that line's available width so wrapping accounts for the indent.
    /// </param>
    /// <returns>The positioned lines together with their overall size and truncation state.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="WhiteSpace.Pre"/>, <see cref="WhiteSpace.PreWrap"/>,
    /// <see cref="WhiteSpace.PreLine"/> and <see cref="WhiteSpace.BreakSpaces"/>
    /// split the input into paragraphs at newlines; other modes collapse runs of
    /// whitespace to single spaces. Spaces are preserved for
    /// <see cref="WhiteSpace.Pre"/>, <see cref="WhiteSpace.PreWrap"/> and
    /// <see cref="WhiteSpace.BreakSpaces"/>; <see cref="WhiteSpace.PreLine"/>
    /// collapses whitespace within each line while still honouring newlines.
    /// Wrapping is disabled for
    /// <see cref="WhiteSpace.NoWrap"/> and <see cref="WhiteSpace.Pre"/>.
    /// </para>
    /// <para>
    /// A style line height of zero or less falls back to <c>1.2 x fontSize</c>.
    /// Each line box is exactly that tall and its baseline sits at 80 percent of
    /// the height, matching <see cref="SimpleTextMeasurer"/>.
    /// </para>
    /// <para>
    /// Ellipsis handling only applies when a line exceeds a finite
    /// <paramref name="maxWidth"/>; the text is trimmed character by character
    /// until the ellipsis fits, which is O(n) measurements in the worst case.
    /// It is applied to every emitted line, including lines produced by
    /// <see cref="WrapParagraph"/> when a single word cannot be broken.
    /// <see cref="TextAlign.Justify"/> and <see cref="TextAlign.MatchParent"/>
    /// are treated as start alignment; no inter-word distribution is performed.
    /// </para>
    /// <para>
    /// <see cref="TextTransform"/> is not applied here. The current
    /// implementation of the internal transform step is the identity, so
    /// callers are responsible for case changes.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var layout = TextLayoutEngine.Layout(
    ///     "hello world",
    ///     TextStyle.Default,
    ///     SimpleTextMeasurer.Instance,
    ///     120,
    ///     WhiteSpace.Normal,
    ///     OverflowWrap.Normal,
    ///     0,
    ///     TextOverflow.Ellipsis);
    /// </code>
    /// </example>
    public static TextLayout Layout(
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        WhiteSpace whiteSpace,
        OverflowWrap overflowWrap,
        int lineClamp,
        TextOverflow textOverflow,
        float firstLineIndent = 0)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextLayout.Empty;
        }

        text = ApplyTransform(text, style);

        var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.2f;
        var lines = new List<TextLine>(Math.Max(1, text.Length / 32));
        var truncated = false;

        var hardBreaks = whiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine
            or WhiteSpace.BreakSpaces;

        var wrap = whiteSpace is not (WhiteSpace.NoWrap or WhiteSpace.Pre);
        var maxLines = lineClamp > 0 ? lineClamp : int.MaxValue;

        var paragraphs = hardBreaks
            ? text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            : [CollapseWhitespace(text)];

        if (whiteSpace == WhiteSpace.PreLine)
        {
            for (var i = 0; i < paragraphs.Length; i++)
            {
                paragraphs[i] = CollapseWhitespace(paragraphs[i]);
            }
        }

        foreach (var paragraph in paragraphs)
        {
            if (lines.Count >= maxLines)
            {
                truncated = true;
                break;
            }

            var indent = lines.Count == 0 ? MathF.Max(0, firstLineIndent) : 0;

            if (!wrap || !float.IsFinite(maxWidth) || maxWidth <= 0)
            {
                AddLine(
                    lines,
                    paragraph,
                    style,
                    measurer,
                    maxWidth,
                    lineHeight,
                    textOverflow,
                    maxLines,
                    ref truncated,
                    indent);
                continue;
            }

            WrapParagraph(
                lines,
                paragraph,
                style,
                measurer,
                maxWidth,
                lineHeight,
                overflowWrap,
                textOverflow,
                maxLines,
                ref truncated,
                indent);
        }

        if (lines.Count == 0)
        {
            lines.Add(new TextLine
            {
                Text = string.Empty,
                Height = lineHeight,
                Baseline = lineHeight * 0.8f
            });
        }

        var width = 0f;
        var y = 0f;

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var lineWidth = line.Width;
            var indent = line.Indent;
            var available = float.IsFinite(maxWidth)
                ? MathF.Max(0, maxWidth - indent)
                : maxWidth;

            width = MathF.Max(width, indent + lineWidth);
            var x = indent + Align(lineWidth, available, style.Align, style.Direction);
            lines[i] = new TextLine
            {
                Text = line.Text,
                X = x,
                Y = y,
                Width = lineWidth,
                Indent = indent,
                Height = lineHeight,
                Baseline = line.Baseline,
                IsTruncated = line.IsTruncated
            };
            y += lineHeight;
        }

        return new TextLayout
        {
            Text = text,
            Style = style,
            Lines = lines.ToArray(),
            Size = new Size(width, y),
            IsTruncated = truncated
        };
    }

    /// <summary>
    /// Band-aware wrapping used when text flows around floats. The callback
    /// returns the available horizontal band for a given line's top edge; the
    /// returned line <c>X</c> values are relative to <paramref name="originX"/>.
    /// </summary>
    /// <param name="text">The source text. Empty input returns <see cref="TextLayout.Empty"/>.</param>
    /// <param name="style">The resolved style supplying the font, line height, alignment and direction.</param>
    /// <param name="measurer">The measurer used for every width query.</param>
    /// <param name="originX">The absolute x coordinate that the band's left edge is converted against.</param>
    /// <param name="startY">The absolute y coordinate of the first line's top edge.</param>
    /// <param name="whiteSpace">Controls whitespace collapsing, preserved newlines and whether wrapping is enabled.</param>
    /// <param name="overflowWrap">Whether an overlong word may be broken between characters.</param>
    /// <param name="lineClamp">The maximum number of lines; values of zero or less mean unlimited.</param>
    /// <param name="textOverflow">Whether an overflowing line is clipped or shortened with an ellipsis.</param>
    /// <param name="bandAt">
    /// Returns the absolute left and right extents available at a given absolute
    /// y coordinate. It is called at least once per emitted line.
    /// </param>
    /// <returns>The positioned lines with <see cref="TextLine.Y"/> values relative to <paramref name="startY"/>.</returns>
    /// <remarks>
    /// <para>
    /// The band may change from line to line, so a word is re-measured against
    /// the band for the line it would start on. Unlike <see cref="Layout"/>,
    /// non-preserved paragraphs are trimmed before wrapping and empty
    /// paragraphs still emit a blank line.
    /// </para>
    /// <para>
    /// Each <see cref="TextLine.X"/> is <c>band.Left - originX</c> plus the
    /// alignment offset within the band; adding <paramref name="originX"/> back
    /// yields absolute coordinates. The result is not cached by the engine.
    /// </para>
    /// </remarks>
    public static TextLayout LayoutWithBands(
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float originX,
        float startY,
        WhiteSpace whiteSpace,
        OverflowWrap overflowWrap,
        int lineClamp,
        TextOverflow textOverflow,
        Func<float, (float Left, float Right)> bandAt)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TextLayout.Empty;
        }

        var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.2f;
        var lines = new List<TextLine>(Math.Max(1, text.Length / 32));
        var maxLines = lineClamp > 0 ? lineClamp : int.MaxValue;
        var truncated = false;

        var hardBreaks = whiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.PreLine
            or WhiteSpace.BreakSpaces;
        var preserveSpaces = whiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.BreakSpaces;
        var wrap = whiteSpace is not (WhiteSpace.NoWrap or WhiteSpace.Pre);

        var paragraphs = hardBreaks
            ? text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            : [CollapseWhitespace(text)];

        if (whiteSpace == WhiteSpace.PreLine)
        {
            for (var i = 0; i < paragraphs.Length; i++)
            {
                paragraphs[i] = CollapseWhitespace(paragraphs[i]);
            }
        }

        foreach (var rawParagraph in paragraphs)
        {
            if (lines.Count >= maxLines)
            {
                truncated = true;
                break;
            }

            var paragraph = preserveSpaces ? rawParagraph : rawParagraph.Trim();

            if (paragraph.Length == 0)
            {
                AddBandLine(lines, string.Empty, style, measurer, originX, startY, lineHeight,
                    bandAt, textOverflow, maxLines, ref truncated);
                continue;
            }

            if (!wrap)
            {
                AddBandLine(lines, paragraph, style, measurer, originX, startY, lineHeight,
                    bandAt, textOverflow, maxLines, ref truncated);
                continue;
            }

            var words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var current = new StringBuilder();

            foreach (var word in words)
            {
                if (lines.Count >= maxLines)
                {
                    truncated = true;
                    break;
                }

                var candidate = current.Length == 0 ? word : current + " " + word;
                var y = startY + lines.Count * lineHeight;
                var band = bandAt(y);
                var candidateWidth = measurer.Measure(candidate, in style).Width;

                if (current.Length > 0 && candidateWidth > MathF.Max(0, band.Right - band.Left))
                {
                    AddBandLine(lines, current.ToString(), style, measurer, originX, startY,
                        lineHeight, bandAt, textOverflow, maxLines, ref truncated);
                    current.Clear();
                    current.Append(word);
                }
                else
                {
                    current.Clear();
                    current.Append(candidate);
                }
            }

            if (current.Length > 0 && lines.Count < maxLines)
            {
                AddBandLine(lines, current.ToString(), style, measurer, originX, startY,
                    lineHeight, bandAt, textOverflow, maxLines, ref truncated);
            }
        }

        if (lines.Count == 0)
        {
            lines.Add(new TextLine
            {
                Text = string.Empty,
                Height = lineHeight,
                Baseline = lineHeight * 0.8f
            });
        }

        var width = 0f;
        var height = 0f;

        for (var i = 0; i < lines.Count; i++)
        {
            width = MathF.Max(width, lines[i].Width);
            height = MathF.Max(height, lines[i].Y + lines[i].Height);
        }

        return new TextLayout
        {
            Text = text,
            Style = style,
            Lines = lines.ToArray(),
            Size = new Size(width, height),
            IsTruncated = truncated
        };
    }

    /// <summary>Measures, ellipsises and aligns one line inside the band at its y coordinate.</summary>
    /// <param name="lines">The destination line list; the line's index determines its y offset.</param>
    /// <param name="text">The candidate line text.</param>
    /// <param name="style">The text style.</param>
    /// <param name="measurer">The measurer used for widths.</param>
    /// <param name="originX">The absolute x coordinate subtracted from the band's left edge.</param>
    /// <param name="startY">The absolute y coordinate of the first line.</param>
    /// <param name="lineHeight">The resolved line box height.</param>
    /// <param name="bandAt">Returns the absolute band for a y coordinate.</param>
    /// <param name="overflow">The overflow mode; <see cref="TextOverflow.Ellipsis"/> trims to the band.</param>
    /// <param name="maxLines">The line clamp, or <see cref="int.MaxValue"/> for unlimited.</param>
    /// <param name="truncated">Set when the clamp or ellipsis truncated content.</param>
    private static void AddBandLine(
        List<TextLine> lines,
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float originX,
        float startY,
        float lineHeight,
        Func<float, (float Left, float Right)> bandAt,
        TextOverflow overflow,
        int maxLines,
        ref bool truncated)
    {
        if (lines.Count >= maxLines)
        {
            truncated = true;
            return;
        }

        var y = startY + lines.Count * lineHeight;
        var band = bandAt(y);
        var bandWidth = MathF.Max(0, band.Right - band.Left);
        var width = measurer.Measure(text, in style).Width;

        if (overflow == TextOverflow.Ellipsis && width > bandWidth)
        {
            var ellipsis = "\u2026";
            var ellipsisWidth = measurer.Measure(ellipsis, in style).Width;
            var target = MathF.Max(0, bandWidth - ellipsisWidth);
            var end = text.Length;

            while (end > 0 && measurer.Measure(text.AsSpan(0, end), in style).Width > target)
            {
                end--;
            }

            text = text[..end].TrimEnd() + ellipsis;
            width = measurer.Measure(text, in style).Width;
            truncated = true;
        }

        var align = Align(width, bandWidth, style.Align, style.Direction);
        var x = band.Left - originX + align;

        lines.Add(new TextLine
        {
            Text = text,
            X = x,
            Y = lines.Count * lineHeight,
            Width = width,
            Height = lineHeight,
            Baseline = lineHeight * 0.8f,
            IsTruncated = truncated
        });
    }

    /// <summary>Greedily wraps one paragraph into lines no wider than <paramref name="maxWidth"/>.</summary>
    /// <param name="lines">The destination line list.</param>
    /// <param name="paragraph">The paragraph text with whitespace already collapsed or preserved.</param>
    /// <param name="style">The text style.</param>
    /// <param name="measurer">The measurer used for candidate widths.</param>
    /// <param name="maxWidth">The available width in CSS pixels.</param>
    /// <param name="lineHeight">The resolved line box height.</param>
    /// <param name="overflowWrap">Whether mid-word breaks are allowed.</param>
    /// <param name="textOverflow">Whether an overlong line is clipped or shortened with an ellipsis.</param>
    /// <param name="maxLines">The line clamp, or <see cref="int.MaxValue"/> for unlimited.</param>
    /// <param name="truncated">Set when the clamp stops wrapping early.</param>
    /// <param name="firstLineIndent">Indent applied to the first emitted line only.</param>
    /// <remarks>
    /// Break opportunities are spaces, tabs, hyphens, slashes, the soft hyphen
    /// and the end of the text. When a candidate is too wide the last good break
    /// is used; otherwise the word is split at the current character for
    /// <see cref="OverflowWrap.Anywhere"/> or <see cref="OverflowWrap.BreakWord"/>.
    /// If no break opportunity fits, a single glyph is emitted on its own line.
    /// Spaces and tabs at the start of a continuation line are always skipped.
    /// </remarks>
    private static void WrapParagraph(
        List<TextLine> lines,
        string paragraph,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        OverflowWrap overflowWrap,
        TextOverflow textOverflow,
        int maxLines,
        ref bool truncated,
        float firstLineIndent = 0)
    {
        var start = 0;
        var lastBreak = -1;
        var lineIndent = MathF.Max(0, firstLineIndent);

        for (var i = 0; i <= paragraph.Length; i++)
        {
            if (lines.Count >= maxLines)
            {
                truncated = true;
                return;
            }

            // Every character has already been emitted (for example by the
            // single-glyph fallback); don't append a trailing empty line.
            if (paragraph.Length > 0 && start >= paragraph.Length)
            {
                return;
            }

            var atEnd = i == paragraph.Length;
            var c = atEnd ? '\0' : paragraph[i];

            var breakable = atEnd || c == ' ' || c == '\t' || c == '-' || c == '/' || c == '\u00AD'
                || (overflowWrap is OverflowWrap.Anywhere or OverflowWrap.BreakWord);

            if (!breakable)
            {
                continue;
            }

            var candidateEnd = atEnd ? paragraph.Length : i + 1;
            var measureEnd = candidateEnd;

            // A trailing space is a break opportunity, not content. Excluding it
            // from the fit test lets "aa bb" wrap at the space even when
            // "aa " is wider than the line but "aa" fits.
            if (!atEnd && c is ' ' or '\t')
            {
                while (measureEnd > start && paragraph[measureEnd - 1] is ' ' or '\t')
                {
                    measureEnd--;
                }
            }

            var width = measurer.Measure(
                paragraph.AsSpan(start, Math.Max(0, measureEnd - start)),
                in style).Width;

            if (width <= MathF.Max(0, maxWidth - lineIndent))
            {
                lastBreak = candidateEnd;

                if (atEnd)
                {
                    EmitLine(
                        lines,
                        paragraph[start..candidateEnd].TrimEnd(),
                        style,
                        measurer,
                        maxWidth,
                        lineHeight,
                        textOverflow,
                        maxLines,
                        ref truncated,
                        lineIndent);
                    return;
                }

                continue;
            }

            // The candidate is too wide. Break at the last good opportunity, or
            // break the word itself when overflow-wrap allows it.
            if (lastBreak > start)
            {
                EmitLine(
                    lines,
                    paragraph[start..lastBreak].TrimEnd(),
                    style,
                    measurer,
                    maxWidth,
                    lineHeight,
                    textOverflow,
                    maxLines,
                    ref truncated,
                    lineIndent);

                lineIndent = 0;
                start = lastBreak;

                while (start < paragraph.Length && paragraph[start] is ' ' or '\t')
                {
                    start++;
                }

                lastBreak = -1;
                i = start - 1;
                continue;
            }

            // The parentheses are required: `&&` binds tighter than `is ... or`,
            // and without them Anywhere would also match when i == start, which
            // emits an empty line and never advances.
            if ((overflowWrap is OverflowWrap.Anywhere or OverflowWrap.BreakWord) && i > start)
            {
                EmitLine(
                    lines,
                    paragraph[start..i],
                    style,
                    measurer,
                    maxWidth,
                    lineHeight,
                    textOverflow,
                    maxLines,
                    ref truncated,
                    lineIndent);

                lineIndent = 0;
                start = i;
                lastBreak = -1;
                i = start - 1;
                continue;
            }

            // A single glyph wider than the container: emit it anyway, then move on.
            if (i == start)
            {
                var end = Math.Min(paragraph.Length, start + 1);

                while (end < paragraph.Length)
                {
                    var test = paragraph[start..end];

                    if (measurer.Measure(test, in style).Width > MathF.Max(0, maxWidth - lineIndent))
                    {
                        break;
                    }

                    end++;
                }

                EmitLine(
                    lines,
                    paragraph[start..end],
                    style,
                    measurer,
                    maxWidth,
                    lineHeight,
                    textOverflow,
                    maxLines,
                    ref truncated,
                    lineIndent);

                lineIndent = 0;
                start = end;
                lastBreak = -1;
                i = start - 1;
            }
        }

        if (start < paragraph.Length && lines.Count < maxLines)
        {
            EmitLine(
                lines,
                paragraph[start..].TrimEnd(),
                style,
                measurer,
                maxWidth,
                lineHeight,
                textOverflow,
                maxLines,
                ref truncated,
                lineIndent);
        }
    }

    /// <summary>Appends one unwrapped line, respecting the line clamp.</summary>
    /// <param name="lines">The destination line list.</param>
    /// <param name="text">The line text.</param>
    /// <param name="style">The text style.</param>
    /// <param name="measurer">The measurer used for widths.</param>
    /// <param name="maxWidth">The width used for clipping and ellipsis; may be non-finite.</param>
    /// <param name="lineHeight">The resolved line box height.</param>
    /// <param name="overflow">The overflow mode.</param>
    /// <param name="maxLines">The line clamp, or <see cref="int.MaxValue"/> for unlimited.</param>
    /// <param name="truncated">Set when the clamp stops emission.</param>
    /// <param name="indent">Indent applied before alignment, in CSS pixels.</param>
    private static void AddLine(
        List<TextLine> lines,
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        TextOverflow overflow,
        int maxLines,
        ref bool truncated,
        float indent = 0)
    {
        if (lines.Count >= maxLines)
        {
            truncated = true;
            return;
        }

        EmitLine(
            lines,
            text,
            style,
            measurer,
            maxWidth,
            lineHeight,
            overflow,
            maxLines,
            ref truncated,
            indent);
    }

    /// <summary>Creates and appends a <see cref="TextLine"/> with no horizontal placement.</summary>
    /// <param name="lines">The destination line list.</param>
    /// <param name="text">The line text.</param>
    /// <param name="style">The text style.</param>
    /// <param name="measurer">The measurer used for widths.</param>
    /// <param name="maxWidth">The clipping width; ellipsis is skipped when it is not finite.</param>
    /// <param name="lineHeight">The resolved line box height; the baseline is set to 80 percent of it.</param>
    /// <param name="overflow">The overflow mode.</param>
    /// <param name="maxLines">The line clamp, or <see cref="int.MaxValue"/> for unlimited.</param>
    /// <param name="truncated">Set when the clamp or ellipsis truncates content.</param>
    /// <param name="indent">Indent applied before alignment, in CSS pixels.</param>
    /// <remarks>
    /// <see cref="TextLine.X"/> and <see cref="TextLine.Y"/> stay at zero here;
    /// the caller assigns them once the full line count is known. Ellipsis
    /// trimming removes trailing whitespace before appending U+2026.
    /// </remarks>
    private static void EmitLine(
        List<TextLine> lines,
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        TextOverflow overflow,
        int maxLines,
        ref bool truncated,
        float indent = 0)
    {
        if (lines.Count >= maxLines)
        {
            truncated = true;
            return;
        }

        var available = float.IsFinite(maxWidth)
            ? MathF.Max(0, maxWidth - indent)
            : maxWidth;
        var width = measurer.Measure(text, in style).Width;

        if (overflow == TextOverflow.Ellipsis && float.IsFinite(available) && width > available)
        {
            var ellipsis = "\u2026";
            var ellipsisWidth = measurer.Measure(ellipsis, in style).Width;
            var target = MathF.Max(0, available - ellipsisWidth);
            var end = text.Length;

            while (end > 0 && measurer.Measure(text.AsSpan(0, end), in style).Width > target)
            {
                end--;
            }

            text = text[..end].TrimEnd() + ellipsis;
            width = measurer.Measure(text, in style).Width;
            truncated = true;
        }

        lines.Add(new TextLine
        {
            Text = text,
            Width = width,
            Indent = indent,
            Height = lineHeight,
            Baseline = lineHeight * 0.8f,
            IsTruncated = truncated
        });
    }

    /// <summary>Maps an alignment mode and direction to a horizontal offset inside the available width.</summary>
    /// <param name="lineWidth">The measured line width in CSS pixels.</param>
    /// <param name="maxWidth">The available width; non-finite values produce a zero offset.</param>
    /// <param name="align">The requested alignment.</param>
    /// <param name="direction">The inline direction, which swaps the meaning of start and end.</param>
    /// <returns>The offset from the line's start edge, never negative.</returns>
    /// <remarks>
    /// <see cref="TextAlign.Left"/> and <see cref="TextAlign.Start"/> share a
    /// branch, as do <see cref="TextAlign.Right"/> and <see cref="TextAlign.End"/>;
    /// <see cref="TextAlign.Justify"/> falls through to start alignment.
    /// </remarks>
    private static float Align(float lineWidth, float maxWidth, TextAlign align, Direction direction)
    {
        if (!float.IsFinite(maxWidth))
        {
            return 0;
        }

        var remaining = MathF.Max(0, maxWidth - lineWidth);

        return align switch
        {
            TextAlign.Center => remaining * 0.5f,
            TextAlign.Right => remaining,
            TextAlign.End => direction == Direction.RightToLeft ? 0 : remaining,
            _ => direction == Direction.RightToLeft ? remaining : 0
        };
    }

    /// <summary>Text transformation hook; currently returns the text unchanged.</summary>
    /// <param name="text">The text to transform.</param>
    /// <param name="style">The style that would supply the transform, if it were carried.</param>
    /// <returns>The unmodified input.</returns>
    /// <remarks>
    /// <see cref="TextStyle"/> deliberately does not carry
    /// <see cref="TextTransform"/>; callers apply case changes before building
    /// the style so the text engine stays independent of CSS plumbing.
    /// </remarks>
    private static string ApplyTransform(string text, in TextStyle style)
    {
        // Text transform lives in ComputedStyle; TextStyle does not carry it.
        // Callers apply it before creating the style, keeping the text engine
        // independent of CSS property plumbing.
        return text;
    }

    /// <summary>Collapses every run of whitespace to one space and trims both ends.</summary>
    /// <param name="text">The text to normalise.</param>
    /// <returns>The collapsed text.</returns>
    /// <remarks>
    /// Space, tab, line feed, carriage return and form feed count as
    /// whitespace. The function allocates a <see cref="StringBuilder"/> sized to
    /// the input and is skipped entirely for whitespace-preserving modes.
    /// </remarks>
    private static string CollapseWhitespace(string text)
    {
        var builder = new StringBuilder(text.Length);
        var pendingSpace = false;

        foreach (var c in text)
        {
            if (c is ' ' or '\t' or '\n' or '\r' or '\f')
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }
}
