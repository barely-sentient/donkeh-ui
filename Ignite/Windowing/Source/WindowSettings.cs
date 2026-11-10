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
            VSync = true;
            ClearColor = 0xFFFFFFFF;
            Samples = 4;
            Resizable = true;
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

        /// <summary>Enable vertical sync. On by default for a tear-free frame loop.</summary>
        public bool VSync;

        /// <summary>The window background before the UI is painted.</summary>
        public uint ClearColor;

        /// <summary>The preferred number of MSAA samples. Zero disables multisampling.</summary>
        public int Samples;

        /// <summary>Whether the user can resize the window.</summary>
        public bool Resizable;

        /// <summary>Builds the defaults used when a field is left at its zero value.</summary>
        public static WindowSettings Default => new()
        {
            Width = 1280,
            Height = 800,
            Title = "Ignite",
            VSync = true,
            ClearColor = 0xFFFFFFFF,
            Samples = 4,
            Resizable = true
        };
    }
}
