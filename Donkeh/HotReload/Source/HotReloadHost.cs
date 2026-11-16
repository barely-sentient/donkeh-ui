using Donkeh.Windowing;

namespace Donkeh.HotReload;

/// <summary>
/// Development entry point for CSS hot reload.
/// </summary>
/// <remarks>
/// <para>
/// Generated projects reference this package only in the Debug configuration.
/// The desktop host discovers it by reflection and attaches it before the frame
/// loop starts, so no application code is needed. Release builds do not restore
/// the package, and neither this assembly nor the watcher appears in their output.
/// </para>
/// <para>
/// Custom hosts that do not use <c>AppRootHost</c> can call
/// <see cref="Attach"/> themselves. Set the <c>DONKEH_HOT_RELOAD</c> environment
/// variable to <c>0</c>, <c>false</c> or <c>off</c> to disable it.
/// </para>
/// </remarks>
public static class HotReloadHost
{
    /// <summary>
    /// Starts watching the CSS files declared through
    /// <c>[Stylesheet(...)]</c> for one window.
    /// </summary>
    /// <param name="window">The window whose cascade should reload.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="window"/> is <see langword="null"/>.
    /// </exception>
    /// <remarks>
    /// The call is cheap when the application has no resolvable source files: it
    /// scans declarations once, finds no files and attaches nothing. Calling it
    /// more than once for the same window attaches more than one watcher, so call
    /// it once per window.
    /// </remarks>
    public static void Attach(DonkehWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (IsDisabled())
        {
            return;
        }

        CssHotReload.TryCreate()?.Attach(window);
        ComponentHotReload.TryCreate()?.Attach(window);
    }

    private static bool IsDisabled()
    {
        var value = Environment.GetEnvironmentVariable("DONKEH_HOT_RELOAD");

        return value is not null
            && (string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));
    }
}
