using static Donkeh.UILab._;

namespace Donkeh.UILab.Tests;

public sealed class EventTests
{
    [Fact]
    public void TypedClickPropRegistersAnEventPayloadHandler()
    {
        ClickEvent? received = null;
        var button = H("button", new Props
        {
            OnClickEvent = click => received = click,
        });

        Assert.True(button.TryGetEventHandler("click", out var handler));

        var expected = new ClickEvent(10, 20, 3, 4);
        ((Action<ClickEvent>)handler!)(expected);

        Assert.Same(expected, received);
        Assert.Equal("click", received!.Type);
        Assert.Equal(10, received.ClientX);
        Assert.Equal(4, received.LocalY);
    }

    [Fact]
    public void TypedPropTakesPrecedenceOverLegacyProp()
    {
        var legacyCalls = 0;
        ClickEvent? received = null;
        var button = H("button", new Props
        {
            OnClick = () => legacyCalls++,
            OnClickEvent = click => received = click,
        });

        Assert.True(button.TryGetEventHandler("click", out var handler));
        Assert.IsType<Action<ClickEvent>>(handler);

        ((Action<ClickEvent>)handler!)(new ClickEvent());

        Assert.Equal(0, legacyCalls);
        Assert.NotNull(received);
    }

    [Fact]
    public void ElementHelpersExposeTypedOverloads()
    {
        ScrollEvent? received = null;
        var element = H("div").OnScroll(scroll => received = scroll);

        Assert.True(element.TryGetEventHandler("scroll", out var handler));

        var expected = new ScrollEvent(12, -48);
        ((Action<ScrollEvent>)handler!)(expected);

        Assert.Same(expected, received);
        Assert.Equal(12, received!.DeltaX);
        Assert.Equal(-48, received.DeltaY);
    }

    [Fact]
    public void KeyDownEventBuildsTheChordFromItsModifiers()
    {
        var keyDown = new KeyDownEvent("Z", KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal("Z", keyDown.Key);
        Assert.Equal("Ctrl+Shift+Z", keyDown.Chord);
        Assert.Equal("keydown", keyDown.Type);
        Assert.False(keyDown.Repeat);
    }

    [Fact]
    public void KeyDownEventCanBeMarkedAsAnAutoRepeat()
    {
        var keyDown = new KeyDownEvent("Left", repeat: true);

        Assert.True(keyDown.Repeat);
        Assert.Equal("Left", keyDown.Chord);
    }

    [Fact]
    public void BaseEventCanCarryADefaultPreventedFlag()
    {
        var uiEvent = new Event("custom");

        Assert.False(uiEvent.DefaultPrevented);
        uiEvent.PreventDefault();
        Assert.True(uiEvent.DefaultPrevented);
    }
}
