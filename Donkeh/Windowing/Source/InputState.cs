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

    /// <summary>Records a mouse-button transition.</summary>
    internal static void SetButton(MouseButton button, bool down)
    {
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
