using CefSharp;

namespace Donkeh.Windowing;

/// <summary>
/// Mirrors CEF load state into an engine-neutral tab, injects document-end
/// scripts and keeps the tab's title and history flags current.
/// </summary>
internal sealed class WebViewLoadHandler : ILoadHandler
{
    private readonly WebViewHost.WebViewTabTrack _track;
    private readonly WebViewPluginRuntime? _runtime;

    public WebViewLoadHandler(
        WebViewHost.WebViewTabTrack track,
        WebViewPluginRuntime? runtime = null)
    {
        _track = track;
        _runtime = runtime;
    }

    public void OnLoadingStateChange(IWebBrowser browserControl, LoadingStateChangedEventArgs args) =>
        _track.UpdateState(args.CanGoBack, args.CanGoForward, args.IsLoading);

    public void OnFrameLoadStart(IWebBrowser browserControl, FrameLoadStartEventArgs args)
    {
        if (args.Frame.IsMain) _track.UpdateState(_track.CanGoBack, _track.CanGoForward, true);
    }

    public void OnFrameLoadEnd(IWebBrowser browserControl, FrameLoadEndEventArgs args)
    {
        if (!args.Frame.IsMain) return;

        Uri? url = null;
        try { url = new Uri(args.Url); }
        catch { }

        _runtime?.Inject(WebViewScriptTiming.DocumentEnd, args.Frame, url, true);
        _runtime?.OnLoadEnd(url);

        _track.UpdateState(_track.CanGoBack, _track.CanGoForward, false);

        try
        {
            args.Frame.EvaluateScriptAsync("document.title").ContinueWith(task =>
            {
                if (task.Status == TaskStatus.RanToCompletion && task.Result.Success)
                {
                    _track.Title = task.Result.Result?.ToString() ?? _track.Address;
                }
            });
        }
        catch
        {
            _track.Title = _track.Address;
        }
    }

    public void OnLoadError(IWebBrowser browserControl, LoadErrorEventArgs args) =>
        _track.UpdateState(_track.CanGoBack, _track.CanGoForward, false);
}
