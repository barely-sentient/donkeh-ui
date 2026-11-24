namespace Donkeh.Windowing;

/// <summary>
/// Process-wide configuration for desktop <c>webview</c> elements: startup
/// pages, the custom URL protocol and the URL-to-asset registry.
/// </summary>
/// <remarks>
/// <para>
/// Replace the default instance with <see cref="Use"/> during startup, or
/// subscribe to <see cref="Configuring"/> to adjust the active options just
/// before CEF is initialized. Scheme registration must happen before the
/// first browser is created; URL mappings may be changed at any time.
/// </para>
/// <para>
/// The class is intentionally not sealed and its members are virtual so an
/// application can subclass it and override resolution completely.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// WebViewOptions.Configure += options =&gt;
/// {
///     options.ProtocolKey = "my-app";
///     options.LoadingPage = "data:text/html,&lt;h1&gt;Loading…&lt;/h1&gt;";
///     options.NewTabPage = "my-app://newtab";
///     options.AddCustomUrl("my-app://path/to/item", "path/to/embedded/asset.html");
/// };
/// </code>
/// </example>
public class WebViewOptions
{
    private static readonly object Sync = new();
    private static WebViewOptions _current = new();

    private readonly Dictionary<string, object> _urls = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Func<Uri, WebViewResource?>> _protocols =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, WebViewResource?> _assetCache =
        new(StringComparer.OrdinalIgnoreCase);

    private string? _protocolKey;
    private bool _frozen;

    /// <summary>Plugins applied to every webview unless removed per view.</summary>
    public List<WebViewPlugin> Plugins { get; } = [];

    /// <summary>Scripts injected into every webview unless removed per view.</summary>
    public List<WebViewScript> Scripts { get; } = [];

    /// <summary>.NET objects bound into every webview's JavaScript context.</summary>
    public List<WebViewObjectBinding> Bindings { get; } = [];

    /// <summary>Global message handlers keyed by channel.</summary>
    public Dictionary<string, List<Action<WebViewMessage>>> MessageHandlers { get; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets whether the <c>window.donkeh</c> bridge preload script is
    /// injected. Disable it to provide your own bridge.
    /// </summary>
    public virtual bool EnablePluginBridge { get; set; } = true;

    /// <summary>Gets or sets the global name of the injected bridge object.</summary>
    public virtual string BridgeGlobalName { get; set; } = "donkeh";

    /// <summary>Gets the active options instance.</summary>
    public static WebViewOptions Current
    {
        get { lock (Sync) return _current; }
    }

    /// <summary>
    /// Replaces the active options instance. Call this before the first
    /// <c>webview</c> creates its browser.
    /// </summary>
    public static void Use(WebViewOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (Sync)
        {
            if (_current._frozen)
            {
                throw new InvalidOperationException(
                    "WebViewOptions cannot be replaced after CEF has been initialized.");
            }

            _current = options;
        }
    }

    /// <summary>
    /// Fired once, just before CEF is initialized. Use it to set up the
    /// protocol key, startup pages and custom URL mappings.
    /// </summary>
    public static event Action<WebViewOptions>? Configuring;

    internal static void RaiseConfiguring()
    {
        WebViewOptions options;
        lock (Sync) options = _current;

        Configuring?.Invoke(options);

        lock (Sync) options._frozen = true;
    }

    /// <summary>
    /// Gets or sets the scheme used by custom application URLs. Set before
    /// the first browser is created. The default is <see langword="null"/>,
    /// meaning no custom scheme is registered.
    /// </summary>
    public virtual string? ProtocolKey
    {
        get => _protocolKey;
        set
        {
            EnsureConfigurable();
            _protocolKey = string.IsNullOrWhiteSpace(value)
                ? null
                : value.Trim().TrimEnd(':').ToLowerInvariant();
        }
    }

    /// <summary>
    /// Gets or sets the URL shown while a webview's real document loads.
    /// Defaults to <c>about:blank</c>. Individual elements can override it
    /// with the <c>loading-page</c> attribute.
    /// </summary>
    public virtual string LoadingPage { get; set; } = "about:blank";

    /// <summary>Gets or sets the page used when a new tab is opened without a URL.</summary>
    public virtual string NewTabPage { get; set; } = "about:blank";

    /// <summary>Gets or sets the page a tab navigates to when no URL is supplied.</summary>
    public virtual string HomePage { get; set; } = "about:blank";

    /// <summary>
    /// Gets or sets an optional global ready hook invoked for every webview
    /// controller once its browser is available.
    /// </summary>
    public virtual Action<WebView>? OnWebViewReady { get; set; }

    /// <summary>Gets or sets a hook invoked whenever a tab is opened.</summary>
    public virtual Action<WebViewTab>? OnTabOpened { get; set; }

    /// <summary>Gets or sets a hook invoked whenever a tab is closed.</summary>
    public virtual Action<WebViewTab>? OnTabClosed { get; set; }

    /// <summary>
    /// Gets or sets a resolver for the second argument of
    /// <see cref="AddCustomUrl(string, string)"/>. Returning
    /// <see langword="null"/> falls back to automatic embedded/file probing.
    /// </summary>
    public virtual Func<string, WebViewResource?>? AssetResolver { get; set; }

    /// <summary>
    /// Gets or sets a catch-all resolver invoked for every custom-scheme URL
    /// after the explicit mappings have been checked.
    /// </summary>
    public virtual Func<Uri, WebViewResource?>? UrlResolver { get; set; }

    /// <summary>
    /// Maps a custom URL to an embedded resource or file path. The target is
    /// auto-detected: an embedded manifest resource is tried first, then a
    /// path relative to the application directory.
    /// </summary>
    public WebViewOptions AddCustomUrl(string url, string asset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(asset);

        lock (_urls) _urls[NormalizeKey(url)] = asset;
        return this;
    }

    /// <summary>Maps a custom URL to literal content.</summary>
    public WebViewOptions AddCustomUrl(string url, WebViewResource resource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(resource);

        lock (_urls) _urls[NormalizeKey(url)] = resource;
        return this;
    }

    /// <summary>Maps a custom URL to a per-request resolver.</summary>
    public WebViewOptions AddCustomUrl(string url, Func<Uri, WebViewResource?> resolver)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentNullException.ThrowIfNull(resolver);

        lock (_urls) _urls[NormalizeKey(url)] = resolver;
        return this;
    }

