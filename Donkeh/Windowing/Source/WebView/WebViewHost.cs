using System.Drawing;
using CefSharp;
using CefSharp.Enums;
using CefSharp.OffScreen;
using CefSharp.Structs;
using Donkeh.UILab;
using Silk.NET.Input;
using CefCursorType = CefSharp.Enums.CursorType;
using CefMouseEvent = CefSharp.MouseEvent;
using CefRange = CefSharp.Structs.Range;
using CefRect = CefSharp.Structs.Rect;
using CefSize = System.Drawing.Size;

namespace Donkeh.Windowing;

/// <summary>
/// Creation options read from a <c>webview</c> element's attributes and layout.
/// </summary>
internal readonly record struct WebViewElementOptions(
    string Source,
    int Width,
    int Height);

/// <summary>
/// Owns one offscreen CEF browser per <c>webview</c> element, captures CPU
/// painted frames and uploads them into the window's image cache.
/// </summary>
/// <remarks>
/// <para>
/// CEF is initialized once on the thread that first creates a browser, and
/// shut down from <see cref="Dispose"/> on that same thread. Browser creation
/// is asynchronous; the element's ready handler fires the first frame after
/// the browser exists.
/// </para>
/// <para>
/// CEF paints BGRA pixels on its own UI thread. The render handler converts
/// them to RGBA and stores them in a per-element buffer. The frame thread then
/// uploads the most recent frame through
/// <see cref="ImageCache.UpdateDynamicTexture"/>, so the webview is drawn by
/// the same display-list path as images and video and keeps its z-index, clip,
/// opacity and transform.
/// </para>
/// </remarks>
internal sealed class WebViewHost : IDisposable
{
    private static readonly object CefInitLock = new();
    private static bool _cefInitialized;
    private static bool _cefInitFailed;

    /// <summary>
    /// True when CEF can be hosted. Android and browser hosts have their own
    /// native webview/DOM paths and never construct this host.
    /// </summary>
    public static bool IsSupported =>
        !OperatingSystem.IsAndroid()
        && !OperatingSystem.IsBrowser()
        && !OperatingSystem.IsIOS()
        && !OperatingSystem.IsMacCatalyst();

    private readonly object _sync = new();
    private readonly Dictionary<ElementNode, Track> _tracks = [];
    private readonly HashSet<ElementNode> _pending = [];
    private readonly HashSet<ElementNode> _readySignaled = [];
    private ElementNode? _focused;
    private bool _disposed;

    /// <summary>
    /// Gets or schedules a browser for an element. Returns the code-facing
    /// controller, or <see langword="null"/> while creation is pending or
    /// unavailable.
    /// </summary>
    public WebView? Ensure(ElementNode element, WebViewElementOptions options, string textureKey)
    {
        if (_disposed || !IsSupported || string.IsNullOrWhiteSpace(options.Source))
        {
            return null;
        }

        options = options with { Source = NormalizeSource(options.Source) };

        lock (_sync)
        {
            if (_tracks.TryGetValue(element, out var existing))
            {
                if (string.Equals(existing.Options.Source, options.Source, StringComparison.Ordinal))
                {
                    existing.Options = options;
                    return existing.Controller;
                }

                // The src attribute changed: replace the browser instead of
                // silently showing the previous document.
                RemoveLocked(element);
            }

            if (_pending.Contains(element))
            {
                return null;
            }

            _pending.Add(element);
        }

        if (!EnsureCef())
        {
            lock (_sync)
            {
                _pending.Remove(element);
            }

            return null;
        }

        Track? track = null;

        try
        {
            var browser = new ChromiumWebBrowser(
                options.Source,
                browserSettings: null,
                requestContext: null,
                automaticallyCreateBrowser: true,
                onAfterBrowserCreated: OnBrowserCreated,
                useLegacyRenderHandler: true);

            track = new Track(textureKey, browser, options);
            browser.RenderHandler = new WebViewRenderHandler(track);
            browser.Size = new CefSize(Math.Max(1, options.Width), Math.Max(1, options.Height));
        }
        catch
        {
            track?.Dispose();
            track = null;
        }

        lock (_sync)
        {
            var accepted = _pending.Remove(element);

            if (track is null || !accepted || _disposed)
            {
                if (track is not null)
                {
                    track.Dispose();
                }

                return null;
            }

            _tracks[element] = track;
            return track.Controller;
        }
    }

