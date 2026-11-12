using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public class ImageIntrinsicTests
{
    [Fact]
    public void FlexColumnImageUsesIntrinsicHeight()
    {
        var css = """
        .column { display: flex; flex-direction: column; align-items: flex-start; width: 400px; }
        .picture { max-width: 100%; height: auto; }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["column"]);
        var image = TestSupport.Node("img", classes: ["picture"]);
        root.Children = new[] { image };

        var options = TestSupport.Options(width: 500, height: 500);
        options.IntrinsicSizeProvider = node =>
            node.TagName == "img" ? new Size(512, 288) : null;

        var result = TestSupport.Layout(table, root, options);
        var imageBox = result.Root!.Children[0];

        Assert.True(imageBox.BorderBox.Width > 0);
        Assert.True(imageBox.BorderBox.Height > 0);
        Assert.Equal(
            imageBox.BorderBox.Width * (288f / 512f),
            imageBox.BorderBox.Height,
            1);
    }

    [Fact]
    public void ImageWithDefiniteWidthKeepsAspectRatio()
    {
        var css = """
        .card { display: flex; flex-direction: column; width: 300px; }
        .card-img-top { width: 100%; height: auto; }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["card"]);
        var image = TestSupport.Node("img", classes: ["card-img-top"]);
        root.Children = new[] { image };

        var options = TestSupport.Options(width: 500, height: 500);
        options.IntrinsicSizeProvider = node =>
            node.TagName == "img" ? new Size(512, 288) : null;

        var result = TestSupport.Layout(table, root, options);
        var imageBox = result.Root!.Children[0];

        Assert.Equal(300f, imageBox.BorderBox.Width, 1);
        Assert.Equal(300f * (288f / 512f), imageBox.BorderBox.Height, 1);
    }
}
