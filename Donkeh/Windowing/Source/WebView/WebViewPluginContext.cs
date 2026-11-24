namespace Donkeh.Windowing;

/// <summary>
/// The plugin-facing surface of one webview tab. Use it to add plugins and
/// scripts for a single view, bind .NET objects, or exchange messages with
/// page JavaScript.
/// </summary>
public sealed class WebViewPluginContext
{
    private readonly WebViewPluginRuntime _runtime;
    private readonly WebViewTab? _tab;

    internal WebViewPluginContext(WebViewPluginRuntime runtime, WebViewTab? tab)
    {
        _runtime = runtime;
        _tab = tab;
    }

    /// <summary>Gets the view this context belongs to.</summary>
    public WebView View => _runtime.View;

    /// <summary>Gets the tab this context belongs to.</summary>
    public WebViewTab? Tab => _tab;

    /// <summary>Gets the plugins attached to this view.</summary>
    public IReadOnlyList<WebViewPlugin> Plugins => _runtime.Plugins;

    /// <summary>Gets the scripts installed on this view.</summary>
    public IReadOnlyList<WebViewScript> Scripts => _runtime.Scripts;

    /// <summary>Gets the .NET objects bound into this view's JavaScript.</summary>
    public IReadOnlyList<WebViewObjectBinding> Bindings => _runtime.Bindings;

    /// <summary>Adds a plugin to this view only.</summary>
    public WebViewPluginContext AddPlugin(WebViewPlugin plugin)
    {
        _runtime.AddPlugin(plugin);
        return this;
    }

    /// <summary>Removes a plugin from this view by name.</summary>
    public WebViewPluginContext RemovePlugin(string name)
    {
        _runtime.RemovePlugin(name);
        return this;
    }

    /// <summary>Adds or replaces a script on this view.</summary>
    public WebViewPluginContext AddScript(WebViewScript script)
    {
        _runtime.AddScript(script);
        return this;
    }

    /// <summary>Removes a script from this view by id.</summary>
    public WebViewPluginContext RemoveScript(string id)
    {
        _runtime.RemoveScript(id);
        return this;
    }

    /// <summary>Exposes a .NET object to page JavaScript.</summary>
    public WebViewPluginContext RegisterObject(
        string name,
        object target,
        WebViewBindingOptions? options = null)
    {
        _runtime.RegisterObject(name, target, options);
        return this;
    }

    /// <summary>Removes a previously registered object binding.</summary>
    public WebViewPluginContext UnregisterObject(string name)
    {
        _runtime.UnregisterObject(name);
        return this;
    }

    /// <summary>Registers a handler for messages on a channel.</summary>
    public WebViewPluginContext OnMessage(string channel, Action<WebViewMessage> handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentNullException.ThrowIfNull(handler);

        _runtime.OnMessage(channel, handler);
        return this;
    }

    /// <summary>Emits an event to the page's JavaScript listeners.</summary>
    public WebViewPluginContext Emit(string channel, object? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channel);

        _runtime.Emit(channel, payload);
        return this;
    }

    /// <summary>Runs a script in this view immediately.</summary>
    public WebViewPluginContext ExecuteJavaScript(string script)
    {
        View.ExecuteJavaScript(script);
        return this;
    }

    /// <summary>Evaluates a script in this view and returns its result.</summary>
    public Task<string?> EvaluateJavaScriptAsync(string script) =>
        View.EvaluateJavaScriptAsync(script);
}
