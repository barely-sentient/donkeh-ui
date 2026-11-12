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
        .field > label { pointer-events: none; }
        .field > input:focus ~ label { color: rgb(255, 0, 0); transform: translateY(-8px); }
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
        Assert.NotNull(labelBox.Style.Transform);
        Assert.Equal(PointerEvents.None, labelBox.Style.PointerEvents);
    }

    [Fact]
    public void InteractionVersionInvalidatesSiblingLabelStyle()
    {
        var css = """
        .field { display: block; }
        .field > label { pointer-events: none; }
        .field > input:focus ~ label { color: rgb(255, 0, 0); transform: translateY(-8px); }
        """;
        var table = new Engine(Condition.AlwaysTrueCompiler).Build(css);
        var root = TestSupport.Node("div", classes: ["field"]);
        var input = TestSupport.Node("input");
        var label = TestSupport.Node("label", text: "Label");
        root.Children = new[] { input, label };
        var engine = new LayoutEngine(table);
        var options = TestSupport.Options();

        var before = engine.Layout(root, options);
        Assert.NotEqual(0xFFFF0000u, before.Root!.Children[1].Style.Color);

        input.IsFocused = true;
        options.InteractionVersion++;
        var after = engine.Layout(root, options);
        Assert.Equal(0xFFFF0000u, after.Root!.Children[1].Style.Color);
        Assert.NotNull(after.Root.Children[1].Style.Transform);
    }
}