    /// <summary>Removes a custom URL mapping.</summary>
    public WebViewOptions RemoveCustomUrl(string url)
    {
        lock (_urls) _urls.Remove(NormalizeKey(url));
        return this;
    }

    /// <summary>Removes every custom URL mapping.</summary>
    public WebViewOptions ClearCustomUrls()
    {
        lock (_urls) _urls.Clear();
        lock (_assetCache) _assetCache.Clear();
        return this;
    }

    /// <summary>
    /// Registers an extra custom scheme with its own resolver. Useful when an
    /// application needs more than one protocol in addition to
    /// <see cref="ProtocolKey"/>.
    /// </summary>
    public WebViewOptions AddProtocol(string scheme, Func<Uri, WebViewResource?> resolver)
    {
        EnsureConfigurable();
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentNullException.ThrowIfNull(resolver);

        lock (_protocols) _protocols[scheme.Trim().TrimEnd(':').ToLowerInvariant()] = resolver;
        return this;
    }

    /// <summary>Removes an extra custom scheme.</summary>
    public WebViewOptions RemoveProtocol(string scheme)
    {
        EnsureConfigurable();
        lock (_protocols) _protocols.Remove(scheme.Trim().TrimEnd(':').ToLowerInvariant());
        return this;
    }

    /// <summary>Adds a plugin to every webview.</summary>
    public WebViewOptions AddPlugin(WebViewPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        Plugins.Add(plugin);
        return this;
    }

    /// <summary>Adds a script to every webview.</summary>
    public WebViewOptions AddScript(WebViewScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        Scripts.Add(script);
        return this;
    }

    /// <summary>Binds a .NET object into every webview's JavaScript context.</summary>
    public WebViewOptions RegisterObject(
        string name,
        object target,
        WebViewBindingOptions? options = null)
    {
        Bindings.Add(new WebViewObjectBinding(name, target, options));
        return this;
    }

