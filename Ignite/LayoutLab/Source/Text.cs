using System.Globalization;
using System.Text;

namespace Ignite.LayoutLab;

/// <summary>Measures a text run for layout. The default implementation is deterministic and font-file free.</summary>
public interface ITextMeasurer
{
    TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style);

    TextMetrics Measure(string text, in TextStyle style);
}

public readonly record struct TextMetrics(float Width, float Ascent, float Descent)
{
    public static readonly TextMetrics Empty = new(0, 0, 0);

    public float Height => Ascent + Descent;
}

/// <summary>
/// Fast approximate metrics. A real renderer can plug in a font-backed
/// implementation; the rest of the layout engine never assumes glyph shapes.
/// </summary>
public sealed class SimpleTextMeasurer : ITextMeasurer
{
    public static readonly SimpleTextMeasurer Instance = new();

    public TextMetrics Measure(string text, in TextStyle style) => Measure(text.AsSpan(), in style);

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

            if (value is '.' or ',' or ':' or ';' or '!' or '|' or 'i' or 'l' or 'I' or '\'')
            {
                return fontSize * 0.28f;
            }

            if (value is 'm' or 'w' or 'M' or 'W')
            {
                return fontSize * 0.82f;
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

public sealed class TextLine
{
    public string Text { get; init; } = string.Empty;
    public float X { get; init; }
    public float Y { get; init; }
    public float Width { get; init; }
    public float Height { get; init; }
    public float Baseline { get; init; }
    public bool IsTruncated { get; init; }

    public Rect Bounds => new(X, Y, Width, Height);
}

/// <summary>The wrapped, aligned result of laying out one text run.</summary>
public sealed class TextLayout
{
    public string Text { get; init; } = string.Empty;
    public TextStyle Style { get; init; }
    public TextLine[] Lines { get; init; } = [];
    public Size Size { get; init; }
    public bool IsTruncated { get; init; }

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
public static class TextLayoutEngine
{
    public static TextLayout Layout(
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        WhiteSpace whiteSpace,
        OverflowWrap overflowWrap,
        int lineClamp,
        TextOverflow textOverflow)
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

        var preserveSpaces = whiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.BreakSpaces;
        var wrap = whiteSpace is not (WhiteSpace.NoWrap or WhiteSpace.Pre);
        var maxLines = lineClamp > 0 ? lineClamp : int.MaxValue;

        var paragraphs = hardBreaks
            ? text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
            : [CollapseWhitespace(text)];

        foreach (var paragraph in paragraphs)
        {
            if (lines.Count >= maxLines)
            {
                truncated = true;
                break;
            }

            if (!wrap || !float.IsFinite(maxWidth) || maxWidth <= 0)
            {
                AddLine(lines, paragraph, style, measurer, maxWidth, lineHeight, textOverflow, maxLines, ref truncated);
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
                preserveSpaces,
                maxLines,
                ref truncated);
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
            width = MathF.Max(width, lineWidth);
            var x = Align(lineWidth, maxWidth, style.Align, style.Direction);
            lines[i] = new TextLine
            {
                Text = line.Text,
                X = x,
                Y = y,
                Width = lineWidth,
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

    private static void WrapParagraph(
        List<TextLine> lines,
        string paragraph,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        OverflowWrap overflowWrap,
        bool preserveSpaces,
        int maxLines,
        ref bool truncated)
    {
        var start = 0;
        var lastBreak = -1;

        for (var i = 0; i <= paragraph.Length; i++)
        {
            if (lines.Count >= maxLines)
            {
                truncated = true;
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
            var candidate = paragraph[start..candidateEnd];
            var width = measurer.Measure(candidate, in style).Width;

            if (width <= maxWidth)
            {
                lastBreak = candidateEnd;

                if (atEnd)
                {
                    EmitLine(lines, paragraph[start..candidateEnd].TrimEnd(), style, measurer, maxWidth, lineHeight, TextOverflow.Clip, maxLines, ref truncated);
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
                    TextOverflow.Clip,
                    maxLines,
                    ref truncated);

                start = lastBreak;

                while (start < paragraph.Length && paragraph[start] is ' ' or '\t')
                {
                    start++;
                }

                lastBreak = -1;
                i = start - 1;
                continue;
            }

            if (overflowWrap is OverflowWrap.Anywhere or OverflowWrap.BreakWord && i > start)
            {
                EmitLine(
                    lines,
                    paragraph[start..i],
                    style,
                    measurer,
                    maxWidth,
                    lineHeight,
                    TextOverflow.Clip,
                    maxLines,
                    ref truncated);

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

                    if (measurer.Measure(test, in style).Width > maxWidth)
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
                    TextOverflow.Clip,
                    maxLines,
                    ref truncated);

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
                TextOverflow.Clip,
                maxLines,
                ref truncated);
        }
    }

    private static void AddLine(
        List<TextLine> lines,
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        TextOverflow overflow,
        int maxLines,
        ref bool truncated)
    {
        if (lines.Count >= maxLines)
        {
            truncated = true;
            return;
        }

        EmitLine(lines, text, style, measurer, maxWidth, lineHeight, overflow, maxLines, ref truncated);
    }

    private static void EmitLine(
        List<TextLine> lines,
        string text,
        in TextStyle style,
        ITextMeasurer measurer,
        float maxWidth,
        float lineHeight,
        TextOverflow overflow,
        int maxLines,
        ref bool truncated)
    {
        if (lines.Count >= maxLines)
        {
            truncated = true;
            return;
        }

        var width = measurer.Measure(text, in style).Width;

        if (overflow == TextOverflow.Ellipsis && float.IsFinite(maxWidth) && width > maxWidth)
        {
            var ellipsis = "\u2026";
            var ellipsisWidth = measurer.Measure(ellipsis, in style).Width;
            var target = MathF.Max(0, maxWidth - ellipsisWidth);
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
            Height = lineHeight,
            Baseline = lineHeight * 0.8f,
            IsTruncated = truncated
        });
    }

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

    private static string ApplyTransform(string text, in TextStyle style)
    {
        // Text transform lives in ComputedStyle; TextStyle does not carry it.
        // Callers apply it before creating the style, keeping the text engine
        // independent of CSS property plumbing.
        return text;
    }

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
