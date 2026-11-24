namespace Donkeh.Windowing;

/// <summary>Internal bridge between an engine-neutral tab and its browser.</summary>
internal interface IWebViewTabBridge
{
    WebView View { get; }
    string Address { get; }
    string Title { get; set; }
    bool IsLoading { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }
    bool IsActive { get; }
    void Load(string url);
    void Reload();
    void Back();
    void Forward();
    void Activate();
    void Close();
}

/// <summary>
/// One browser tab belonging to a <c>webview</c> element. The element paints
/// whichever tab the manager marks active.
/// </summary>
public sealed class WebViewTab
{
    private readonly IWebViewTabBridge _bridge;

    internal IWebViewTabBridge Bridge => _bridge;

    internal WebViewTab(Guid id, IWebViewTabBridge bridge)
    {
        Id = id;
        _bridge = bridge;
    }

    /// <summary>Gets the stable identity of the tab.</summary>
    public Guid Id { get; }

    /// <summary>Gets the controller for this tab's browser.</summary>
    public WebView View => _bridge.View;

    /// <summary>Gets the plugin context for this tab.</summary>
    public WebViewPluginContext? Plugins =>
        Bridge is WebViewHost.WebViewTabTrack track ? track.Plugins : null;

    /// <summary>Gets the URL currently displayed by the tab.</summary>
    public string Address => _bridge.Address;

    /// <summary>Gets or sets the tab's display title.</summary>
    public string Title
    {
        get => _bridge.Title;
        set => _bridge.Title = value;
    }

    /// <summary>Gets whether the tab is still loading a document.</summary>
    public bool IsLoading => _bridge.IsLoading;

    /// <summary>Gets whether the tab can navigate back.</summary>
    public bool CanGoBack => _bridge.CanGoBack;

    /// <summary>Gets whether the tab can navigate forward.</summary>
    public bool CanGoForward => _bridge.CanGoForward;

    /// <summary>Gets whether this is the tab currently shown by the element.</summary>
    public bool IsActive => _bridge.IsActive;

    /// <summary>Navigates the tab to a URL.</summary>
    public void Load(string url) => _bridge.Load(url);

    /// <summary>Reloads the tab.</summary>
    public void Reload() => _bridge.Reload();

    /// <summary>Navigates back in the tab's history.</summary>
    public void Back() => _bridge.Back();

    /// <summary>Navigates forward in the tab's history.</summary>
    public void Forward() => _bridge.Forward();

    /// <summary>Makes this tab the visible one.</summary>
    public void Activate() => _bridge.Activate();

    /// <summary>Closes this tab.</summary>
    public void Close() => _bridge.Close();

    /// <summary>Fired when the tab's title, address or load state changes.</summary>
    public event Action<WebViewTab>? StateChanged;

    internal void RaiseStateChanged() => StateChanged?.Invoke(this);
}

/// <summary>
/// The tab collection of a <c>webview</c> element. Apps can bind a tab strip
/// to <see cref="Tabs"/>, <see cref="Active"/> and the change events.
/// </summary>
public sealed class WebViewTabManager
{
    private readonly List<WebViewTab> _tabs = [];

    internal Func<string, WebViewTab?>? TabFactory { get; set; }
    internal Action<WebViewTab>? TabDestroyer { get; set; }
    internal Action<WebViewTab>? TabActivator { get; set; }

    /// <summary>Gets the open tabs in creation order.</summary>
    public IReadOnlyList<WebViewTab> Tabs => _tabs;

    /// <summary>Gets the currently visible tab, if any.</summary>
    public WebViewTab? Active { get; private set; }

    /// <summary>
    /// Opens a new tab. An empty URL uses
    /// <see cref="WebViewOptions.NewTabPage"/>.
    /// </summary>
    public WebViewTab? Open(string? url = null, bool activate = true)
    {
        if (TabFactory is null) return null;

        var target = string.IsNullOrWhiteSpace(url)
            ? WebViewOptions.Current.NewTabPage
            : url!;

        var tab = TabFactory(target);
        if (tab is null) return null;

        if (activate) Activate(tab);
        return tab;
    }

    /// <summary>Closes a tab and activates a neighbour when possible.</summary>
    public void Close(WebViewTab tab)
    {
        if (!_tabs.Contains(tab)) return;

        var wasActive = ReferenceEquals(Active, tab);
        var index = _tabs.IndexOf(tab);

        TabDestroyer?.Invoke(tab);
        Detach(tab);

        if (wasActive && _tabs.Count > 0)
        {
            var next = _tabs[Math.Min(index, _tabs.Count - 1)];
            Activate(next);
        }
    }

    /// <summary>Makes a tab visible.</summary>
    public void Activate(WebViewTab tab)
    {
        if (!_tabs.Contains(tab)) return;

        TabActivator?.Invoke(tab);
        SetActive(tab);
    }

    /// <summary>Activates the next tab, wrapping at the end.</summary>
    public WebViewTab? Next()
    {
        if (_tabs.Count == 0) return null;
        var index = Active is null ? -1 : _tabs.IndexOf(Active);
        var next = _tabs[(index + 1 + _tabs.Count) % _tabs.Count];
        Activate(next);
        return next;
    }

    /// <summary>Activates the previous tab, wrapping at the start.</summary>
    public WebViewTab? Previous()
    {
        if (_tabs.Count == 0) return null;
        var index = Active is null ? 0 : _tabs.IndexOf(Active);
        var previous = _tabs[(index - 1 + _tabs.Count) % _tabs.Count];
        Activate(previous);
        return previous;
    }

    /// <summary>Fired when a tab is added.</summary>
    public event Action<WebViewTab>? TabOpened;

    /// <summary>Fired when a tab is removed.</summary>
    public event Action<WebViewTab>? TabClosed;

    /// <summary>Fired when the visible tab changes.</summary>
    public event Action<WebViewTab?>? ActiveChanged;

    /// <summary>Fired when the tab list changes.</summary>
    public event Action? TabsChanged;

    /// <summary>Fired when any tab's title, address or load state changes.</summary>
    public event Action<WebViewTab>? TabStateChanged;

    internal void Attach(WebViewTab tab)
    {
        if (_tabs.Contains(tab)) return;

        _tabs.Add(tab);
        tab.StateChanged += OnTabStateChanged;
        TabOpened?.Invoke(tab);
        TabsChanged?.Invoke();
    }

    internal void Detach(WebViewTab tab)
    {
        if (!_tabs.Remove(tab)) return;

        tab.StateChanged -= OnTabStateChanged;
        if (ReferenceEquals(Active, tab)) SetActive(null);
        TabClosed?.Invoke(tab);
        TabsChanged?.Invoke();
    }

    internal void SetActive(WebViewTab? tab)
    {
        if (ReferenceEquals(Active, tab)) return;

        Active = tab;
        ActiveChanged?.Invoke(tab);
    }

    private void OnTabStateChanged(WebViewTab tab) => TabStateChanged?.Invoke(tab);
}
