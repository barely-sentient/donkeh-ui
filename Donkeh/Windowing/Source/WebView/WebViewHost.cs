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
    string LoadingPage,
    int Width,
    int Height);

/// <summary>
/// Owns one offscreen CEF host per <c>webview</c> element, with one or more
/// browser tabs, and uploads painted frames into the window's image cache.
/// </summary>
/// <remarks>
/// CEF is initialized once on the thread that first creates a browser, and
/// shut down from <see cref="Dispose"/> on that same thread. Custom schemes
/// are registered from <see cref="WebViewOptions"/> immediately before
/// initialization, so applications configure them through
/// <see cref="WebViewOptions.Configuring"/>.
/// </remarks>
internal sealed class WebViewHost : IDisposable
{
    private static readonly object CefInitLock = new();
    private static bool _cefInitialized;
    private static bool _cefInitFailed;

    public static bool IsSupported =>
        !OperatingSystem.IsAndroid()
        && !OperatingSystem.IsBrowser()
        && !OperatingSystem.IsIOS()
        && !OperatingSystem.IsMacCatalyst();

    private readonly object _sync = new();
    private readonly Dictionary<ElementNode, WebViewGroup> _groups = [];
    private readonly HashSet<ElementNode> _pending = [];
    private readonly HashSet<ElementNode> _readySignaled = [];
    private ElementNode? _focused;
    private bool _disposed;

    /// <summary>Gets or schedules browsers for an element.</summary>
    public WebView? Ensure(ElementNode element, WebViewElementOptions options, string textureKey)
    {
        if (_disposed || !IsSupported || string.IsNullOrWhiteSpace(options.Source))
        {
            return null;
        }

        options = options with { Source = NormalizeSource(options.Source) };

        WebViewGroup? group = null;

        lock (_sync)
        {
            if (_groups.TryGetValue(element, out var existing))
            {
                if (string.Equals(existing.Options.Source, options.Source, StringComparison.Ordinal))
                {
                    existing.Options = options;
                    return existing.Controller;
                }

                RemoveLocked(element);
            }

            if (_pending.Contains(element)) return null;
            _pending.Add(element);
        }

        var configured = WebViewOptions.Current;

        if (!EnsureCef())
        {
            lock (_sync) _pending.Remove(element);
            return null;
        }

        try
        {
            group = new WebViewGroup(this, element, options, textureKey);
            group.Manager.TabFactory = url => CreateTab(group, url);
            group.Manager.TabDestroyer = tab => DestroyTab(group, tab);
            group.Manager.TabActivator = tab => ActivateTab(group, tab);

            var first = CreateTab(group, options.Source);
            if (first is null) throw new InvalidOperationException("Could not create the webview browser.");

            ActivateTab(group, first);
        }
        catch
        {
            group?.Dispose();
            lock (_sync) _pending.Remove(element);
            return null;
        }

        lock (_sync)
        {
            if (_disposed)
            {
                group.Dispose();
                return null;
            }

            _groups[element] = group;
        }

        try { configured.OnWebViewReady?.Invoke(group.Controller); }
        catch { }

        return group.Controller;
    }

