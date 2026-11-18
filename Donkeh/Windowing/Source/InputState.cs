using Donkeh.LayoutLab;
using Silk.NET.Input;

namespace Donkeh.Windowing;

/// <summary>
/// The modifier keys held during the most recent pointer interaction.
/// </summary>
/// <remarks>
/// UI event delegates intentionally carry only coordinates. Components that
/// need browser-style modifier gestures read this snapshot, which the window
/// refreshes before dispatching a press or a wheel event.
/// </remarks>
public static class InputState
{
    /// <summary>Gets whether Ctrl was held.</summary>
    public static bool Control { get; internal set; }

    /// <summary>Gets whether Shift was held.</summary>
    public static bool Shift { get; internal set; }

    /// <summary>Gets whether Alt was held.</summary>
    public static bool Alt { get; internal set; }

    /// <summary>Gets whether the left mouse button is down.</summary>
    public static bool Left { get; internal set; }

    /// <summary>Gets whether the middle mouse button is down.</summary>
    public static bool Middle { get; internal set; }

    /// <summary>Gets whether the right mouse button is down.</summary>
    public static bool Right { get; internal set; }

    /// <summary>Gets or sets a cursor to show for the current gesture.</summary>
    /// <remarks>
    /// A component that owns a drag can set this while the gesture is active,
    /// for example <see cref="Cursor.Grabbing"/> during a pan, instead of
    /// changing classes and forcing a style re-resolution. The host clears it
    /// when the gesture ends.
    /// </remarks>
    public static Cursor? CursorOverride { get; set; }

    /// <summary>Gets a counter that increments on every pointer press.</summary>
    /// <remarks>
    /// A gesture that ends while no pointer move is being dispatched (such as a
    /// middle-button pan) never observes the button going up. Comparing this
    /// value lets it notice that a new press started and discard its stale drag
    /// origin.
    /// </remarks>
    public static int PressSequence { get; private set; }

    /// <summary>Gets the pointer X in window CSS pixels.</summary>
    /// <remarks>
    /// Pointer handlers receive coordinates local to their own painted box, which
    /// makes them independent of layout. A drag that scrolls the element it is
    /// on cannot use those local coordinates for deltas, because they shift with
    /// the scroll; reading this window-space position instead keeps the gesture
    /// stable.
    /// </remarks>
    public static float PointerX { get; internal set; }

    /// <summary>Gets the pointer Y in window CSS pixels.</summary>
    public static float PointerY { get; internal set; }

    /// <summary>Records the pointer position in window CSS pixels.</summary>
    internal static void SetPointer(float x, float y)
    {
        PointerX = x;
        PointerY = y;
    }

    /// <summary>Records a mouse-button transition.</summary>
    internal static void SetButton(MouseButton button, bool down)
    {
        if (down)
        {
            PressSequence++;
        }

        switch (button)
        {
            case MouseButton.Left:
                Left = down;
                break;

            case MouseButton.Middle:
                Middle = down;
                break;

            case MouseButton.Right:
                Right = down;
                break;
        }
    }

    /// <summary>Refreshes the snapshot from the connected keyboards.</summary>
    internal static void Update(IInputContext? input)
    {
        if (input is null || input.Keyboards.Count == 0)
        {
            Control = false;
            Shift = false;
            Alt = false;
            return;
        }

        IKeyboard keyboard = input.Keyboards[0];
        Control = keyboard.IsKeyPressed(Key.ControlLeft) || keyboard.IsKeyPressed(Key.ControlRight);
        Shift = keyboard.IsKeyPressed(Key.ShiftLeft) || keyboard.IsKeyPressed(Key.ShiftRight);
        Alt = keyboard.IsKeyPressed(Key.AltLeft) || keyboard.IsKeyPressed(Key.AltRight);
    }
}
