using static Donkeh.UILab._;

namespace Donkeh.UILab.Tests;

public sealed class ComponentTests
{
    [Fact]
    public void StateUpdatePatchesTheLiveNodeInPlace()
    {
        var renderer = new Renderer();
        var button = (ElementNode)renderer.Mount(C<Counter>());

        Assert.Equal("Counter: 0", button.TextContent);
        Assert.True(button.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();

        Assert.Equal("Counter: 1", button.TextContent);
        Assert.Same(button, renderer.Root);
    }

    [Fact]
    public void KeyedReorderReusesElementInstances()
    {
        var renderer = new Renderer();
        var list = (ElementNode)renderer.Mount(C<KeyedList>());
        var items = (FragmentNode)list.Children[0];
        var first = (ElementNode)items.Children[0];
        var second = (ElementNode)items.Children[1];

        var button = (ElementNode)list.Children[1];
        Assert.True(button.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();

        Assert.Equal(2, items.Children.Length);
        Assert.Same(second, items.Children[0]);
        Assert.Same(first, items.Children[1]);
    }

    [Fact]
    public void EffectRunsAfterCommitAndCanUpdateState()
    {
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(C<EffectComponent>());

        Assert.Equal("effect: 1", root.TextContent);
    }

    [Fact]
    public void ContextProviderReachesConsumer()
    {
        var renderer = new Renderer();
        var provider = (FragmentNode)renderer.Mount(
            Provider(Contexts.Theme, "dark", C<ContextConsumer>()));

        var consumer = (ElementNode)provider.Children[0];

        Assert.Equal("dark", consumer.TextContent);
    }

    [Fact]
    public void NoOpStateUpdateDoesNotBumpRevision()
    {
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(C<Stable>());
        var revision = root.Revision;

        Assert.True(root.TryGetEventHandler("click", out var handler));
        ((Action)handler!)();

        Assert.Equal(revision, root.Revision);
    }

    [Fact]
    public void UnchangedChildrenKeepTheirRevision()
    {
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(C<StableChild>());
        var fixedChild = (ElementNode)root.Children[0];
        var revision = fixedChild.Revision;

        var button = (ElementNode)root.Children[1];
        Assert.True(button.TryGetEventHandler("click", out var handler));
        ((Action)handler!)();

        Assert.Same(fixedChild, root.Children[0]);
        Assert.Equal(revision, fixedChild.Revision);
        Assert.Equal("tick 1", button.TextContent);
    }

    [Fact]
    public void UnkeyedInsertUpdatesTheRenderedList()
    {
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(C<UnkeyedList>());
        var button = (ElementNode)root.Children[1];

        Assert.Equal(2, ((FragmentNode)root.Children[0]).Children.Length);
        Assert.True(button.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();

        var items = (FragmentNode)root.Children[0];
        Assert.Equal(3, items.Children.Length);
        Assert.Equal("new", ((ElementNode)items.Children[0]).TextContent);
    }

    [Fact]
    public void StateWithUpdateUsesTheCurrentValue()
    {
        var renderer = new Renderer();
        var root = (ElementNode)renderer.Mount(C<Updater>());

        Assert.Equal("value: 0", root.TextContent);
        Assert.True(root.TryGetEventHandler("click", out var handler));

        ((Action)handler!)();
        ((Action)handler!)();

        Assert.Equal("value: 2", root.TextContent);
    }
}

public sealed class Counter : Component
{
    public override Node Render()
    {
        var (counter, setCounter) = State(0);

        return H("button", new Props { ClassName = "counter-btn" },
            Text($"Counter: {counter}"))
            .OnClick(() => setCounter(counter + 1));
    }
}

public sealed class KeyedList : Component
{
    public override Node Render()
    {
        var (reversed, setReversed) = State(false);
        var items = reversed ? new[] { "B", "A" } : new[] { "A", "B" };

        return H("div", new Props { ClassName = "list" },
            Each(items, item => H("div", new Props { ClassName = "item" }, Text(item)).Key(item)),
            H("button", new Props { ClassName = "reorder" }, Text("reorder"))
                .OnClick(() => setReversed(!reversed)));
    }
}

public sealed class EffectComponent : Component
{
    public override Node Render()
    {
        var (count, setCount) = State(0);

        Effect(() => setCount(1), []);

        return H("div", Text($"effect: {count}"));
    }
}

public sealed class ContextConsumer : Component
{
    public override Node Render()
    {
        var theme = UseContext(Contexts.Theme);
        return H("div", Text(theme));
    }
}

public static class Contexts
{
    public static readonly Context<string> Theme = new("default");
}

public sealed class Stable : Component
{
    public override Node Render()
    {
        var (value, setValue) = State(5);

        return H("div", new Props { ClassName = "stable" }, Text($"value: {value}"))
            .OnClick(() => setValue(5));
    }
}

public sealed class StableChild : Component
{
    public override Node Render()
    {
        var (tick, setTick) = State(0);

        return H("div",
            H("span", new Props { ClassName = "fixed" }, Text("fixed")),
            H("button", Text($"tick {tick}")).OnClick(() => setTick(tick + 1)));
    }
}

public sealed class Updater : Component
{
    public override Node Render()
    {
        var (value, _, update) = StateWithUpdate(0);

        return H("div", Text($"value: {value}"))
            .OnClick(() => update(current => current + 1));
    }
}

public sealed class UnkeyedList : Component
{
    public override Node Render()
    {
        var (inserted, setInserted) = State(false);
        var items = inserted ? new[] { "new", "A", "B" } : new[] { "A", "B" };

        return H("div",
            Each(items, item => H("div", Text(item))),
            H("button", Text("insert")).OnClick(() => setInserted(true)));
    }
}