    /// <summary>Registers a global handler for a message channel.</summary>
    public WebViewOptions OnMessage(string channel, Action<WebViewMessage> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        if (!MessageHandlers.TryGetValue(channel, out var handlers))
        {
            handlers = [];
            MessageHandlers[channel] = handlers;
        }

        handlers.Add(handler);
        return this;
    }

    /// <summary>Resolves a request URL to content, or returns null when it is not handled.</summary>
    public virtual WebViewResource? Resolve(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        var candidates = new[]
        {
            NormalizeKey(uri.AbsoluteUri),
            NormalizeKey(uri.GetLeftPart(UriPartial.Path)),
            NormalizeKey(uri.AbsoluteUri.TrimEnd('/')),
            NormalizeKey(uri.GetLeftPart(UriPartial.Path).TrimEnd('/'))
        };

        foreach (var key in candidates)
        {
            object? entry;
            lock (_urls) _urls.TryGetValue(key, out entry);

            if (entry is not null)
            {
                var resolved = entry switch
                {
                    WebViewResource resource => resource,
                    string path => ResolveAsset(path),
                    Func<Uri, WebViewResource?> resolver => resolver(uri),
                    _ => null
                };

                if (resolved is not null) return resolved;
            }
        }

        Func<Uri, WebViewResource?>? protocol;
        lock (_protocols) _protocols.TryGetValue(uri.Scheme.ToLowerInvariant(), out protocol);
        var fromProtocol = protocol?.Invoke(uri);
        if (fromProtocol is not null) return fromProtocol;

        var fromResolver = UrlResolver?.Invoke(uri);
        if (fromResolver is not null) return fromResolver;

        return ResolveCore(uri);
    }

    /// <summary>
    /// Extension point for subclasses. Called last by <see cref="Resolve"/>.
    /// </summary>
    protected virtual WebViewResource? ResolveCore(Uri uri) => null;

    /// <summary>
    /// Resolves an asset path by invoking <see cref="AssetResolver"/>, then
    /// probing embedded manifest resources and finally files on disk.
    /// </summary>
    public virtual WebViewResource? ResolveAsset(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var custom = AssetResolver?.Invoke(path);
        if (custom is not null) return custom;

        lock (_assetCache)
        {
            if (_assetCache.TryGetValue(path, out var cached)) return cached;
        }

        var result = ProbeAsset(path);

        lock (_assetCache) _assetCache[path] = result;
        return result;
    }

    private static WebViewResource? ProbeAsset(string path)
    {
        var normalized = path.Replace('\\', '/').TrimStart('/');
        var dotted = normalized.Replace('/', '.');

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string[] names;
            try { names = assembly.GetManifestResourceNames(); }
            catch { continue; }

            var match = Array.Find(names, name =>
                name.Equals(dotted, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("." + dotted, StringComparison.OrdinalIgnoreCase));

            if (match is null) continue;

            try
            {
                using var stream = assembly.GetManifestResourceStream(match);
                if (stream is null) continue;
                using var memory = new MemoryStream();
                stream.CopyTo(memory);
                return new WebViewResource(memory.ToArray(), WebViewResource.GetMimeType(normalized));
            }
            catch
            {
                // A resource can disappear if an assembly is unloaded mid-request.
            }
        }

        var full = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory,
                normalized.Replace('/', Path.DirectorySeparatorChar));

        return File.Exists(full) ? WebViewResource.FromFile(full) : null;
    }

    internal IEnumerable<string> GetRegisteredSchemes()
    {
        if (!string.IsNullOrWhiteSpace(ProtocolKey)) yield return ProtocolKey!;

        lock (_protocols)
        {
            foreach (var scheme in _protocols.Keys) yield return scheme;
        }
    }

    internal bool IsCustomScheme(string scheme)
    {
        if (string.Equals(scheme, ProtocolKey, StringComparison.OrdinalIgnoreCase)) return true;
        lock (_protocols) return _protocols.ContainsKey(scheme.ToLowerInvariant());
    }

    private static string NormalizeKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return "";

        var candidate = url.Trim();
        return Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            ? uri.AbsoluteUri
            : candidate;
    }

    private void EnsureConfigurable()
    {
        lock (Sync)
        {
            if (_frozen)
            {
                throw new InvalidOperationException(
                    "Custom schemes must be registered before the first webview is created.");
            }
        }
    }
}
