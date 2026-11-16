using System.Reflection;

[assembly: Donkeh.UILab.Stylesheet("Assets/stylesheet.css")]

namespace Donkeh.UILab.Tests;

public sealed class StylesheetSourceTests
{
    [Fact]
    public void SourcesExposeDeclaringAssemblyPathAndCss()
    {
        var assembly = typeof(StylesheetSourceTests).Assembly;
        var source = Assert.Single(
            Donkeh.UILab.Stylesheet.Sources(assembly),
            candidate => candidate.Path == "Assets/stylesheet.css");

        Assert.Same(assembly, source.Assembly);
        Assert.Contains(".source-test", source.Css, StringComparison.Ordinal);
        Assert.Equal(source.Css, Donkeh.UILab.Stylesheet.Load(assembly));
    }

    [Fact]
    public void SourcesRejectNullAssembly()
    {
        Assert.Throws<ArgumentNullException>(() =>
            Donkeh.UILab.Stylesheet.Sources((Assembly)null!));
    }
}
