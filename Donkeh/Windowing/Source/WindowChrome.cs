namespace Donkeh.Windowing;

/// <summary>
/// Window actions for applications that draw their own title bar on a frameless
/// native window.
/// </summary>
/// <remarks>
/// <para>
/// The host attaches the active <see cref="DonkehWindow"/> while its frame loop
/// runs. Calls made before the window exists, after it closes, or on a host
/// without a native desktop window are ignored.
/// </para>
/// <para>
/// Pair these actions with CSS <c>app-region: drag</c> on the title bar and
/// <c>app-region: no-drag</c> on the controls. The host turns drag regions into
/// native window movement and leaves no-drag regions to normal input.
/// </para>
/// </remarks>
public static class WindowChrome
{
    private static DonkehWindow? _window;

    /// <summary>Gets whether the current window is maximized.</summary>
    public static bool IsMaximized => _window?.IsWindowMaximized == true;

    /// <summary>Minimizes the current window.</summary>
    public static void Minimize() => _window?.MinimizeWindow();

    /// <summary>Maximizes the current window, or restores it when already maximized.</summary>
    public static void ToggleMaximize() => _window?.ToggleMaximizeWindow();

    /// <summary>Closes the current window.</summary>
    public static void Close() => _window?.CloseWindow();

    /// <summary>Binds the window whose chrome is currently interactive.</summary>
    /// <param name="window">The running host window.</param>
    internal static void Attach(DonkehWindow window) => _window = window;

    /// <summary>Clears a window binding when it closes or is replaced.</summary>
    /// <param name="window">The host window being detached.</param>
    internal static void Detach(DonkehWindow window)
    {
        if (ReferenceEquals(_window, window))
        {
            _window = null;
        }
    }
}
