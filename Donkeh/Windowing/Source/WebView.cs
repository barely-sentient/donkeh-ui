using CefSharp;
using CefSharp.OffScreen;

namespace Donkeh.Windowing;

/// <summary>
/// Code-facing control surface for a <c>webview</c> element. Wraps the
/// offscreen browser so applications can navigate, manage tabs and run script
/// without taking a direct dependency on CefSharp.
/// </summary>
/// <remarks>
/// Instances are created by the window and handed to
/// <see cref="WebViewProps.OnReady"/>. Methods are safe to call from the
/// frame thread; script evaluation runs on Chromium's renderer thread and
/// returns asynchronously.
/// </remarks>
public sealed class WebView
{
    private readonly ChromiumWebBrowser? _browser;
    private readonly WebViewTabManager? _tabs;

    internal WebView(ChromiumWebBrowser browser) => _browser = browser;

    internal WebView(WebViewTabManager tabs) => _tabs = tabs;

    private ChromiumWebBrowser? ActiveBrowser =>
        _browser ?? _tabs?.Active?.View?._browser;

    /// <summary>Gets the tab manager, or null for a standalone browser.</summary>
    public WebViewTabManager? Tabs => _tabs;

    /// <summary>Gets the plugin context for the active browser.</summary>
    public WebViewPluginContext? Plugins => _tabs?.Active?.Plugins ?? PluginContext;

    internal WebViewPluginContext? PluginContext { get; set; }

    /// <summary>Gets the URL currently displayed by the active tab.</summary>
    public string Address => ActiveBrowser?.Address ?? "";

    /// <summary>Gets whether the active tab is still loading a document.</summary>
    public bool IsLoading => ActiveBrowser?.IsLoading ?? false;

    /// <summary>Gets whether the underlying browser has been created.</summary>
    public bool IsReady => ActiveBrowser?.IsBrowserInitialized ?? false;

    /// <summary>Gets whether the active tab can navigate back.</summary>
    public bool CanGoBack
    {
        get
        {
            try { return ActiveBrowser?.GetBrowser()?.CanGoBack ?? false; }
            catch { return false; }
        }
    }

    /// <summary>Gets whether the active tab can navigate forward.</summary>
    public bool CanGoForward
    {
        get
        {
            try { return ActiveBrowser?.GetBrowser()?.CanGoForward ?? false; }
            catch { return false; }
        }
    }

    /// <summary>Navigates the active tab to <paramref name="url"/>.</summary>
    /// <param name="url">Absolute URL, custom application URL, or a <c>data:</c>/<c>file:</c> URI.</param>
    public void Load(string url)
    {
        var browser = ActiveBrowser;
        if (browser is null || browser.IsDisposed || string.IsNullOrWhiteSpace(url)) return;

        browser.Load(url);
    }

    /// <summary>Reloads the current address.</summary>
    public void Reload()
    {
        if (!string.IsNullOrEmpty(Address)) Load(Address);
    }

    /// <summary>Navigates back in the active tab's history.</summary>
    public void Back()
    {
        try { ActiveBrowser?.GetBrowser()?.GoBack(); }
        catch { }
    }

    /// <summary>Navigates forward in the active tab's history.</summary>
    public void Forward()
    {
        try { ActiveBrowser?.GetBrowser()?.GoForward(); }
        catch { }
    }

    /// <summary>Opens a new tab, optionally navigating it to a URL.</summary>
    public WebViewTab? OpenTab(string? url = null, bool activate = true) =>
        _tabs?.Open(url, activate);

    /// <summary>Runs a string of JavaScript in the active tab's main frame.</summary>
    /// <param name="script">The script to execute.</param>
    /// <remarks>Script runs asynchronously on Chromium's renderer thread.</remarks>
    public void ExecuteJavaScript(string script)
    {
        var browser = ActiveBrowser;
        if (browser is null || browser.IsDisposed || !browser.IsBrowserInitialized
            || string.IsNullOrEmpty(script))
        {
            return;
        }

        try
        {
            using var frame = browser.GetMainFrame();
            frame?.ExecuteJavaScriptAsync(script);
        }
        catch
        {
            // The page may have navigated while the call was being dispatched.
        }
    }

    /// <summary>Evaluates JavaScript and returns the result as a string.</summary>
    public async Task<string?> EvaluateJavaScriptAsync(string script)
    {
        var browser = ActiveBrowser;
        if (browser is null || browser.IsDisposed || !browser.IsBrowserInitialized
            || string.IsNullOrEmpty(script))
        {
            return null;
        }

        try
        {
            using var frame = browser.GetMainFrame();
            if (frame is null) return null;

            var response = await frame.EvaluateScriptAsync(script).ConfigureAwait(false);
            return response.Success ? response.Result?.ToString() : null;
        }
        catch
        {
            return null;
        }
    }
}
