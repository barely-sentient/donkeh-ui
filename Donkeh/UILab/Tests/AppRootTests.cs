using Donkeh.Hosting;
using static Donkeh.UILab._;

namespace Donkeh.UILab.Tests;

public sealed class AppRootTests
{
    [Fact]
    public void LocatorFindsAttributedRootAndMapsSettings()
    {
        var definition = AppRootLocator.Find();

        Assert.Equal(typeof(AttributedRoot), definition.ComponentType);
        Assert.Equal(900, definition.Settings.Width);
        Assert.Equal(640, definition.Settings.Height);
        Assert.Equal("Root test", definition.Settings.Title);
        Assert.True(definition.Settings.Frameless);
        Assert.Equal(0xFF102030u, definition.Settings.ClearColor);
        Assert.Equal(9f, definition.Settings.CornerRadius?.TopLeft);
    }

    [Fact]
    public void DefinitionCreatesMountableRoot()
    {
        var definition = AppRootLocator.Find();
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(definition.CreateRoot());

        Assert.Equal("root", root.TextContent);
    }
}

[AppRoot(
    Width = 900,
    Height = 640,
    Title = "Root test",
    Frameless = true,
    CornerRadius = 9f,
    ClearColor = 0xFF102030u)]
public sealed class AttributedRoot : Component
{
    public override Node Render() => H("div", Text("root"));
}
