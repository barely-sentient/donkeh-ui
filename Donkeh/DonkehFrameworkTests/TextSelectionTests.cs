using Donkeh.LayoutLab;
using Donkeh.Windowing;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Regression coverage for text selection geometry. Line coordinates are text
/// local while clips are painted absolute, so scrolled controls must translate
/// the clip before choosing visible lines.
/// </summary>
public sealed class TextSelectionTests
{
    [Fact]
    public void ScrolledClipStillSelectsTheFirstLine()
    {
        var lines = new[]
        {
            new TextLine { Text = "Text value", Y = 0, Width = 80, Height = 17 }
        };

        var clip = new Rect(101.65625f, 1649.6769f, 171, 17);
        var (first, last) = DonkehWindow.GetVisibleSelectionLines(lines, clip, 1649.6769f);

        Assert.Equal(0, first);
        Assert.Equal(1, last);
    }

    [Fact]
    public void LineRangeSkipsLinesAboveTheClip()
    {
        var lines = new[]
        {
            new TextLine { Text = "First", Y = 0, Width = 40, Height = 17 },
            new TextLine { Text = "Second", Y = 20, Width = 40, Height = 17 }
        };

        var clip = new Rect(0, 20, 100, 17);
        var (first, last) = DonkehWindow.GetVisibleSelectionLines(lines, clip, originY: 0);

        Assert.Equal(1, first);
        Assert.Equal(2, last);
    }

    [Fact]
    public void ClipBelowEveryLineProducesAnEmptyRange()
    {
        var lines = new[]
        {
            new TextLine { Text = "First", Y = 0, Width = 40, Height = 17 }
        };

        var clip = new Rect(0, 100, 100, 10);
        var (first, last) = DonkehWindow.GetVisibleSelectionLines(lines, clip, originY: 0);

        Assert.Equal(1, first);
        Assert.Equal(1, last);
    }
}
