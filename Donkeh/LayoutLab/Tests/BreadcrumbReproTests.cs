using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public class BreadcrumbReproTests
{
    [Fact]
    public void BreadcrumbSeparatorStaysOnTextLine()
    {
        var css = """
        .breadcrumb { display: flex; flex-wrap: wrap; padding: 0; list-style: none; margin: 0; }
        .breadcrumb-item { display: block; padding-left: 8px; }
        a { display: inline; }
        .breadcrumb-item + .breadcrumb-item::before { float: left; padding-right: 8px; color: #6c757d; content: "/"; }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("ol", classes: ["breadcrumb"]);
        var item1 = TestSupport.Node("li", text: "Home", classes: ["breadcrumb-item"]);
        var item2 = TestSupport.Node("li", text: "Library", classes: ["breadcrumb-item"]);
        var item3 = TestSupport.Node("li", text: "Data", classes: ["breadcrumb-item"]);
        root.Children = new[] { item1, item2, item3 };
        var result = TestSupport.Layout(table, root, 1000, 800, false);

        var itemBoxes = result.Root!.Children;
        Assert.Empty(itemBoxes[0].Children);
        Assert.Equal(2, itemBoxes[1].Children.Count);
        Assert.Equal(2, itemBoxes[2].Children.Count);
        Assert.Equal(Float.Left, itemBoxes[1].Children[0].Style.Float);
        Assert.Equal(Float.Left, itemBoxes[2].Children[0].Style.Float);
        Assert.True(itemBoxes[1].Children[1].Node.IsTextNode);
        Assert.True(itemBoxes[2].Children[1].Node.IsTextNode);

        var pseudo = itemBoxes[1].Children[0];
        var anchor = itemBoxes[1].Children[1];
        Assert.Equal(anchor.BorderBox.Y, pseudo.BorderBox.Y);
    }
}
