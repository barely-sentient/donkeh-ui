using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public class CarouselReproTests
{
    [Fact]
    public void CaptionAnchorsToSlideBottom()
    {
        var css = """
        .carousel { position: relative; height: 240px; }
        .carousel-inner { position: relative; width: 100%; overflow: hidden; height: 100%; }
        .carousel-item { position: relative; display: none; float: left; width: 100%; margin-right: -100%; height: 100%; }
        .carousel-item.active { display: block; }
        .carousel-caption { position: absolute; right: 15%; bottom: 20px; left: 15%; }
        .carousel-control-prev { position: absolute; top: 0; bottom: 0; display: flex; align-items: center; justify-content: center; width: 15%; }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["carousel"]);
        root.SetAttribute("style", "height: 240px;");
        var inner = TestSupport.Node("div", classes: ["carousel-inner"]);
        inner.SetAttribute("style", "height: 100%;");
        var item = TestSupport.Node("div", classes: ["carousel-item", "active"]);
        item.SetAttribute("style", "height: 100%;");
        var text = new IgniteSupportedDomNode { IsTextNode = true, TextContent = "A slide with a caption" };
        var caption = TestSupport.Node("div", classes: ["carousel-caption"]);
        caption.TextContent = "caption";
        item.Children = new[] { text, caption };
        inner.Children = new[] { item };
        var control = TestSupport.Node("button", classes: ["carousel-control-prev"]);
        root.Children = new[] { inner, control };

        var result = TestSupport.Layout(table, root, 400, 300, false);
        var itemBox = result.Root!.Children[0].Children[0];
        var captionBox = itemBox.Children[1];
        var controlBox = result.Root.Children[1];

        Assert.True(itemBox.BorderBox.Height > 200, $"item height was {itemBox.BorderBox.Height}");
        Assert.True(captionBox.BorderBox.Y > itemBox.BorderBox.Y + 100,
            $"caption y {captionBox.BorderBox.Y} item {itemBox.BorderBox}");
        Assert.True(controlBox.BorderBox.Height > 200, $"control height was {controlBox.BorderBox.Height}");
    }
}
