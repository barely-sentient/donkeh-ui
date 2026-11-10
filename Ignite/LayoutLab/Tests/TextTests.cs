namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Tests for the text layer: the deterministic measurer, wrapping, whitespace
/// handling, overflow, line clamping, alignment and the float-band variant.
/// A fixed-width measurer keeps the expected line breaks exact.
/// </summary>
public class TextTests
{
    /// <summary>Every character is ten pixels wide, independent of the style.</summary>
    private sealed class FixedMeasurer : ITextMeasurer
    {
        public static readonly FixedMeasurer Instance = new();

        public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style)
        {
            return new TextMetrics(text.Length * 10, style.FontSize * 0.8f, style.FontSize * 0.2f);
        }

        public TextMetrics Measure(string text, in TextStyle style)
        {
            return Measure(text.AsSpan(), in style);
        }
    }

    private static TextStyle Style(
        float fontSize = 10,
        float lineHeight = 20,
        TextAlign align = TextAlign.Start,
        Direction direction = Direction.LeftToRight,
        TextDecoration decoration = TextDecoration.None,
        WhiteSpace whiteSpace = WhiteSpace.Normal,
        string fontFamily = "sans-serif",
        int fontWeight = 400,
        bool italic = false,
        float letterSpacing = 0)
    {
        return new TextStyle(
            fontFamily,
            fontSize,
            fontWeight,
            italic,
            0xFF000000,
            letterSpacing,
            lineHeight,
            align,
            decoration,
            whiteSpace,
            direction);
    }

    private static TextLayout Layout(
        string text,
        float maxWidth = 1000,
        WhiteSpace whiteSpace = WhiteSpace.Normal,
        OverflowWrap overflowWrap = OverflowWrap.Normal,
        int lineClamp = 0,
        TextOverflow textOverflow = TextOverflow.Clip,
        TextAlign align = TextAlign.Start,
        Direction direction = Direction.LeftToRight)
    {
        var style = Style(align: align, direction: direction);
        return TextLayoutEngine.Layout(
            text, in style, FixedMeasurer.Instance, maxWidth,
            whiteSpace, overflowWrap, lineClamp, textOverflow);
    }

    // -------------------------------------------------------------- measurer

    [Fact]
    public void TheDefaultStyleMatchesTheCssInitialValues()
    {
        var style = TextStyle.Default;

        Assert.Equal("sans-serif", style.FontFamily);
        Assert.Equal(16, style.FontSize, 3);
        Assert.Equal(400, style.FontWeight);
        Assert.False(style.Italic);
        Assert.Equal(CssColor.Black, style.Color);
        Assert.Equal(0, style.LetterSpacing, 3);
        Assert.Equal(1.2f, style.LineHeight, 3);
        Assert.Equal(TextAlign.Start, style.Align);
        Assert.Equal(TextDecoration.None, style.Decoration);
        Assert.Equal(WhiteSpace.Normal, style.WhiteSpace);
        Assert.Equal(Direction.LeftToRight, style.Direction);
    }

    [Fact]
    public void EmptyTextHasZeroWidthButAFullLineBox()
    {
        var metrics = SimpleTextMeasurer.Instance.Measure(string.Empty, Style(fontSize: 20));

        Assert.Equal(0, metrics.Width, 3);
        Assert.Equal(16, metrics.Ascent, 3);
        Assert.Equal(4, metrics.Descent, 3);
        Assert.Equal(20, metrics.Height, 3);
    }

    [Theory]
    [InlineData("0", 0.56f)]
    [InlineData("A", 0.65f)]
    [InlineData("a", 0.51f)]
    [InlineData(" ", 0.26f)]
    [InlineData(".", 0.28f)]
    [InlineData(",", 0.28f)]
    [InlineData("!", 0.28f)]
    [InlineData("@", 0.48f)]
    public void AsciiAdvancesFollowTheDeterministicTable(string text, float factor)
    {
        var metrics = SimpleTextMeasurer.Instance.Measure(text, Style(fontSize: 100));
        Assert.Equal(factor * 100, metrics.Width, 3);
    }

    [Fact]
    public void TabsAreWideAndNewlinesHaveNoAdvance()
    {
        Assert.Equal(104, SimpleTextMeasurer.Instance.Measure("\t", Style(fontSize: 100)).Width, 3);
        Assert.Equal(0, SimpleTextMeasurer.Instance.Measure("\n", Style(fontSize: 100)).Width, 3);
        Assert.Equal(0, SimpleTextMeasurer.Instance.Measure("\r", Style(fontSize: 100)).Width, 3);
    }

    [Fact]
    public void CjkIsFullWidthAndOtherNonAsciiIsNarrower()
    {
        Assert.Equal(100, SimpleTextMeasurer.Instance.Measure("\u3042", Style(fontSize: 100)).Width, 3);
        Assert.Equal(58, SimpleTextMeasurer.Instance.Measure("\u00e9", Style(fontSize: 100)).Width, 3);
    }

    [Fact]
    public void LetterSpacingIsAddedPerRune()
    {
        var metrics = SimpleTextMeasurer.Instance.Measure("abc", Style(fontSize: 10, letterSpacing: 2));
        Assert.Equal(3 * 5.1f + 3 * 2, metrics.Width, 3);
    }

    [Fact]
    public void BoldAndItalicWidenTheRun()
    {
        var plain = SimpleTextMeasurer.Instance.Measure("abc", Style(fontSize: 10));

        var bold = SimpleTextMeasurer.Instance.Measure("abc", Style(fontSize: 10, fontWeight: 600));
        Assert.Equal(plain.Width * 1.03f, bold.Width, 3);

        var italic = SimpleTextMeasurer.Instance.Measure("abc", Style(fontSize: 10, italic: true));
        Assert.Equal(plain.Width * 1.01f, italic.Width, 3);

        var both = SimpleTextMeasurer.Instance.Measure(
            "abc", Style(fontSize: 10, fontWeight: 700, italic: true));
        Assert.Equal(plain.Width * 1.03f * 1.01f, both.Width, 3);
    }

    [Fact]
    public void MetricsHeightIsAscentPlusDescent()
    {
        Assert.Equal(28, new TextMetrics(10, 20, 8).Height, 3);
        Assert.Equal(TextMetrics.Empty, new TextMetrics(0, 0, 0));
    }

    // -------------------------------------------------------------- layout

    [Fact]
    public void EmptyAndNullTextReturnTheSharedEmptyLayout()
    {
        Assert.Same(TextLayout.Empty, Layout(string.Empty));
        Assert.Same(TextLayout.Empty, Layout(null!));
    }

    [Fact]
    public void ASingleLineKeepsItsIntrinsicSize()
    {
        var layout = Layout("abc");

        Assert.Single(layout.Lines);
        Assert.Equal("abc", layout.Lines[0].Text);
        Assert.Equal(30, layout.Lines[0].Width, 3);
        Assert.Equal(20, layout.Lines[0].Height, 3);
        Assert.Equal(16, layout.Lines[0].Baseline, 3);
        Assert.Equal(0, layout.Lines[0].X, 3);
        Assert.Equal(0, layout.Lines[0].Y, 3);
        Assert.Equal(30, layout.Size.Width, 3);
        Assert.Equal(20, layout.Size.Height, 3);
        Assert.False(layout.IsTruncated);
        Assert.Equal("abc", layout.Text);
    }

    [Fact]
    public void NormalWhitespaceIsCollapsedAndTrimmed()
    {
        var layout = Layout("  a \t\n  b  ");

        Assert.Single(layout.Lines);
        Assert.Equal("a b", layout.Lines[0].Text);
    }

    [Fact]
    public void WrappingBreaksAtSpacesInTheNormalMode()
    {
        var layout = Layout("aaaa bbbb", maxWidth: 60);

        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal("aaaa", layout.Lines[0].Text);
        Assert.Equal("bbbb", layout.Lines[1].Text);
        Assert.Equal(20, layout.Lines[1].Y, 3);
        Assert.Equal(40, layout.Size.Width, 3);
        Assert.Equal(40, layout.Size.Height, 3);
    }

    [Fact]
    public void NoWrapKeepsEverythingOnOneLine()
    {
        var layout = Layout("aaaa bbbb cccc", whiteSpace: WhiteSpace.NoWrap, maxWidth: 20);

        Assert.Single(layout.Lines);
        Assert.Equal("aaaa bbbb cccc", layout.Lines[0].Text);
        Assert.Equal(140, layout.Lines[0].Width, 3);
    }

    [Fact]
    public void PreAndPreWrapHonourHardLineBreaks()
    {
        foreach (var mode in new[] { WhiteSpace.Pre, WhiteSpace.PreWrap, WhiteSpace.PreLine })
        {
            var layout = Layout("a\nb\r\nc", whiteSpace: mode);

            Assert.Equal(3, layout.Lines.Length);
            Assert.Equal("a", layout.Lines[0].Text);
            Assert.Equal("b", layout.Lines[1].Text);
            Assert.Equal("c", layout.Lines[2].Text);
        }
    }

    [Fact]
    public void PrePreservesSpacesWhileNormalCollapsesThem()
    {
        var pre = Layout("  a  b", whiteSpace: WhiteSpace.Pre);
        Assert.Equal("  a  b", pre.Lines[0].Text);
        Assert.Equal(60, pre.Lines[0].Width, 3);

        var normal = Layout("  a  b");
        Assert.Equal("a b", normal.Lines[0].Text);
    }

    [Fact]
    public void ATrailingHardBreakProducesAnEmptyLine()
    {
        var layout = Layout("a\n", whiteSpace: WhiteSpace.Pre);

        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal("a", layout.Lines[0].Text);
        Assert.Equal(string.Empty, layout.Lines[1].Text);
        Assert.Equal(0, layout.Lines[1].Width, 3);
    }

    [Fact]
    public void NonPositiveMaxWidthSkipsWrapping()
    {
        var layout = Layout("aaaa bbbb", maxWidth: 0);

        Assert.Single(layout.Lines);
        Assert.Equal("aaaa bbbb", layout.Lines[0].Text);
    }

    [Fact]
    public void OverflowWrapAnywhereBreaksInsideWords()
    {
        var layout = Layout("abcdef", maxWidth: 25, overflowWrap: OverflowWrap.Anywhere);

        Assert.Equal(3, layout.Lines.Length);
        Assert.Equal("ab", layout.Lines[0].Text);
        Assert.Equal("cd", layout.Lines[1].Text);
        Assert.Equal("ef", layout.Lines[2].Text);
    }

    [Fact]
    public void OverflowWrapBreakWordBehavesLikeAnywhere()
    {
        var layout = Layout("abcdef", maxWidth: 25, overflowWrap: OverflowWrap.BreakWord);
        Assert.Equal(3, layout.Lines.Length);
    }

    [Fact]
    public void BreakOpportunitiesIncludeHyphensAndSlashes()
    {
        var layout = Layout("aaaa-bbbb", maxWidth: 60);

        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal("aaaa-", layout.Lines[0].Text);
        Assert.Equal("bbbb", layout.Lines[1].Text);
    }

    [Fact]
    public void EllipsisTruncatesOverflowingText()
    {
        var layout = Layout("abcdef", maxWidth: 40, whiteSpace: WhiteSpace.NoWrap,
            textOverflow: TextOverflow.Ellipsis);

        Assert.Single(layout.Lines);
        Assert.Equal("abc\u2026", layout.Lines[0].Text);
        Assert.True(layout.Lines[0].IsTruncated);
        Assert.True(layout.IsTruncated);
    }

    [Fact]
    public void EllipsisLeavesTextThatFitsUntouched()
    {
        var layout = Layout("abc", maxWidth: 40, textOverflow: TextOverflow.Ellipsis);

        Assert.Equal("abc", layout.Lines[0].Text);
        Assert.False(layout.IsTruncated);
    }

    [Fact]
    public void LineClampStopsAfterTheRequestedNumberOfLines()
    {
        var layout = Layout("a\nb\nc", whiteSpace: WhiteSpace.Pre, lineClamp: 2);

        Assert.Equal(2, layout.Lines.Length);
        Assert.True(layout.IsTruncated);
        Assert.Equal(40, layout.Size.Height, 3);
    }

    [Fact]
    public void LineClampOfZeroMeansUnlimited()
    {
        var layout = Layout("a\nb\nc", whiteSpace: WhiteSpace.Pre, lineClamp: 0);
        Assert.Equal(3, layout.Lines.Length);
        Assert.False(layout.IsTruncated);
    }

    [Theory]
    [InlineData(TextAlign.Start, Direction.LeftToRight, 0f)]
    [InlineData(TextAlign.Start, Direction.RightToLeft, 80f)]
    [InlineData(TextAlign.End, Direction.LeftToRight, 80f)]
    [InlineData(TextAlign.End, Direction.RightToLeft, 0f)]
    [InlineData(TextAlign.Left, Direction.LeftToRight, 0f)]
    [InlineData(TextAlign.Right, Direction.RightToLeft, 80f)]
    [InlineData(TextAlign.Center, Direction.LeftToRight, 40f)]
    public void AlignmentUsesTheAvailableWidthAndDirection(
        TextAlign align, Direction direction, float expectedX)
    {
        var layout = Layout("ab", maxWidth: 100, whiteSpace: WhiteSpace.NoWrap,
            align: align, direction: direction);

        Assert.Equal(expectedX, layout.Lines[0].X, 3);
    }

    [Fact]
    public void AlignmentIsZeroWhenTheWidthIsUnbounded()
    {
        var layout = Layout("ab", maxWidth: float.PositiveInfinity,
            whiteSpace: WhiteSpace.NoWrap, align: TextAlign.Center);

        Assert.Equal(0, layout.Lines[0].X, 3);
    }

    [Fact]
    public void TextLineBoundsUseTheLineOriginAndSize()
    {
        var line = new TextLine { X = 3, Y = 4, Width = 5, Height = 6 };
        Assert.Equal(new Rect(3, 4, 5, 6), line.Bounds);
    }

    [Fact]
    public void EmptyTextDoesNotApplyATextTransform()
    {
        // The text engine deliberately leaves text-transform to the style
        // resolver, so the input is preserved verbatim.
        var layout = Layout("Hello");
        Assert.Equal("Hello", layout.Text);
        Assert.Equal("Hello", layout.Lines[0].Text);
    }

    // -------------------------------------------------------------- bands

    private static TextLayout Bands(
        string text,
        Func<float, (float Left, float Right)> bandAt,
        float originX = 0,
        float startY = 0,
        float maxWidth = 1000,
        WhiteSpace whiteSpace = WhiteSpace.Normal,
        OverflowWrap overflowWrap = OverflowWrap.Normal,
        int lineClamp = 0,
        TextOverflow textOverflow = TextOverflow.Clip,
        TextAlign align = TextAlign.Start,
        Direction direction = Direction.LeftToRight)
    {
        var style = Style(align: align, direction: direction);
        return TextLayoutEngine.LayoutWithBands(
            text, in style, FixedMeasurer.Instance, originX, startY,
            whiteSpace, overflowWrap, lineClamp, textOverflow, bandAt);
    }

    [Fact]
    public void BandLayoutOfEmptyTextReturnsTheSharedEmptyLayout()
    {
        Assert.Same(TextLayout.Empty, Bands(string.Empty, _ => (0, 100)));
    }

    [Fact]
    public void BandLayoutStartsAtTheBandLeftEdge()
    {
        var layout = Bands("ab", _ => (5, 105));

        Assert.Single(layout.Lines);
        Assert.Equal(5, layout.Lines[0].X, 3);
        Assert.Equal(20, layout.Lines[0].Width, 3);
        Assert.Equal(0, layout.Lines[0].Y, 3);
    }

    [Fact]
    public void BandOriginIsSubtractedFromTheLineX()
    {
        var layout = Bands("ab", _ => (5, 105), originX: 2);
        Assert.Equal(3, layout.Lines[0].X, 3);
    }

    [Fact]
    public void BandLayoutWraPsToTheBandWidth()
    {
        var layout = Bands("aaaa bbbb", _ => (0, 60));

        Assert.Equal(2, layout.Lines.Length);
        Assert.Equal("aaaa", layout.Lines[0].Text);
        Assert.Equal("bbbb", layout.Lines[1].Text);
        Assert.Equal(20, layout.Lines[1].Y, 3);
    }

    [Fact]
    public void BandsAreQueriedPerLineTop()
    {
        var seen = new List<float>();
        var layout = Bands("aaaa bbbb cccc", y =>
        {
            seen.Add(y);
            return (0, 60);
        });

        Assert.Equal(3, layout.Lines.Length);
        Assert.Contains(0, seen);
        Assert.Contains(20, seen);
    }

    [Fact]
    public void BandLayoutEllipsisTruncatesPerBand()
    {
        var layout = Bands("abcdef", _ => (0, 30), textOverflow: TextOverflow.Ellipsis,
            whiteSpace: WhiteSpace.NoWrap);

        Assert.Single(layout.Lines);
        Assert.Equal("ab\u2026", layout.Lines[0].Text);
        Assert.True(layout.IsTruncated);
    }

    [Fact]
    public void BandLayoutStartsAtTheRequestedY()
    {
        var layout = Bands("ab", _ => (0, 100), startY: 50);
        Assert.Equal(0, layout.Lines[0].Y, 3);
        Assert.Equal(20, layout.Size.Height, 3);
    }

    [Fact]
    public void BandLayoutHonoursLineClamp()
    {
        var layout = Bands("a\nb\nc", _ => (0, 100),
            whiteSpace: WhiteSpace.Pre, lineClamp: 2);

        Assert.Equal(2, layout.Lines.Length);
        Assert.True(layout.IsTruncated);
    }

    [Fact]
    public void BandLayoutWithNoWrapKeepsOneLine()
    {
        var layout = Bands("aaaa bbbb", _ => (0, 20), whiteSpace: WhiteSpace.NoWrap);
        Assert.Single(layout.Lines);
        Assert.Equal("aaaa bbbb", layout.Lines[0].Text);
    }

    [Fact]
    public void EmptyBandLinesAreEmittedForBlankParagraphs()
    {
        var layout = Bands("a\n\nb", _ => (0, 100), whiteSpace: WhiteSpace.Pre);

        Assert.Equal(3, layout.Lines.Length);
        Assert.Equal(string.Empty, layout.Lines[1].Text);
    }

    [Fact]
    public void BandLayoutAlignsWithinTheBand()
    {
        var layout = Bands("ab", _ => (10, 110), align: TextAlign.Center, direction: Direction.LeftToRight);
        Assert.Equal(10 + 40, layout.Lines[0].X, 3);
    }
}
