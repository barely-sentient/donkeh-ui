using static Donkeh.UILab._;

namespace Donkeh.UILab.Tests;

public sealed class BuilderTests
{
    [Fact]
    public void H_InternsAttributesAndKeepsNestedChildren()
    {
        var tree = H("div", new Props { ClassName = "app" },
            H("div", new Props { ClassName = "topbar" }));

        var renderer = new Renderer();
        var live = (ElementNode)renderer.Mount(tree);

        Assert.Equal("div", live.Tag);
        Assert.True(live.HasClass("app"));
        Assert.Single(live.Children);
        Assert.True(((ElementNode)live.Children[0]).HasClass("topbar"));
    }

    [Fact]
    public void H_SupportsPropsAndEvents()
    {
        var tree = H("div", new Props
        {
            ClassName = "panel",
            Attributes = new Dictionary<string, string>
            {
                ["data-role"] = "main",
                ["aria-label"] = "Panel",
            },
        });

        Assert.True(tree.HasClass("panel"));
        Assert.True(tree.TryGetAttribute("data-role", out var role));
        Assert.Equal("main", role);
        Assert.True(tree.TryGetAttribute("aria-label", out var label));
        Assert.Equal("Panel", label);
    }

    [Fact]
    public void H_SupportsAnEmptyAttributeList()
    {
        var tree = H("div");
        Assert.Empty(tree.Children);
    }

    [Fact]
    public void Props_OnScrollRegistersScrollHandler()
    {
        var invoked = 0;
        var tree = H("div", new Props { OnScroll = () => invoked++ });

        Assert.True(tree.TryGetEventHandler("scroll", out var handler));

        ((Action)handler!)();

        Assert.Equal(1, invoked);
    }

    [Fact]
    public void TextOnlyChildrenAreFoldedIntoTextContent()
    {
        var renderer = new Renderer();
        var button = (ElementNode)renderer.Mount(
            H("button", new Props { ClassName = "counter-btn" }, Text("Counter: 0")));

        Assert.Equal("Counter: 0", button.TextContent);
        Assert.Empty(button.Children);
    }

    [Fact]
    public void MixedContentKeepsTextNodes()
    {
        var renderer = new Renderer();
        var live = (ElementNode)renderer.Mount(H("p", "Hello ", H("b", "world"), "!"));

        Assert.Null(live.TextContent);
        Assert.Equal(3, live.Children.Length);
        Assert.IsType<TextNode>(live.Children[0]);
        Assert.True(live.Children[0].IsTextNode);
        Assert.IsType<ElementNode>(live.Children[1]);
    }

    [Fact]
    public void FragmentsAreTransparentNodes()
    {
        var renderer = new Renderer();
        var fragment = (FragmentNode)renderer.Mount(
            F(H("span", "one"), H("span", "two")));

        Assert.True(fragment.IsFragment);
        Assert.Equal(2, fragment.Children.Length);
    }
}