    /// <summary>
    /// Converts a plain path to a <c>file:</c> URI so local content can be
    /// loaded. Absolute and data URLs are passed through unchanged.
    /// </summary>
    private static string NormalizeSource(string source)
    {
        if (source.Contains("://", StringComparison.Ordinal)
            || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        try
        {
            return new Uri(Path.GetFullPath(source)).AbsoluteUri;
        }
        catch
        {
            return source;
        }
    }

    private static void OnBrowserCreated(IBrowser browser)
    {
        try
        {
            browser.GetHost().WindowlessFrameRate = 60;
        }
        catch
        {
            // Older CEF builds may reject the requested rate; the default is fine.
        }
    }

    /// <summary>
    /// Keeps an element's CEF viewport in sync with its layout box.
    /// </summary>
    public void SyncSize(ElementNode element, int width, int height)
    {
        Track? track;

        lock (_sync)
        {
            if (!_tracks.TryGetValue(element, out track))
            {
                return;
            }
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        if (track.Browser.Size.Width == width && track.Browser.Size.Height == height)
        {
            return;
        }

        try
        {
            track.Browser.Size = new CefSize(width, height);
        }
        catch
        {
            // The browser may have been disposed by a navigation.
        }
    }

    /// <summary>Gets the image-cache key receiving an element's frames.</summary>
    public string? GetTextureKey(ElementNode element)
    {
        lock (_sync)
        {
            return _tracks.TryGetValue(element, out var track) ? track.TextureKey : null;
        }
    }

    /// <summary>Uploads the most recent frame of every browser.</summary>
    /// <returns>Whether any browser produced a new frame.</returns>
    public bool UpdateFrames(ImageCache images)
    {
        Track[] tracks;

        lock (_sync)
        {
            if (_tracks.Count == 0)
            {
                return false;
            }

            tracks = [.. _tracks.Values];
        }

        var updated = false;

        foreach (var track in tracks)
        {
            lock (track.FrameSync)
            {
                if (!track.HasFrame)
                {
                    continue;
                }

                images.UpdateDynamicTexture(
                    track.TextureKey,
                    track.Pixels.AsSpan(0, track.Width * track.Height * 4),
                    track.Width,
                    track.Height);
                updated = true;
            }
        }

        return updated;
    }

    /// <summary>Claims the one-time ready notification for an element.</summary>
    public bool MarkReady(ElementNode element)
    {
        lock (_sync)
        {
            return _readySignaled.Add(element);
        }
    }

    /// <summary>Disposes browsers for elements that left the tree.</summary>
    public void Prune(HashSet<ElementNode> live)
    {
        List<ElementNode>? stale = null;

        lock (_sync)
        {
            foreach (var element in _tracks.Keys)
            {
                if (!live.Contains(element))
                {
                    (stale ??= []).Add(element);
                }
            }

            if (stale is not null)
            {
                foreach (var element in stale)
                {
                    RemoveLocked(element);
                }
            }
        }
    }

    /// <summary>
    /// Moves browser focus to <paramref name="element"/>, blurring the
    /// previously focused browser when needed.
    /// </summary>
    public void SetFocused(ElementNode? element)
    {
        ElementNode? previous;

        lock (_sync)
        {
            previous = _focused;
            _focused = element;
        }

        if (ReferenceEquals(previous, element))
        {
            return;
        }

        SendFocus(previous, false);
        SendFocus(element, true);
    }

    private void SendFocus(ElementNode? element, bool focus)
    {
        if (element is null)
        {
            return;
        }

        var track = GetTrack(element);
        if (track is null || !track.Browser.IsBrowserInitialized || track.Browser.IsDisposed)
        {
            return;
        }

        try
        {
            track.Browser.GetBrowser().GetHost().SendFocusEvent(focus);
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ input

    /// <summary>Forwards a pointer move to an element's browser.</summary>
    public bool SendMouseMove(ElementNode element, int x, int y, CefEventFlags modifiers, bool leave)
    {
        var host = GetHost(element);
        if (host is null)
        {
            return false;
        }

        try
        {
            host.SendMouseMoveEvent(new CefMouseEvent(x, y, modifiers), leave);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Forwards a mouse press or release to an element's browser.</summary>
    public bool SendMouseClick(
        ElementNode element,
        int x,
        int y,
        CefEventFlags modifiers,
        MouseButtonType button,
        bool up,
        int clickCount)
    {
        var host = GetHost(element);
        if (host is null)
        {
            return false;
        }

        try
        {
            host.SendMouseClickEvent(new CefMouseEvent(x, y, modifiers), button, up, clickCount);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Forwards a wheel event to an element's browser.</summary>
    public bool SendMouseWheel(
        ElementNode element,
        int x,
        int y,
        CefEventFlags modifiers,
        float vertical,
        float horizontal)
    {
        var host = GetHost(element);
        if (host is null)
        {
            return false;
        }

        try
        {
            host.SendMouseWheelEvent(
                new CefMouseEvent(x, y, modifiers),
                (int)(horizontal * WheelUnits),
                (int)(vertical * WheelUnits));
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const float WheelUnits = 120f;

    /// <summary>Forwards a key down/up event to an element's browser.</summary>
    public bool SendKey(
        ElementNode element,
        KeyEventType type,
        int virtualKey,
        CefEventFlags modifiers)
    {
        var host = GetHost(element);
        if (host is null || virtualKey == 0)
        {
            return false;
        }

        try
        {
            host.SendKeyEvent(new KeyEvent
            {
                Type = type,
                Modifiers = modifiers,
                WindowsKeyCode = virtualKey,
                NativeKeyCode = virtualKey,
                IsSystemKey = false,
                FocusOnEditableField = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Forwards a character to an element's browser.</summary>
    public bool SendChar(ElementNode element, char character, CefEventFlags modifiers)
    {
        var host = GetHost(element);
        if (host is null || char.IsControl(character))
        {
            return false;
        }

        try
        {
            host.SendKeyEvent(new KeyEvent
            {
                Type = KeyEventType.Char,
                Modifiers = modifiers,
                WindowsKeyCode = character,
                NativeKeyCode = character,
                IsSystemKey = false,
                FocusOnEditableField = true
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Maps a Silk.NET key to a Win32 virtual key code.</summary>
    public static int ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return 0x41 + ((int)key - (int)Key.A);
        }

        if (key >= Key.Number0 && key <= Key.Number9)
        {
            return 0x30 + ((int)key - (int)Key.Number0);
        }

        if (key >= Key.F1 && key <= Key.F12)
        {
            return 0x70 + ((int)key - (int)Key.F1);
        }

        return key switch
        {
            Key.Backspace => 0x08,
            Key.Tab => 0x09,
            Key.Enter => 0x0D,
            Key.ShiftLeft or Key.ShiftRight => 0x10,
            Key.ControlLeft or Key.ControlRight => 0x11,
            Key.AltLeft or Key.AltRight => 0x12,
            Key.Escape => 0x1B,
            Key.Space => 0x20,
            Key.PageUp => 0x21,
            Key.PageDown => 0x22,
            Key.End => 0x23,
            Key.Home => 0x24,
            Key.Left => 0x25,
            Key.Up => 0x26,
            Key.Right => 0x27,
            Key.Down => 0x28,
            Key.Insert => 0x2D,
            Key.Delete => 0x2E,
            _ => 0
        };
    }

    private Track? GetTrack(ElementNode element)
    {
        lock (_sync)
        {
            return _tracks.GetValueOrDefault(element);
        }
    }

    private IBrowserHost? GetHost(ElementNode element)
    {
        var track = GetTrack(element);
        if (track is null || track.Browser.IsDisposed || !track.Browser.IsBrowserInitialized)
        {
            return null;
        }

        try
        {
            return track.Browser.GetBrowser().GetHost();
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------- lifecycle

    private static bool EnsureCef()
    {
        lock (CefInitLock)
        {
            if (_cefInitialized)
            {
                return true;
            }

            if (_cefInitFailed)
            {
                return false;
            }

            try
            {
                if (Cef.IsInitialized != true)
                {
                    var settings = new CefSharp.OffScreen.CefSettings
                    {
                        LogSeverity = LogSeverity.Disable
                    };

                    if (!Cef.Initialize(settings))
                    {
                        _cefInitFailed = true;
                        return false;
                    }
                }

                _cefInitialized = true;
                return true;
            }
            catch
            {
                _cefInitFailed = true;
                return false;
            }
        }
    }

    private void RemoveLocked(ElementNode element)
    {
        _pending.Remove(element);
        _readySignaled.Remove(element);

        if (_tracks.Remove(element, out var track))
        {
            track.Dispose();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Track[] tracks;

        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _pending.Clear();
            _readySignaled.Clear();
            tracks = [.. _tracks.Values];
            _tracks.Clear();
        }

        foreach (var track in tracks)
        {
            track.Dispose();
        }

        // Cef.Shutdown must run on the same thread as Cef.Initialize. Both are
        // driven by the window's frame thread (Ensure/Sync and OnClosing).
        if (_cefInitialized)
        {
            try
            {
                Cef.Shutdown();
            }
            catch
            {
            }
            finally
            {
                _cefInitialized = false;
            }
        }
    }

    private sealed class Track : IDisposable
    {
        public Track(string textureKey, ChromiumWebBrowser browser, WebViewElementOptions options)
        {
            TextureKey = textureKey;
            Browser = browser;
            Options = options;
            Controller = new WebView(browser);
        }

        /// <summary>Guards the captured frame buffer between CEF and the renderer.</summary>
        public object FrameSync { get; } = new();

        /// <summary>BGRA pixels converted to RGBA, row-major, top-down.</summary>
        public byte[] Pixels { get; set; } = [];

        public int Width { get; set; }

        public int Height { get; set; }

        public bool HasFrame { get; set; }

        public string TextureKey { get; }

        public ChromiumWebBrowser Browser { get; }

        public WebView Controller { get; }

        public WebViewElementOptions Options { get; set; }

        public void Dispose()
        {
            try
            {
                Browser.Dispose();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// CEF render handler that converts each BGRA paint into the RGBA buffer
    /// consumed by <see cref="ImageCache"/>.
    /// </summary>
    private sealed class WebViewRenderHandler : IRenderHandler
    {
        private readonly Track _track;

        public WebViewRenderHandler(Track track) => _track = track;

        public ScreenInfo? GetScreenInfo() => null;

        public CefRect GetViewRect() =>
            new(0, 0, Math.Max(1, _track.Options.Width), Math.Max(1, _track.Options.Height));

        public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
        {
            screenX = 0;
            screenY = 0;
            return false;
        }

        public void OnAcceleratedPaint(PaintElementType type, CefRect dirtyRect, AcceleratedPaintInfo info)
        {
            // CPU OSR only: accelerated paint is intentionally not requested.
        }

        public void OnPaint(PaintElementType type, CefRect dirtyRect, IntPtr buffer, int width, int height)
        {
            if (type != PaintElementType.View
                || buffer == IntPtr.Zero
                || width <= 0
                || height <= 0)
            {
                return;
            }

            var length = width * height * 4;

            lock (_track.FrameSync)
            {
                if (_track.Pixels.Length != length)
                {
                    _track.Pixels = new byte[length];
                }

                unsafe
                {
                    var source = (byte*)buffer;
                    var target = _track.Pixels;

                    for (var i = 0; i < length; i += 4)
                    {
                        target[i] = source[i + 2];
                        target[i + 1] = source[i + 1];
                        target[i + 2] = source[i];
                        target[i + 3] = source[i + 3];
                    }
                }

                _track.Width = width;
                _track.Height = height;
                _track.HasFrame = true;
            }
        }

        public void OnCursorChange(IntPtr cursor, CefCursorType type, CursorInfo customCursorInfo)
        {
        }

        public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y) => false;

        public void UpdateDragCursor(DragOperationsMask operation)
        {
        }

        public void OnPopupShow(bool show)
        {
        }

        public void OnPopupSize(CefRect rect)
        {
        }

        public void OnImeCompositionRangeChanged(CefRange selectedRange, CefRect[] characterBounds)
        {
        }

        public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode)
        {
        }

        public void Dispose()
        {
        }
    }
}
