namespace Donkeh.Windowing;

/// <summary>
/// Content served for a <c>webview</c> custom URL. Deliberately free of
/// CefSharp types so applications can provide content without depending on
/// the browser engine.
/// </summary>
public sealed class WebViewResource
{
    /// <summary>Creates a resource from a byte buffer.</summary>
    public WebViewResource(
        byte[] content,
        string contentType,
        int statusCode = 200,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        Content = content ?? [];
        ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType;
        StatusCode = statusCode;
        Headers = headers ?? EmptyHeaders;
    }

    private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets the response body.</summary>
    public byte[] Content { get; }

    /// <summary>Gets the MIME type of the response.</summary>
    public string ContentType { get; }

    /// <summary>Gets the HTTP-style status code.</summary>
    public int StatusCode { get; }

    /// <summary>Gets additional response headers.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>Creates a UTF-8 HTML response.</summary>
    public static WebViewResource FromHtml(string html) =>
        new(System.Text.Encoding.UTF8.GetBytes(html ?? ""), "text/html; charset=utf-8");

    /// <summary>Creates a response from a string using an explicit content type.</summary>
    public static WebViewResource FromString(string text, string contentType) =>
        new(System.Text.Encoding.UTF8.GetBytes(text ?? ""), contentType);

    /// <summary>Creates a response from a byte buffer.</summary>
    public static WebViewResource FromBytes(byte[] bytes, string contentType) =>
        new(bytes, contentType);

    /// <summary>Reads a file and infers its content type from the extension.</summary>
    public static WebViewResource FromFile(string path, string? contentType = null)
    {
        var full = Path.IsPathRooted(path)
            ? path
            : Path.Combine(AppContext.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));

        var bytes = File.ReadAllBytes(full);
        return new WebViewResource(bytes, contentType ?? GetMimeType(full));
    }

    /// <summary>Creates a redirect response.</summary>
    public static WebViewResource Redirect(string url) =>
        new([], "text/plain; charset=utf-8", 302,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Location"] = url });

    /// <summary>Creates a 404 response.</summary>
    public static WebViewResource NotFound(string? message = null) =>
        new(System.Text.Encoding.UTF8.GetBytes(
                "<!doctype html><title>Not found</title><h1>Not found</h1><p>"
                + System.Net.WebUtility.HtmlEncode(message ?? "The requested page does not exist.") + "</p>"),
            "text/html; charset=utf-8", 404);

    /// <summary>Guesses a MIME type from a file name or URL.</summary>
    public static string GetMimeType(string path)
    {
        var extension = Path.GetExtension(path.Split('?')[0]).ToLowerInvariant();

        return extension switch
        {
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".xhtml" => "application/xhtml+xml; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".js" or ".mjs" => "application/javascript; charset=utf-8",
            ".json" => "application/json; charset=utf-8",
            ".xml" => "application/xml; charset=utf-8",
            ".txt" or ".md" => "text/plain; charset=utf-8",
            ".svg" => "image/svg+xml",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".pdf" => "application/pdf",
            ".wasm" => "application/wasm",
            _ => "application/octet-stream"
        };
    }
}
