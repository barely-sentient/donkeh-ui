namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Class names that are also CSS property keywords must match like any other
/// class. The tokenizer emits PropertyName for these, so the selector matcher
/// has to accept both token kinds.
/// </summary>
public sealed class KeywordClassTests
{
    [Theory]
    [InlineData("content")]
    [InlineData("color")]
    [InlineData("display")]
    [InlineData("border")]
    public void PropertyKeywordClassNamesMatch(string className)
    {
        var node = TestSupport.Node("div", classes: className);
        var style = TestSupport.ResolveStyle($".{className} {{ color: #ff0000; }}", node);

        Assert.Equal(0xFFFF0000u, style.Color);
    }
}
