using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

public class FocusSiblingTests
{
    [Fact]
    public void FocusedControlChangesFollowingLabelColour()
    {
        var css = """
        .field { display: block; }
        .field > input:focus ~ label { color: rgb(255, 0, 0); }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["field"]);
        var input = TestSupport.Node("input", classes: ["form-control"]);
        var label = TestSupport.Node("label", text: "Floating label");
        root.Children = new[] { input, label };
        input.IsFocused = true;
        var result = TestSupport.Layout(table, root, 500, 200, false);

        var labelBox = result.Root!.Children[1];
        Assert.Equal(0xFFFF0000u, labelBox.Style.Color);
    }
}
