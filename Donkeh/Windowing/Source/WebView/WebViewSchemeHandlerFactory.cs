using CefSharp;

namespace Donkeh.Windowing;

/// <summary>
/// Serves registered custom-scheme URLs from <see cref="WebViewOptions"/>.
/// </summary>
internal sealed class WebViewSchemeHandlerFactory : ISchemeHandlerFactory
{
    private readonly WebViewOptions _options;

    public WebViewSchemeHandlerFactory(WebViewOptions options) => _options = options;

    public IResourceHandler Create(IBrowser browser, IFrame frame, string schemeName, IRequest request)
    {
        WebViewResource? resource = null;

        if (Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            try { resource = _options.Resolve(uri); }
            catch { resource = null; }
        }

        resource ??= WebViewResource.NotFound("No handler is registered for " + request.Url);

        var handler = ResourceHandler.FromByteArray(resource.Content, resource.ContentType) as ResourceHandler;

        if (handler is null)
        {
            return ResourceHandler.FromByteArray(resource.Content, resource.ContentType);
        }

        handler.StatusCode = resource.StatusCode;
        handler.StatusText = resource.StatusCode switch
        {
            200 => "OK",
            302 => "Found",
            404 => "Not Found",
            _ => handler.StatusText ?? "OK"
        };

        foreach (var header in resource.Headers)
        {
            handler.Headers[header.Key] = header.Value;
        }

        return handler;
    }
}
