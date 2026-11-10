using Ignite.Data.Dom;

namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Host-driven interaction pseudo classes are evaluated against node state at
/// match time instead of being rejected as unsupported selectors.
/// </summary>
public sealed class InteractionPseudoTests
{
    [Fact]
    public void HoverMatchesOnlyWhileTheNodeIsHovered()
    {
        const string css = ".btn:hover { color: #ff0000; }";
        var node = TestSupport.Node("button", classes: "btn");

        Assert.Equal(0xFF000000u, TestSupport.ResolveStyle(css, node).Color);

        node.IsHovered = true;
        Assert.Equal(0xFFFF0000u, TestSupport.ResolveStyle(css, node).Color);

        node.IsHovered = false;
        Assert.Equal(0xFF000000u, TestSupport.ResolveStyle(css, node).Color);
    }

    [Fact]
    public void FocusAndFocusVisibleAreSeparateStates()
    {
        const string css = ".field:focus { color: #00ff00; } .field:focus-visible { background-color: #0000ff; }";
        var node = TestSupport.Node("input", classes: "field");

        node.IsFocused = true;
        var focused = TestSupport.ResolveStyle(css, node);
        Assert.Equal(0xFF00FF00u, focused.Color);
        Assert.Equal(0x00000000u, focused.BackgroundColor);

        node.IsFocusVisible = true;
        var visible = TestSupport.ResolveStyle(css, node);
        Assert.Equal(0xFF0000FFu, visible.BackgroundColor);
    }

    [Fact]
    public void FocusWithinMatchesAnAncestorOfTheFocusedNode()
    {
        const string css = ".card:focus-within { border-color: #ff0000; }";
        var card = TestSupport.Node("div", classes: "card");
        var field = TestSupport.Node("input");
        card.Children = [field];
        field.IsFocused = true;

        var parentStyle = TestSupport.ResolveStyle(css, field);
        var style = TestSupport.ResolveStyle(css, card);

        Assert.Equal(0xFFFF0000u, style.BorderColorTop);
        Assert.Equal(0xFF000000u, parentStyle.ResolveBorderColor(parentStyle.BorderColorTop));
    }

    [Fact]
    public void EmptyIgnoresFoldedTextContent()
    {
        const string css = ":empty { color: #ff0000; }";

        var empty = TestSupport.Node("span");
        Assert.Equal(0xFFFF0000u, TestSupport.ResolveStyle(css, empty).Color);

        var text = TestSupport.Node("span", text: "visible");
        Assert.Equal(0xFF000000u, TestSupport.ResolveStyle(css, text).Color);
    }

    [Fact]
    public void CheckedDisabledAndLinkAreAttributeDriven()
    {
        const string css = "input:checked { color: #ff0000; }"
            + " input:disabled { background-color: #00ff00; }"
            + " input:enabled { background-color: #0000ff; }"
            + " a:link { color: #123456; }";

        var checkbox = TestSupport.Node("input");
        checkbox.SetAttribute("checked", "checked");
        Assert.Equal(0xFFFF0000u, TestSupport.ResolveStyle(css, checkbox).Color);

        var disabled = TestSupport.Node("input");
        disabled.SetAttribute("disabled", "disabled");
        Assert.Equal(0xFF00FF00u, TestSupport.ResolveStyle(css, disabled).BackgroundColor);

        var enabled = TestSupport.Node("input");
        Assert.Equal(0xFF0000FFu, TestSupport.ResolveStyle(css, enabled).BackgroundColor);

        var link = TestSupport.Node("a");
        link.SetAttribute("href", "/");
        Assert.Equal(0xFF123456u, TestSupport.ResolveStyle(css, link).Color);

        var plainAnchor = TestSupport.Node("a");
        Assert.Equal(0xFF000000u, TestSupport.ResolveStyle(css, plainAnchor).Color);
    }
}
