using Donkeh.UILab;

namespace Donkeh.Windowing;

/// <summary>
/// Props for a <c>webview</c> element. Extends <see cref="Props"/> with a typed
/// callback that receives the host's browser controller once the underlying
/// offscreen browser has been created.
/// </summary>
/// <example>
/// <code>
/// H("webview", new WebViewProps
/// {
///     Src = "https://example.com",
///     OnReady = view =&gt; view.ExecuteJavaScript("document.title")
/// });
/// </code>
/// </example>
public sealed class WebViewProps : Props
{
    /// <summary>
    /// Gets or sets the callback invoked once the window has created the
    /// underlying browser for this element.
    /// </summary>
    /// <remarks>
    /// The handler receives a <see cref="WebView"/> controller, not the raw
    /// CefSharp object, so application code does not depend on the hosting
    /// engine. Creation is asynchronous; the callback runs the first frame
    /// after the browser becomes available.
    /// </remarks>
    public Action<WebView>? OnReady
    {
        get => OnWebViewReady as Action<WebView>;
        init => OnWebViewReady = value;
    }
}
