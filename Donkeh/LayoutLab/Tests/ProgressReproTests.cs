using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public class ProgressReproTests
{
    [Fact]
    public void InlinePercentWidthOnFlexItem()
    {
        var css = """
        .progress { display: flex; height: 16px; overflow: hidden; }
        .progress-bar { display: flex; flex-direction: column; justify-content: center; overflow: hidden; }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["progress"]);
        var bar = TestSupport.Node("div", classes: ["progress-bar"]);
        bar.SetAttribute("style", "width: 65%;");
        root.Children = new[] { bar };
        var result = TestSupport.Layout(table, root, 1000, 100, false);
        var barBox = result.Root!.Children[0];
        Assert.Equal(650f, barBox.BorderBox.Width, 1);
    }
}
