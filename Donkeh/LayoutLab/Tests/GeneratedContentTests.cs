using Donkeh.Data;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public sealed class GeneratedContentTests
{
    [Fact]
    public void BeforeContentDecodesCssHexEscapes()
    {
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(
            ".icon::before { content: \"\\f0c9\"; }");
        var root = TestSupport.Node("div");
        var icon = TestSupport.Node("span", classes: ["icon"]);
        root.Children = [icon];

        var result = TestSupport.Layout(table, root, 200, 100, false);
        var iconBox = result.Root!.Children[0];
        var pseudo = Assert.Single(iconBox.Children);

        Assert.Equal("\uF0C9", pseudo.Node.TextContent);
    }

    [Fact]
    public void SimpleIsSelectorListExpandsWhenRequested()
    {
        // The native host expands simple :is() lists before parsing; verify the
        // matcher still rejects the logical form itself rather than matching wrongly.
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(
            ":is(.a, .b) { color: red; }");

        Assert.Contains(
            table.Selectors,
            selector => !selector.IsMatchable);
    }
}
