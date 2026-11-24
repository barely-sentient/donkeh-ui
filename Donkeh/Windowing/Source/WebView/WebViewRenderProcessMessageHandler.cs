using CefSharp;

namespace Donkeh.Windowing;

/// <summary>
/// Chains CefSharp's own render-process handler (which binds registered
/// objects) with plugin script injection.
/// </summary>
internal sealed class WebViewRenderProcessMessageHandler : IRenderProcessMessageHandler
{
    private readonly IRenderProcessMessageHandler? _inner;
    private readonly WebViewPluginRuntime _runtime;

    public WebViewRenderProcessMessageHandler(
        IRenderProcessMessageHandler? inner,
        WebViewPluginRuntime runtime)
    {
        _inner = inner;
        _runtime = runtime;
    }

    public void OnContextCreated(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame)
    {
        _inner?.OnContextCreated(chromiumWebBrowser, browser, frame);
        _runtime.OnContextCreated(frame);
    }

    public void OnContextReleased(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame) =>
        _inner?.OnContextReleased(chromiumWebBrowser, browser, frame);

    public void OnFocusedNodeChanged(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IDomNode node) =>
        _inner?.OnFocusedNodeChanged(chromiumWebBrowser, browser, frame, node);

    public void OnUncaughtException(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        JavascriptException exception) =>
        _inner?.OnUncaughtException(chromiumWebBrowser, browser, frame, exception);
}