    /// <summary>Converts a plain path to a <c>file:</c> URI; passes URLs through.</summary>
    private static string NormalizeSource(string source)
    {
        if (source.Contains("://", StringComparison.Ordinal)
            || source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || source.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
        {
            return source;
        }

        try { return new Uri(Path.GetFullPath(source)).AbsoluteUri; }
        catch { return source; }
    }

    private static void OnBrowserCreated(IBrowser browser, WebViewTabTrack track)
    {
        try { browser.GetHost().WindowlessFrameRate = 60; }
        catch { }

        try
        {
            if (!string.Equals(track.TargetUrl, "about:blank", StringComparison.OrdinalIgnoreCase))
            {
                browser.MainFrame?.LoadUrl(track.TargetUrl);
            }
        }
        catch
        {
            // The browser may have navigated before the callback ran.
        }
    }

    private WebViewTab? CreateTab(WebViewGroup group, string url)
    {
        if (_disposed || string.IsNullOrWhiteSpace(url)) return null;

        var track = new WebViewTabTrack(this, group, NormalizeSource(url));
        WebViewTab? tab = null;
        var loading = string.IsNullOrWhiteSpace(group.Options.LoadingPage)
            ? "about:blank"
            : NormalizeSource(group.Options.LoadingPage);

        try
        {
            var browser = new ChromiumWebBrowser(
                loading,
                browserSettings: null,
                requestContext: null,
                // Defer creation until bindings/handlers are installed: CefSharp
                // serializes legacy bound objects into extraInfo at creation time.
                automaticallyCreateBrowser: false,
                onAfterBrowserCreated: b => OnBrowserCreated(b, track),
                useLegacyRenderHandler: true);

            track.Attach(browser);
            track.Width = Math.Max(1, group.Options.Width);
            track.Height = Math.Max(1, group.Options.Height);
            browser.RenderHandler = new WebViewRenderHandler(track);

            tab = new WebViewTab(Guid.NewGuid(), track);
            track.Tab = tab;
            var runtime = new WebViewPluginRuntime(browser, track, tab);
            track.Runtime = runtime;
            track.Plugins = runtime.Context;
            tab.View.PluginContext = runtime.Context;

            var previousRenderHandler = browser.RenderProcessMessageHandler;
            browser.RenderProcessMessageHandler =
                new WebViewRenderProcessMessageHandler(previousRenderHandler, runtime);
            browser.LoadHandler = new WebViewLoadHandler(track, runtime);
            browser.Size = new CefSize(Math.Max(1, group.Options.Width), Math.Max(1, group.Options.Height));
            browser.CreateBrowser();
        }
        catch
        {
            track.DisposeBrowser();
            return null;
        }

        group.Tracks.Add(track);
        group.Manager.Attach(tab!);

        try { WebViewOptions.Current.OnTabOpened?.Invoke(tab!); }
        catch { }

        return tab;
    }

    private void DestroyTab(WebViewGroup group, WebViewTab tab)
    {
        if (tab.Bridge is not WebViewTabTrack track) return;

        group.Tracks.Remove(track);
        track.DisposeBrowser();

        try { WebViewOptions.Current.OnTabClosed?.Invoke(tab); }
        catch { }
    }

    private void ActivateTab(WebViewGroup group, WebViewTab tab)
    {
        if (tab.Bridge is not WebViewTabTrack track) return;

        group.Manager.SetActive(tab);
        SetFocused(ReferenceEquals(_focused, group.Element) ? group.Element : null);
    }

    /// <summary>Keeps every tab's CEF viewport in sync with its layout box.</summary>
    public void SyncSize(ElementNode element, int width, int height)
    {
        WebViewGroup? group;

        lock (_sync)
        {
            if (!_groups.TryGetValue(element, out group)) return;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);

        group.Options = group.Options with { Width = width, Height = height };

        foreach (var track in group.Tracks)
        {
            track.Width = width;
            track.Height = height;

            if (track.Browser is null) continue;

            if (track.Browser.Size.Width == width && track.Browser.Size.Height == height) continue;

            try { track.Browser.Size = new CefSize(width, height); }
            catch { }
        }
    }

    /// <summary>Gets the image-cache key for the element's visible tab.</summary>
    public string? GetTextureKey(ElementNode element)
    {
        lock (_sync)
        {
            if (!_groups.TryGetValue(element, out var group)) return null;
            return group.Active?.TextureKey;
        }
    }

    /// <summary>Uploads the most recent frame of every tab.</summary>
    public bool UpdateFrames(ImageCache images)
    {
        WebViewGroup[] groups;

        lock (_sync)
        {
            if (_groups.Count == 0) return false;
            groups = [.. _groups.Values];
        }

        var updated = false;

        foreach (var group in groups)
        {
            foreach (var track in group.Tracks)
            {
                lock (track.FrameSync)
                {
                    if (!track.HasFrame) continue;

                    images.UpdateDynamicTexture(
                        track.TextureKey,
                        track.Pixels.AsSpan(0, track.Width * track.Height * 4),
                        track.Width,
                        track.Height);
                    updated = true;
                }
            }
        }

        return updated;
    }

    /// <summary>Claims the one-time ready notification for an element.</summary>
    public bool MarkReady(ElementNode element)
    {
        lock (_sync) return _readySignaled.Add(element);
    }

    /// <summary>Disposes browsers for elements that left the tree.</summary>
    public void Prune(HashSet<ElementNode> live)
    {
        List<ElementNode>? stale = null;

        lock (_sync)
        {
            foreach (var element in _groups.Keys)
            {
                if (!live.Contains(element)) (stale ??= []).Add(element);
            }

            if (stale is not null)
            {
                foreach (var element in stale) RemoveLocked(element);
            }
        }
    }

    /// <summary>Moves browser focus to an element's visible tab.</summary>
    public void SetFocused(ElementNode? element)
    {
        ElementNode? previous;

        lock (_sync)
        {
            previous = _focused;
            _focused = element;
        }

        if (ReferenceEquals(previous, element)) return;

        SendFocus(previous, false);
        SendFocus(element, true);
    }

    private void SendFocus(ElementNode? element, bool focus)
    {
        if (element is null) return;

        var track = GetActiveTrack(element);
        if (track?.Browser is null || track.Browser.IsDisposed || !track.Browser.IsBrowserInitialized) return;

        try { track.Browser.GetBrowser().GetHost().SendFocusEvent(focus); }
        catch { }
    }

    // ------------------------------------------------------------------ input

    public bool SendMouseMove(ElementNode element, int x, int y, CefEventFlags modifiers, bool leave)
    {
        var host = GetHost(element);
        if (host is null) return false;

        try
        {
            host.SendMouseMoveEvent(new CefMouseEvent(x, y, modifiers), leave);
            return true;
        }
        catch { return false; }
    }

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
        if (host is null) return false;

        try
        {
            host.SendMouseClickEvent(new CefMouseEvent(x, y, modifiers), button, up, clickCount);
            return true;
        }
        catch { return false; }
    }

