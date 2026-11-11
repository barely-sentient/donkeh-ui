using Ignite.Inspector;

namespace Ignite.Windowing
{
    /// <summary>Creation options for an <see cref="IgniteWindow"/>.</summary>
    /// <remarks>
    /// <para>
    /// This is a mutable struct, so it can be composed with an object initializer
    /// and copied into the window without heap allocation. The parameterless
    /// constructor and <see cref="Default"/> supply desktop-oriented defaults.
    /// </para>
    /// <para>
    /// <c>default(WindowSettings)</c> bypasses the constructor: some fields treat
    /// zero as "unset" and receive fallbacks, while booleans keep their false
    /// meaning. The result is therefore a fixed, non-resizable window with the
    /// inspector disabled and an uncapped frame rate, not the constructor defaults.
    /// Prefer <see cref="Default"/> or an explicit constructor call.
    /// </para>
    /// <para>
    /// Settings are copied when the window is constructed. Changing a copy
    /// afterwards has no effect on the live window, and some values are only
    /// applied when <see cref="IgniteWindow.Run(Ignite.UILab.Node, string)"/>
    /// creates the native window.
    /// </para>
    /// </remarks>
    /// <seealso cref="IgniteWindow"/>
    public struct WindowSettings
    {
        /// <summary>Creates settings with sensible desktop defaults.</summary>
        /// <remarks>
        /// The defaults are a 1280 by 800 resizable window titled
        /// <c>Ignite</c>, no VSync, a 144 FPS cap, opaque white clear colour,
        /// 4x MSAA, a frame border and the inspector enabled at the bottom.
        /// </remarks>
        public WindowSettings()
        {
            Width = 1280;
            Height = 800;
            Frameless = false;
            FullScreen = false;
            Title = "Ignite";
            VSync = false;
            TargetFramesPerSecond = 144;
            ClearColor = 0xFFFFFFFF;
            Samples = 4;
            Resizable = true;
            EnableDevTools = true;
            DevToolsPosition = DevToolsDock.Bottom;
        }

        /// <summary>
        /// The logical window width in CSS pixels. Non-positive values use the
        /// default.
        /// </summary>
        /// <value>
        /// The initial client width; values of zero or less fall back to 1280.
        /// </value>
        /// <remarks>
        /// The value is passed straight to the native window options, so it seeds
        /// the initial client size only; it does not constrain layout or the DOM.
        /// </remarks>
        public int Width;

        /// <summary>
        /// The logical window height in CSS pixels. Non-positive values use the
        /// default.
        /// </summary>
        /// <value>
        /// The initial client height; values of zero or less fall back to 800.
        /// </value>
        /// <remarks>
        /// Like <see cref="Width"/>, this only seeds the native window size.
        /// </remarks>
        public int Height;

        /// <summary>Remove the native title bar and border.</summary>
        /// <remarks>
        /// When set, the window is created with a hidden border and
        /// <see cref="Resizable"/> is ignored, because a frameless window has no
        /// native resize affordance.
        /// </remarks>
        public bool Frameless;

        /// <summary>Open the window full-screen.</summary>
        /// <remarks>
        /// The window is created in the platform's fullscreen state and the
        /// initial <see cref="Width"/> and <see cref="Height"/> are effectively
        /// overridden by the display size.
        /// </remarks>
        public bool FullScreen;

        /// <summary>The window title.</summary>
        /// <remarks>
        /// <see langword="null"/> or whitespace is replaced with <c>Ignite</c>.
        /// </remarks>
        public string? Title;

        /// <summary>
        /// Enable vertical sync. Off by default; frames are capped by
        /// <see cref="TargetFramesPerSecond"/>.
        /// </summary>
        /// <remarks>
        /// VSync and the frame cap can both be active; the platform's present
        /// interval usually dominates. The setting is applied when the native
        /// window is created and cannot be changed on a live window.
        /// </remarks>
        public bool VSync;

        /// <summary>
        /// Maximum rendered frames per second. Zero leaves the loop uncapped.
        /// The default of 144 keeps the app responsive without burning a full
        /// core on frames nobody can see.
        /// </summary>
        /// <value>
        /// A positive frame cap, or zero/negative for no artificial limit.
        /// </value>
        /// <remarks>
        /// The value is forwarded to the native window's frame limiter. It does
        /// not affect layout or animation timing, which use the real elapsed time.
        /// </remarks>
        public int TargetFramesPerSecond;

        /// <summary>The window background before the UI is painted.</summary>
        /// <value>
        /// A packed <c>0xAARRGGBB</c> colour. Zero is treated as "unset" and
        /// resolves to opaque white.
        /// </value>
        /// <remarks>
        /// Because zero is a sentinel, transparent black cannot be requested
        /// through this field. The colour is cleared every frame before the
        /// instruction list is drawn.
        /// </remarks>
        public uint ClearColor;

        /// <summary>The preferred number of MSAA samples. Zero disables multisampling.</summary>
        /// <value>
        /// A positive sample count, or zero/negative to omit the request.
        /// </value>
        /// <remarks>
        /// The driver may round the request to a supported count or ignore it
        /// entirely. Higher counts improve curve and glyph-edge quality at the
        /// cost of memory and fill rate; the window does not verify the result.
        /// </remarks>
        public int Samples;

        /// <summary>Whether the user can resize the window.</summary>
        /// <remarks>
        /// Ignored when <see cref="Frameless"/> is set, since the hidden border
        /// takes over the resize behaviour.
        /// </remarks>
        public bool Resizable;

        /// <summary>
        /// Enables the built-in inspector. Press Ctrl+Shift+I or F12 to toggle it.
        /// </summary>
        /// <remarks>
        /// Disabling this removes the inspector key handling, skips its stylesheet
        /// and layout engine entirely, and makes the show/hide/toggle methods
        /// no-ops. The inspector is decoration only; the app tree is unaffected.
        /// </remarks>
        public bool EnableDevTools;

        /// <summary>Where the inspector opens when it is first shown.</summary>
        /// <remarks>
        /// Copied into <see cref="DevToolsState.Dock"/> at construction time only.
        /// Change that live state afterwards to move the panel; editing this field
        /// later has no effect.
        /// </remarks>
        public DevToolsDock DevToolsPosition;

        /// <summary>
        /// Gets a new settings value containing the desktop defaults.
        /// </summary>
        /// <value>
        /// A new settings value with the same initial values as the parameterless
        /// constructor.
        /// </value>
        /// <remarks>
        /// This is a property rather than a cached field, so every access returns a
        /// fresh struct value (a stack copy, not a heap allocation, and not a
        /// singleton). Use it when composing settings from a base value:
        /// <c>WindowSettings.Default with { Title = "App" }</c>.
        /// </remarks>
        public static WindowSettings Default => new()
        {
            Width = 1280,
            Height = 800,
            Title = "Ignite",
            VSync = false,
            TargetFramesPerSecond = 144,
            ClearColor = 0xFFFFFFFF,
            Samples = 4,
            Resizable = true,
            EnableDevTools = true,
            DevToolsPosition = DevToolsDock.Bottom
        };
    }
}
