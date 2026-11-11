using Ignite.Inspector;

namespace Ignite.Windowing
{
    /// <summary>Creation options for an <see cref="IgniteWindow"/>.</summary>
    public struct WindowSettings
    {
        /// <summary>Creates settings with sensible desktop defaults.</summary>
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

        /// <summary>The logical window width in CSS pixels. Zero uses the default.</summary>
        public int Width;

        /// <summary>The logical window height in CSS pixels. Zero uses the default.</summary>
        public int Height;

        /// <summary>Remove the native title bar and border.</summary>
        public bool Frameless;

        /// <summary>Open the window full-screen.</summary>
        public bool FullScreen;

        /// <summary>The window title.</summary>
        public string? Title;

        /// <summary>Enable vertical sync. Off by default; frames are capped by <see cref="TargetFramesPerSecond"/>.</summary>
        public bool VSync;

        /// <summary>
        /// Maximum rendered frames per second. Zero leaves the loop uncapped.
        /// The default of 144 keeps the app responsive without burning a full
        /// core on frames nobody can see.
        /// </summary>
        public int TargetFramesPerSecond;

        /// <summary>The window background before the UI is painted.</summary>
        public uint ClearColor;

        /// <summary>The preferred number of MSAA samples. Zero disables multisampling.</summary>
        public int Samples;

        /// <summary>Whether the user can resize the window.</summary>
        public bool Resizable;

        /// <summary>
        /// Enables the built-in inspector. Press Ctrl+Shift+I or F12 to toggle it.
        /// </summary>
        public bool EnableDevTools;

        /// <summary>Where the inspector opens when it is first shown.</summary>
        public DevToolsDock DevToolsPosition;

        /// <summary>Builds the defaults used when a field is left at its zero value.</summary>
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
