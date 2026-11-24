namespace Donkeh.Windowing;

/// <summary>
/// Base class for engine-neutral webview plugins. A plugin can contribute
/// preload scripts, bind .NET objects into JavaScript and handle IPC messages.
/// </summary>
/// <remarks>
/// Plugins are attached to one webview tab. Override the lifecycle methods to
/// react to document creation, load completion and messages. Register plugins
/// globally in <see cref="WebViewOptions.Configuring"/> or per view through
/// <see cref="WebViewPluginContext"/>.
/// </remarks>
public abstract class WebViewPlugin
{
    /// <summary>Gets the plugin name used for de-duplication and removal.</summary>
    public virtual string Name => GetType().Name;

    /// <summary>Gets whether the plugin participates in injection and events.</summary>
    public virtual bool Enabled => true;

    /// <summary>Gets scripts contributed by the plugin.</summary>
    public virtual IReadOnlyList<WebViewScript> Scripts => [];

    /// <summary>Called when the plugin is attached to a webview.</summary>
    public virtual void OnAttached(WebViewPluginContext context) { }

    /// <summary>Called when the plugin is removed from a webview.</summary>
    public virtual void OnDetached(WebViewPluginContext context) { }

    /// <summary>Called when a frame's JavaScript context is created.</summary>
    public virtual void OnContextCreated(WebViewPluginContext context, Uri url, bool isMainFrame) { }

    /// <summary>Called after a main-frame document finishes loading.</summary>
    public virtual void OnLoad(WebViewPluginContext context, Uri url) { }

    /// <summary>Called for messages on channels not consumed by a handler.</summary>
    public virtual void OnMessage(WebViewMessage message) { }
}
