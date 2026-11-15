namespace Donkeh.Inspector;

/// <summary>Identifies where the inspector is docked.</summary>
/// <remarks>
/// <para>
/// The enum lives in <c>Donkeh.Hosting</c> rather than the inspector assembly so
/// <see cref="Donkeh.Windowing.WindowSettings"/> can expose it without forcing
/// hosts to reference the inspector implementation.
/// </para>
/// <para>
/// <see cref="Bottom"/> is the zero value, so a freshly created
/// <c>DevToolsState</c> docks at the bottom until the host applies a saved
/// preference.
/// </para>
/// </remarks>
public enum DevToolsDock
{
    /// <summary>
    /// Dock the panel across the bottom of the app viewport. Numeric value 0.
    /// </summary>
    Bottom,

    /// <summary>
    /// Dock the panel along the right edge of the app viewport. Numeric value 1.
    /// </summary>
    Right,

    /// <summary>
    /// Host the panel in its own native window. Numeric value 2.
    /// </summary>
    /// <remarks>
    /// The window is created by the windowing layer on a background STA thread
    /// and shares the same <c>DevToolsState</c> instance.
    /// </remarks>
    Window
}
