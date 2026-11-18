namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// A canvas is replaced content, so its draw marker must sit at the canvas's
/// paint position. This is what lets a DOM popup with a higher z-index cover
/// the canvas.
/// </summary>
public sealed class CanvasPaintOrderTests
{
    /// <summary>
    /// A promoted overlay paints after the canvas marker, not before every
    /// canvas in the frame.
    /// </summary>
    [Fact]
    public void CanvasMarkerObeysZIndex()
    {
        var canvas = TestSupport.Node("canvas", null, null, "art");
        var overlay = TestSupport.Node("div", "overlay");
        var root = TestSupport.Node("div", "root");
        root.Children = [canvas, overlay];

        const string Css = """
            #root { position: relative; width: 400px; height: 300px; background: #111; }
            .art { position: absolute; left: 0; top: 0; width: 200px; height: 150px; }
            #overlay {
                position: absolute; left: 10px; top: 10px;
                width: 50px; height: 50px;
                z-index: 10; background: #ff0000;
            }
            """;

        var result = TestSupport.Layout(Css, root);
        var opcodes = result.Opcodes;
        var canvasAt = -1;
        var overlayAt = -1;

        for (var i = 0; i < opcodes.Length; i++)
        {
            switch (opcodes[i].OpCode)
            {
                case InstructionOpCode.DrawCanvas:
                    canvasAt = i;
                    break;
                case InstructionOpCode.DrawRect when MathF.Abs(opcodes[i].W - 50) < 0.01f:
                    overlayAt = i;
                    break;
            }
        }

        Assert.True(canvasAt >= 0, "The canvas emitted no DrawCanvas marker.");
        Assert.True(overlayAt >= 0, "The promoted overlay emitted no rectangle.");
        Assert.True(canvasAt < overlayAt, "The overlay must paint after the canvas it covers.");

        var painted = Assert.Single(result.Canvases);
        Assert.Same(canvas, painted.Node);
    }
}
