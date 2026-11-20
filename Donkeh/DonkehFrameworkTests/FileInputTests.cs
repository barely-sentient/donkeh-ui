using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Regression coverage for file inputs: the displayed name, the stored path and
/// the value the native chooser commits.
/// </summary>
public sealed class FileInputTests
{
    [Fact]
    public void EmptyFileInputShowsChromesPrompt()
    {
        var input = RenderedHtml.Element("input", new Props { Type = "file" });

        Assert.Equal("No file chosen", input.Box!.Text!.Text);
        Assert.Equal(88, input.Box!.Padding.Left, 2);
    }

    [Fact]
    public void FileInputDisplaysTheFileNameButKeepsTheFullPath()
    {
        var path = Path.Combine("tmp", "photo.png");
        var input = RenderedHtml.Element("input", new Props
        {
            Type = "file",
            Value = path
        });

        Assert.Equal("photo.png", input.Box!.Text!.Text);
        Assert.Equal(path, input.Value);
    }

    [Fact]
    public void ChoosingAFileWritesThePathAndReturnsTheDisplayName()
    {
        var path = Path.Combine("tmp", "photo.png");
        var element = H("input", new Props { Type = "file" });

        var name = FileDialog.Apply(element, path);

        Assert.Equal("photo.png", name);
        Assert.True(element.TryGetAttribute("value", out var stored));
        Assert.Equal(path, stored);
    }
}
