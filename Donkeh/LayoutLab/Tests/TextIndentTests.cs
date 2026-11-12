using Donkeh.LayoutLab;

namespace Donkeh.LayoutLab.Tests;

public class TextIndentTests
{
    [Fact]
    public void TextIndentOffsetsTheFirstLine()
    {
        var css = """
        .indented { width: 200px; text-indent: 15px; }
        """;
        var root = TestSupport.Node("p", text: "Hello world", classes: ["indented"]);
        var result = TestSupport.Layout(css, root, 400, 200, false);
        var box = result.Root!;

        Assert.NotNull(box.Text);
        Assert.Equal(15f, box.Text!.Lines[0].X, 1);
        Assert.Equal(0f, box.Text.Lines[0].Indent - 15f, 1);
    }

    [Fact]
    public void PercentageTextIndentUsesContainingBlockWidth()
    {
        var css = """
        .indented { width: 200px; text-indent: 10%; }
        """;
        var root = TestSupport.Node("p", text: "Hello world", classes: ["indented"]);
        var result = TestSupport.Layout(css, root, 400, 200, false);
        var box = result.Root!;

        Assert.NotNull(box.Text);
        Assert.Equal(20f, box.Text!.Lines[0].X, 1);
    }
}
