using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.Testing.Tests;

public sealed class ComponentTestHarnessTests
{
    [Fact]
    public void ClickRendersStateAndSettlesLayout()
    {
        var page = ComponentTest.Render(C<Counter>());

        Assert.Equal("Counter: 0", page.Find(".counter-btn").Text);

        page.Click(".counter-btn");

        Assert.Equal("Counter: 1", page.Find(".counter-btn").Text);
        Assert.Same(page.Root, page.Renderer!.Root);
    }

    [Fact]
    public void SelectorQueriesFindElementsByClassIdAttributeAndText()
    {
        var page = ComponentTest.Render(
            H("section", new Props { Id = "panel", ClassName = "card" },
                H("h1", "Title"),
                H("p", new Props { ClassName = "body", Role = "note" }, "Body copy")));

        Assert.Equal("Title", page.Find("#panel h1").Text);
        Assert.Equal("Body copy", page.Find(".card .body").Text);
        Assert.Equal("note", page.Find("[role='note']").GetAttribute("role"));
        Assert.Equal("p", page.FindByText("Body copy").Tag);
        Assert.Single(page.FindAll("p"));
    }

    [Fact]
    public void ParentHandlersReceiveBubbledEventsWithTheOriginalTarget()
    {
        ClickEvent? received = null;
        var page = ComponentTest.Render(
            H("div", new Props
            {
                ClassName = "outer",
                OnClickEvent = click => received = click,
            },
                H("button", new Props { ClassName = "inner" }, "Press")));

        page.Click(".inner");

        Assert.NotNull(received);
        Assert.True(received!.Target?.HasClass("inner"));
        Assert.True(received.CurrentTarget?.HasClass("outer"));
    }

    [Fact]
    public void HoverAndFocusUpdatePseudoClassStyles()
    {
        var page = ComponentTest.Render(
            H("div", new Props { ClassName = "target" }),
            options => options.Stylesheets.Add(
                ".target { width: 10px; height: 10px; background-color: #000000; }" +
                ".target:hover { width: 50px; }" +
                ".target:focus { height: 40px; background-color: #ff0000; }"));

        var target = page.Find(".target");
        Assert.Equal(10, target.Bounds.Width, 3);

        page.Hover(".target");
        Assert.Equal(50, target.Bounds.Width, 3);
        Assert.True(target.IsHovered);

        page.Focus(".target");
        Assert.Equal(40, target.Bounds.Height, 3);
        Assert.Equal(0xFFFF0000u, target.Style!.BackgroundColor);
        Assert.True(target.IsFocused);

        page.Unhover().Blur(".target");
        Assert.Equal(10, target.Bounds.Width, 3);
        Assert.Equal(10, target.Bounds.Height, 3);
    }

    [Fact]
    public void ScrollUpdatesTheOffsetAndDispatchesDeltas()
    {
        ScrollEvent? received = null;
        var page = ComponentTest.Render(
            H("div", new Props
            {
                ClassName = "scroll",
                OnScrollEvent = scroll => received = scroll,
            },
                H("div", new Props { ClassName = "content" })),
            options => options.Stylesheets.Add(
                ".scroll { width: 100px; height: 50px; overflow: auto; }" +
                ".content { height: 200px; }"));

        page.Scroll(".scroll", 0, 40);

        Assert.NotNull(received);
        Assert.Equal(40, received!.DeltaY, 3);
        Assert.Equal(40, page.Find(".scroll").Element.ScrollOffsetY, 3);
    }

    [Fact]
    public void InputAndTypeUpdateAControlledValue()
    {
        var page = ComponentTest.Render(C<ControlledInput>());

        page.Input(".field", "hello");
        Assert.Equal("hello", page.Find(".field").Value);
        Assert.Equal("value: hello", page.Find(".echo").Text);

        page.Type(".field", "!");
        Assert.Equal("hello!", page.Find(".field").Value);
        Assert.Equal("value: hello!", page.Find(".echo").Text);
    }

    [Fact]
    public void HandlerExceptionsAreNotSwallowed()
    {
        var page = ComponentTest.Render(
            H("button", new Props
            {
                ClassName = "failing",
                OnClick = () => throw new InvalidOperationException("boom"),
            }));

        var exception = Assert.Throws<InvalidOperationException>(() => page.Click(".failing"));
        Assert.Equal("boom", exception.Message);
    }

    [Fact]
    public void MissingQueriesThrowTheDedicatedException()
    {
        var page = ComponentTest.Render(H("div"));

        Assert.Null(page.TryFind(".missing"));
        Assert.Throws<ComponentQueryException>(() => page.Find(".missing"));
    }

    [Fact]
    public void UpdatePatchesTheRootComponentAndPreservesState()
    {
        var page = ComponentTest.Render<Greeting, GreetingProps>(
            new GreetingProps { Name = "Ada" });

        Assert.Equal("Hello Ada (0)", page.Find(".greeting").Text);

        page.Click(".greeting");
        Assert.Equal("Hello Ada (1)", page.Find(".greeting").Text);

        page.Update(C<Greeting, GreetingProps>(new GreetingProps { Name = "Grace" }));

        Assert.Equal("Hello Grace (1)", page.Find(".greeting").Text);
    }
}

public sealed class GreetingProps
{
    public string Name { get; init; } = string.Empty;
}

public sealed class Greeting : Component<GreetingProps>
{
    public override Node Render()
    {
        var (count, setCount) = State(0);

        return H("button", new Props { ClassName = "greeting" },
                Text($"Hello {Props.Name} ({count})"))
            .OnClick(() => setCount(count + 1));
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

public sealed class ControlledInput : Component
{
    public override Node Render()
    {
        var (value, setValue) = State(string.Empty);

        return H("div",
            H("input", new Props
            {
                ClassName = "field",
                Value = value,
                OnInputEvent = input => setValue(input.Value),
            }),
            H("span", new Props { ClassName = "echo" }, Text($"value: {value}")));
    }
}
