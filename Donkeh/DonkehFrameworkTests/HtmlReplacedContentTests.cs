using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Replaced content defaults: inline display, Chrome's placeholder sizes for
/// canvas/video/iframe/object/embed and the progress/meter widget metrics.
/// </summary>
public sealed class HtmlReplacedContentTests
{
    [Fact]
    public void CanvasUsesTheThreeHundredByOneFiftyDefault()
    {
        var canvas = RenderedHtml.Element("canvas");

        Assert.Equal(Display.Inline, canvas.Style!.Display);
        Assert.Equal(300, canvas.Bounds.Width, 2);
        Assert.Equal(150, canvas.Bounds.Height, 2);
    }

    [Fact]
    public void VideoUsesTheSameDefaultBoxAndContainFitting()
    {
        var video = RenderedHtml.Element("video");

        Assert.Equal(Display.Inline, video.Style!.Display);
        Assert.Equal(300, video.Bounds.Width, 2);
        Assert.Equal(150, video.Bounds.Height, 2);
        Assert.Equal(ObjectFit.Contain, video.Style!.ObjectFit);
    }

    [Fact]
    public void IframeUsesAnInsetBorderAroundTheDefaultBox()
    {
        var iframe = RenderedHtml.Element("iframe");
        var box = iframe.Box!;

        Assert.Equal(Display.Inline, iframe.Style!.Display);
        Assert.Equal(2, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Inset, iframe.Style!.BorderStyleTop);
        Assert.Equal(304, box.BorderBox.Width, 2);
        Assert.Equal(154, box.BorderBox.Height, 2);
    }

    [Fact]
    public void ObjectAndEmbedUseTheDefaultBoxWithoutABorder()
    {
        foreach (var tag in new[] { "object", "embed" })
        {
            var element = RenderedHtml.Element(tag);

            Assert.Equal(Display.Inline, element.Style!.Display);
            Assert.Equal(0, element.Box!.Border.Top, 3);
            Assert.Equal(300, element.Bounds.Width, 2);
            Assert.Equal(150, element.Bounds.Height, 2);
        }
    }

    [Fact]
    public void ImageUsesItsWidthAndHeightAttributes()
    {
        var image = RenderedHtml.Element("img", new Props
        {
            Src = "image.png",
            Alt = "Image",
            Attributes = new Dictionary<string, string>
            {
                ["width"] = "120",
                ["height"] = "80"
            }
        });

        Assert.Equal(Display.Inline, image.Style!.Display);
        Assert.Equal(120, image.Bounds.Width, 2);
        Assert.Equal(80, image.Bounds.Height, 2);
    }

    [Fact]
    public void ProgressUsesTenEmByOneEmAndNoBorder()
    {
        var progress = RenderedHtml.Element("progress");
        var style = progress.Style!;

        Assert.Equal(Display.InlineBlock, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(160, progress.Bounds.Width, 2);
        Assert.Equal(16, progress.Bounds.Height, 2);
        Assert.Equal(0, progress.Box!.Border.Top, 3);
        Assert.Equal(CssColor.Transparent, style.BackgroundColor);
        Assert.Equal(-3.2f, style.VerticalAlign.Resolve(new LengthContext(fontSize: 16)), 2);
    }

    [Fact]
    public void MeterUsesFiveEmByOneEm()
    {
        var meter = RenderedHtml.Element("meter");

        Assert.Equal(Display.InlineBlock, meter.Style!.Display);
        Assert.Equal(80, meter.Bounds.Width, 2);
        Assert.Equal(16, meter.Bounds.Height, 2);
        Assert.Equal(0, meter.Box!.Border.Top, 3);
    }

    [Fact]
    public void AudioWithControlsHasChromesThreeHundredByFiftyFourBox()
    {
        var audio = RenderedHtml.Element("audio", new Props { Controls = true });

        Assert.Equal(Display.Inline, audio.Style!.Display);
        Assert.Equal(300, audio.Bounds.Width, 2);
        Assert.Equal(54, audio.Bounds.Height, 2);
    }

    [Fact]
    public void SvgIsInlineAndHasNoDefaultBox()
    {
        var svg = RenderedHtml.Element("svg");

        Assert.Equal(Display.Inline, svg.Style!.Display);
        Assert.Equal(0, svg.Bounds.Width, 2);
        Assert.Equal(0, svg.Bounds.Height, 2);
    }
}
