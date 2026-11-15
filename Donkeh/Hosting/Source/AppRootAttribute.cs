using Donkeh.Inspector;
using Donkeh.LayoutLab;
using Donkeh.Windowing;

namespace Donkeh.Hosting;

/// <summary>
/// Marks the root UI component of a Donkeh application and supplies its
/// window settings.
/// </summary>
/// <remarks>
/// <para>
/// Apply this attribute once to a public component with a parameterless
/// constructor. The framework discovers it and creates the platform entry point
/// for desktop, Android and browser hosts. The app no longer implements
/// <c>IDonkehApp</c> or writes platform-specific <c>Main</c> methods.
/// </para>
/// <para>
/// C# attribute arguments must be constants, so settings are written as named
/// properties rather than an object initializer:
/// </para>
/// <example>
/// <code>
/// [AppRoot(
///     Width = 1180,
///     Height = 760,
///     Title = "My app",
///     Frameless = true,
///     CornerRadius = 9f,
///     ClearColor = 0xFF101216)]
/// public sealed class App : Component
/// {
///     public override Node Render() => ...;
/// }
/// </code>
/// </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AppRootAttribute : Attribute
{
    /// <summary>Gets or sets the initial window width in CSS pixels.</summary>
    public int Width { get; set; } = 1280;

    /// <summary>Gets or sets the initial window height in CSS pixels.</summary>
    public int Height { get; set; } = 800;

    /// <summary>Gets or sets whether the window has no title bar or border.</summary>
    public bool Frameless { get; set; }

    /// <summary>Gets or sets whether the window opens full-screen.</summary>
    public bool FullScreen { get; set; }

    /// <summary>Gets or sets whether the window opens maximized.</summary>
    public bool Maximized { get; set; }

    /// <summary>Gets or sets the window title.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets whether vertical sync is enabled.</summary>
    public bool VSync { get; set; }

    /// <summary>Gets or sets the frame cap. Zero leaves the loop uncapped.</summary>
    public int TargetFramesPerSecond { get; set; } = 144;

    /// <summary>Gets or sets the packed <c>0xAARRGGBB</c> clear colour.</summary>
    public uint ClearColor { get; set; } = 0xFFFFFFFF;

    /// <summary>Gets or sets the requested MSAA sample count.</summary>
    public int Samples { get; set; } = 4;

    /// <summary>Gets or sets the renderer submission strategy.</summary>
    public RenderPath RenderPath { get; set; } = RenderPath.Auto;

    /// <summary>Gets or sets whether the native window can be resized.</summary>
    public bool Resizable { get; set; } = true;

    /// <summary>Gets or sets the device-to-CSS-pixel ratio. Zero detects it automatically.</summary>
    public float DpiScale { get; set; }

    /// <summary>Gets or sets whether the built-in inspector is available.</summary>
    public bool EnableDevTools { get; set; } = true;

    /// <summary>Gets or sets where the inspector opens.</summary>
    public DevToolsDock DevToolsPosition { get; set; } = DevToolsDock.Bottom;

    /// <summary>Gets or sets the minimum client width. Zero means no minimum.</summary>
    public int MinWidth { get; set; }

    /// <summary>Gets or sets the minimum client height. Zero means no minimum.</summary>
    public int MinHeight { get; set; }

    /// <summary>Gets or sets the maximum client width. Zero means no maximum.</summary>
    public int MaxWidth { get; set; }

    /// <summary>Gets or sets the maximum client height. Zero means no maximum.</summary>
    public int MaxHeight { get; set; }

    /// <summary>Gets or sets the window icon source.</summary>
    public string? Icon { get; set; }

    /// <summary>Gets or sets whether the window stays above other windows.</summary>
    public bool TopMost { get; set; }

    /// <summary>Gets or sets whether the framebuffer uses transparency.</summary>
    public bool Transparent { get; set; }

    /// <summary>
    /// Gets or sets a uniform native window corner radius in CSS pixels.
    /// </summary>
    /// <value>
    /// <see cref="float.NaN"/> (the default) derives the shape from CSS; zero
    /// keeps a frameless window square.
    /// </value>
    public float CornerRadius { get; set; } = float.NaN;

    /// <summary>Creates a <see cref="WindowSettings"/> value from the attribute.</summary>
    /// <returns>Settings suitable for any Donkeh host.</returns>
    public WindowSettings ToWindowSettings()
    {
        var settings = WindowSettings.Default;

        settings.Width = Width;
        settings.Height = Height;
        settings.Frameless = Frameless;
        settings.FullScreen = FullScreen;
        settings.Maximized = Maximized;
        settings.Title = string.IsNullOrWhiteSpace(Title) ? settings.Title : Title;
        settings.VSync = VSync;
        settings.TargetFramesPerSecond = TargetFramesPerSecond;
        settings.ClearColor = ClearColor;
        settings.Samples = Samples;
        settings.RenderPath = RenderPath;
        settings.Resizable = Resizable;
        settings.DpiScale = DpiScale;
        settings.EnableDevTools = EnableDevTools;
        settings.DevToolsPosition = DevToolsPosition;
        settings.MinWidth = MinWidth;
        settings.MinHeight = MinHeight;
        settings.MaxWidth = MaxWidth;
        settings.MaxHeight = MaxHeight;
        settings.Icon = Icon;
        settings.TopMost = TopMost;
        settings.Transparent = Transparent;
        settings.CornerRadius = float.IsNaN(CornerRadius)
            ? null
            : Donkeh.LayoutLab.CornerRadius.All(CornerRadius);

        return settings;
    }
}
