using CefSharp;
using CefSharp.OffScreen;

namespace Donkeh.Windowing;

/// <summary>
/// Code-facing control surface for a <c>webview</c> element. Wraps the
/// offscreen browser so applications can navigate and run script without
/// taking a direct dependency on CefSharp.
/// </summary>
/// <remarks>
/// Instances are created by the window and handed to
/// <see cref="WebViewProps.OnReady"/>. Methods are safe to call from the
/// frame thread; script evaluation runs on Chromium's renderer thread and
/// returns asynchronously.
/// </remarks>
public sealed class WebView
{
    private readonly ChromiumWebBrowser _browser;

    internal WebView(ChromiumWebBrowser browser) => _browser = browser;

    /// <summary>Gets the URL currently displayed by the browser.</summary>
    public string Address => _browser.Address;

    /// <summary>Gets whether the browser is still loading a document.</summary>
    public bool IsLoading => _browser.IsLoading;

    /// <summary>Gets whether the underlying browser has been created.</summary>
    public bool IsReady => _browser.IsBrowserInitialized;

    /// <summary>Navigates to <paramref name="url"/>.</summary>
    /// <param name="url">Absolute URL, or a <c>data:</c>/<c>file:</c> URI.</param>
    public void Load(string url)
    {
        if (!_browser.IsDisposed)
        {
            _browser.Load(url);
        }
    }

    /// <summary>Reloads the current address.</summary>
    public void Reload()
    {
        if (!string.IsNullOrEmpty(Address))
        {
            Load(Address);
        }
    }

    /// <summary>Runs a string of JavaScript in the main frame.</summary>
    /// <param name="script">The script to execute.</param>
    /// <remarks>Script runs asynchronously on Chromium's renderer thread.</remarks>
    public void ExecuteJavaScript(string script)
    {
        if (_browser.IsDisposed || !_browser.IsBrowserInitialized || string.IsNullOrEmpty(script))
        {
            return;
        }

        try
        {
            using var frame = _browser.GetMainFrame();
            frame?.ExecuteJavaScriptAsync(script);
        }
        catch
        {
            // The page may have navigated while the call was being dispatched.
        }
    }
}
