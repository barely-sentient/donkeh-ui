using System.Reflection;
using Donkeh.UILab;

[assembly: Image("Assets/sample.png")]

namespace Donkeh.UILab.Tests;

[Image("Assets/type-level.png")]
public sealed class TypeLevelAssetHost
{
}

public sealed class ImageAssetTests
{
    [Fact]
    public void AttributeTrimsTheDeclaredPath()
    {
        var attribute = new ImageAttribute("  Assets\\sample.png  ");

        Assert.Equal("Assets\\sample.png", attribute.Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AttributeRejectsBlankPaths(string path)
    {
        Assert.ThrowsAny<ArgumentException>(() => new ImageAttribute(path));
    }

    [Fact]
    public void AssemblyLevelDeclarationResolvesTheEmbeddedResource()
    {
        var bytes = Image.Load("Assets/sample.png");

        Assert.NotEmpty(bytes);
        Assert.Equal(
            Image.Load("packed:Assets/sample.png"),
            bytes);
    }

    [Fact]
    public void FileNameFallbackMatchesANestedResource()
    {
        Assert.True(Image.TryGetBytes("sample.png", out var bytes));
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void PrefixedSourcesForceThePackedLookup()
    {
        Assert.True(Image.TryGetBytes("packed:Assets/sample.png", out var packed));
        Assert.True(Image.TryGetBytes("embedded:Assets/sample.png", out var embedded));
        Assert.True(Image.TryGetBytes("asset:Assets/sample.png", out var asset));

        Assert.Equal(packed, embedded);
        Assert.Equal(packed, asset);
    }

    [Fact]
    public void DifferentDirectoriesDoNotMatchByFileName()
    {
        Assert.False(Image.TryGetBytes("Other/sample.png", out var ignored));
    }

    [Fact]
    public void UnknownSourcesReturnFalse()
    {
        Assert.False(Image.TryGetBytes("missing.png", out var bytes));
        Assert.Empty(bytes);
    }

    [Fact]
    public void LoadThrowsForAnUnknownSource()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => Image.Load("missing.png"));

        Assert.Contains("missing.png", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssemblyOverloadScansOnlyThatAssembly()
    {
        var assembly = typeof(ImageAssetTests).Assembly;

        Assert.True(Image.TryGetBytes(assembly, "Assets/sample.png", out var bytes));
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void TypeLevelDeclarationsAreDiscovered()
    {
        Assert.True(Image.TryGetBytes("Assets/type-level.png", out var bytes));
        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void PackedSourceDetectionIgnoresCaseAndWhitespace()
    {
        Assert.True(Image.IsPackedSource("  PACKED:Assets/sample.png"));
        Assert.True(Image.IsPackedSource("Embedded:Assets/sample.png"));
        Assert.True(Image.IsPackedSource("asset:Assets/sample.png"));
        Assert.False(Image.IsPackedSource("Assets/sample.png"));
        Assert.False(Image.IsPackedSource(""));
    }
}
