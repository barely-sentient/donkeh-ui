using static Donkeh.UILab._;

namespace Donkeh.UILab.Tests;

public sealed class BrowserBridgeTests
{
    [Fact]
    public void EnumerateAttributesIncludesIdClassAndProps()
    {
        var element = H("input", new Props
        {
            Id = "name",
            ClassName = "field big",
            Type = "text",
            Value = "Ada",
            Placeholder = "Name",
            OnInput = _ => { },
        });

        var attributes = element.EnumerateAttributes()
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        Assert.Equal("name", attributes["id"]);
        Assert.Equal(
            new[] { "big", "field" },
            attributes["class"].Split(' ').OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal("text", attributes["type"]);
        Assert.Equal("Ada", attributes["value"]);
        Assert.Equal("Name", attributes["placeholder"]);
        Assert.Contains("input", element.EnumerateEventNames());
    }

    [Fact]
    public void AfterUpdateFiresForMountAndStateChanges()
    {
        var renderer = new Renderer();
        var updates = 0;
        renderer.AfterUpdate += () => updates++;

        var root = (ElementNode)renderer.Mount(C<Counter>());
        Assert.Equal(1, updates);

        Assert.True(root.TryGetEventHandler("click", out var handler));
        ((Action)handler!)();

        Assert.Equal(2, updates);
        Assert.Equal("Counter: 1", root.TextContent);
    }
}