    public bool SendMouseWheel(
        ElementNode element,
        int x,
        int y,
        CefEventFlags modifiers,
        float vertical,
        float horizontal)
    {
        var host = GetHost(element);
        if (host is null) return false;

        try
        {
            host.SendMouseWheelEvent(
                new CefMouseEvent(x, y, modifiers),
                (int)(horizontal * WheelUnits),
                (int)(vertical * WheelUnits));
            return true;
        }
        catch { return false; }
    }

    private const float WheelUnits = 120f;

    public bool SendKey(ElementNode element, KeyEventType type, int virtualKey, CefEventFlags modifiers)
    {
        var host = GetHost(element);
        if (host is null || virtualKey == 0) return false;

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
        catch { return false; }
    }

    public bool SendChar(ElementNode element, char character, CefEventFlags modifiers)
    {
        var host = GetHost(element);
        if (host is null || char.IsControl(character)) return false;

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
        catch { return false; }
    }

    /// <summary>Maps a Silk.NET key to a Win32 virtual key code.</summary>
    public static int ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z) return 0x41 + ((int)key - (int)Key.A);
        if (key >= Key.Number0 && key <= Key.Number9) return 0x30 + ((int)key - (int)Key.Number0);
        if (key >= Key.F1 && key <= Key.F12) return 0x70 + ((int)key - (int)Key.F1);

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

    private WebViewTabTrack? GetActiveTrack(ElementNode element)
    {
        lock (_sync)
        {
            if (!_groups.TryGetValue(element, out var group)) return null;
            return group.Active;
        }
    }

    private IBrowserHost? GetHost(ElementNode element)
    {
        var track = GetActiveTrack(element);
        if (track?.Browser is null || track.Browser.IsDisposed || !track.Browser.IsBrowserInitialized)
        {
            return null;
        }

        try { return track.Browser.GetBrowser().GetHost(); }
        catch { return null; }
    }

    // ------------------------------------------------------------- lifecycle

    private static bool EnsureCef()
    {
        lock (CefInitLock)
        {
            if (_cefInitialized) return true;
            if (_cefInitFailed) return false;

            try
            {
                if (Cef.IsInitialized != true)
                {
                    // Give the application its one chance to configure schemes,
                    // pages and URL mappings before CEF locks them down.
                    WebViewOptions.RaiseConfiguring();
                    var configured = WebViewOptions.Current;

                    var settings = new CefSharp.OffScreen.CefSettings
                    {
                        LogSeverity = LogSeverity.Disable
                    };

                    foreach (var scheme in configured.GetRegisteredSchemes())
                    {
                        settings.RegisterScheme(new CefCustomScheme(
                            scheme,
                            SchemeOptions.Standard | SchemeOptions.Secure | SchemeOptions.CorsEnabled)
                        {
                            SchemeHandlerFactory = new WebViewSchemeHandlerFactory(configured)
                        });
                    }

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

        if (_groups.Remove(element, out var group)) group.Dispose();
    }

    public void Dispose()
    {
        WebViewGroup[] groups;

        lock (_sync)
        {
            if (_disposed) return;

            _disposed = true;
            _pending.Clear();
            _readySignaled.Clear();
            groups = [.. _groups.Values];
            _groups.Clear();
        }

        foreach (var group in groups) group.Dispose();

        // Cef.Shutdown must run on the same thread as Cef.Initialize.
        if (_cefInitialized)
        {
            try { Cef.Shutdown(); }
            catch { }
            finally { _cefInitialized = false; }
        }
    }

    /// <summary>All browsers and tab state belonging to one element.</summary>
    internal sealed class WebViewGroup : IDisposable
    {
        public WebViewGroup(
            WebViewHost host,
            ElementNode element,
            WebViewElementOptions options,
            string textureKeyBase)
        {
            Host = host;
            Element = element;
            Options = options;
            TextureKeyBase = textureKeyBase;
            Controller = new WebView(Manager);
        }

        public WebViewHost Host { get; }
        public ElementNode Element { get; }
        public WebViewElementOptions Options { get; set; }
        public string TextureKeyBase { get; }
        public WebViewTabManager Manager { get; } = new();
        public List<WebViewTabTrack> Tracks { get; } = [];
        public WebView Controller { get; }
        public WebViewTabTrack? Active => Manager.Active?.Bridge as WebViewTabTrack;

        public void Dispose()
        {
            foreach (var track in Tracks.ToArray()) track.DisposeBrowser();
            Tracks.Clear();
        }
    }

    /// <summary>One tab's browser, frame buffer and engine-neutral bridge.</summary>
    internal sealed class WebViewTabTrack : IWebViewTabBridge, IDisposable
    {
        private readonly WebViewHost _host;
        private readonly WebViewGroup _group;
        private string _title = "";
        private bool _isLoading;
        private bool _canGoBack;
        private bool _canGoForward;

        public WebViewTabTrack(WebViewHost host, WebViewGroup group, string targetUrl)
        {
            _host = host;
            _group = group;
            TargetUrl = targetUrl;
            TextureKey = group.TextureKeyBase + ":" + Guid.NewGuid().ToString("N");
        }

        public object FrameSync { get; } = new();
        public byte[] Pixels { get; set; } = [];
        public int Width { get; set; }
        public int Height { get; set; }
        public bool HasFrame { get; set; }
        public string TextureKey { get; }
        public string TargetUrl { get; }
        public ChromiumWebBrowser? Browser { get; private set; }
        public WebViewTab? Tab { get; set; }
        public WebViewPluginRuntime? Runtime { get; set; }
        public WebViewPluginContext? Plugins { get; set; }
        public WebView View => _view ??= new WebView(Browser!);
        private WebView? _view;

        public void Attach(ChromiumWebBrowser browser) => Browser = browser;

        public void DisposeBrowser()
        {
            Runtime?.Dispose();
            Runtime = null;

            try { Browser?.Dispose(); }
            catch { }
            Browser = null;
        }

        public void Dispose() => DisposeBrowser();

        public string Address => Browser?.Address ?? "";

        public string Title
        {
            get => _title;
            set
            {
                _title = value ?? "";
                Tab?.RaiseStateChanged();
            }
        }

        public bool IsLoading => _isLoading;
        public bool CanGoBack => _canGoBack;
        public bool CanGoForward => _canGoForward;
        public bool IsActive => ReferenceEquals(_group.Manager.Active, Tab);

        public void UpdateState(bool canGoBack, bool canGoForward, bool isLoading)
        {
            _canGoBack = canGoBack;
            _canGoForward = canGoForward;
            _isLoading = isLoading;
            Tab?.RaiseStateChanged();
        }

        public void Load(string url)
        {
            if (Browser is null || Browser.IsDisposed || string.IsNullOrWhiteSpace(url)) return;

            var source = url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                || url.Contains("://", StringComparison.Ordinal)
                || url.Equals("about:blank", StringComparison.OrdinalIgnoreCase)
                ? url
                : NormalizeSource(url);

            Browser.Load(source);
        }

        public void Reload() => Load(Address);
        public void Back() { try { Browser?.GetBrowser()?.GoBack(); } catch { } }
        public void Forward() { try { Browser?.GetBrowser()?.GoForward(); } catch { } }
        public void Activate() { if (Tab is not null) _host.ActivateTab(_group, Tab); }
        public void Close() { if (Tab is not null) _group.Manager.Close(Tab); }
    }

    /// <summary>
    /// CEF render handler that converts BGRA paints into RGBA frame buffers.
    /// </summary>
    private sealed class WebViewRenderHandler : IRenderHandler
    {
        private readonly WebViewTabTrack _track;

        public WebViewRenderHandler(WebViewTabTrack track) => _track = track;

        public ScreenInfo? GetScreenInfo() => null;

        public CefRect GetViewRect() =>
            new(0, 0, Math.Max(1, _track.Width), Math.Max(1, _track.Height));

        public bool GetScreenPoint(int viewX, int viewY, out int screenX, out int screenY)
        {
            screenX = 0;
            screenY = 0;
            return false;
        }

        public void OnAcceleratedPaint(PaintElementType type, CefRect dirtyRect, AcceleratedPaintInfo info) { }

        public void OnPaint(PaintElementType type, CefRect dirtyRect, IntPtr buffer, int width, int height)
        {
            if (type != PaintElementType.View || buffer == IntPtr.Zero || width <= 0 || height <= 0) return;

            var length = width * height * 4;

            lock (_track.FrameSync)
            {
                if (_track.Pixels.Length != length) _track.Pixels = new byte[length];

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

        public void OnCursorChange(IntPtr cursor, CefCursorType type, CursorInfo customCursorInfo) { }
        public bool StartDragging(IDragData dragData, DragOperationsMask mask, int x, int y) => false;
        public void UpdateDragCursor(DragOperationsMask operation) { }
        public void OnPopupShow(bool show) { }
        public void OnPopupSize(CefRect rect) { }
        public void OnImeCompositionRangeChanged(CefRange selectedRange, CefRect[] characterBounds) { }
        public void OnVirtualKeyboardRequested(IBrowser browser, TextInputMode inputMode) { }
        public void Dispose() { }
    }
}
