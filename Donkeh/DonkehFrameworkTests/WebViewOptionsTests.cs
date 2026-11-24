using Donkeh.Windowing;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Coverage for webview configuration and custom URL resolution. These tests
/// exercise the engine-neutral option layer and never initialize CEF.
/// </summary>
public sealed class WebViewOptionsTests
{
    [Fact]
    public void LiteralCustomUrlResolvesWithQueryString()
    {
        var options = new WebViewOptions();
        options.ProtocolKey = "my-app";
        options.AddCustomUrl("my-app://item", WebViewResource.FromHtml("<h1>item</h1>"));

        var resource = options.Resolve(new Uri("my-app://item?mode=full"));

        Assert.NotNull(resource);
        Assert.Equal(200, resource!.StatusCode);
        Assert.Equal("<h1>item</h1>", System.Text.Encoding.UTF8.GetString(resource.Content));
    }

    [Fact]
    public void AssetResolverTakesPrecedenceOverProbing()
    {
        var options = new WebViewOptions();
        options.AssetResolver = path => WebViewResource.FromString(path, "text/plain");
        options.AddCustomUrl("my-app://page", "some/asset.html");

        var resource = options.Resolve(new Uri("my-app://page"));

        Assert.NotNull(resource);
        Assert.Equal("some/asset.html", System.Text.Encoding.UTF8.GetString(resource!.Content));
    }

    [Fact]
    public void ResolverDelegateGetsTheRequestUri()
    {
        var options = new WebViewOptions();
        options.AddCustomUrl("my-app://dynamic", uri =>
            WebViewResource.FromHtml("<h1>" + uri.Query + "</h1>"));

        var resource = options.Resolve(new Uri("my-app://dynamic?id=7"));

        Assert.NotNull(resource);
        Assert.Contains("id=7", System.Text.Encoding.UTF8.GetString(resource!.Content));
    }

    [Fact]
    public void UrlResolverIsTheFinalFallback()
    {
        var options = new WebViewOptions();
        options.ProtocolKey = "my-app";
        options.UrlResolver = uri => uri.Scheme == "my-app"
            ? WebViewResource.FromHtml("fallback")
            : null;

        var resource = options.Resolve(new Uri("my-app://unknown/path"));

        Assert.NotNull(resource);
        Assert.Contains("fallback", System.Text.Encoding.UTF8.GetString(resource!.Content));
    }

    [Fact]
    public void ProtocolKeyNormalizesTrailingColon()
    {
        var options = new WebViewOptions { ProtocolKey = "My-App:" };

        Assert.Equal("my-app", options.ProtocolKey);
        Assert.Contains("my-app", options.GetRegisteredSchemes());
    }

    [Fact]
    public void ExtraProtocolIsRegisteredAlongsideProtocolKey()
    {
        var options = new WebViewOptions { ProtocolKey = "my-app" };
        options.AddProtocol("assets", _ => WebViewResource.FromHtml("asset"));

        var schemes = options.GetRegisteredSchemes().ToArray();

        Assert.Contains("my-app", schemes);
        Assert.Contains("assets", schemes);
    }

    [Fact]
    public void RemoveCustomUrlClearsTheMapping()
    {
        var options = new WebViewOptions();
        options.AddCustomUrl("my-app://gone", WebViewResource.FromHtml("gone"));
        options.RemoveCustomUrl("my-app://gone");

        Assert.Null(options.Resolve(new Uri("my-app://gone")));
    }

    [Theory]
    [InlineData("page.html", "text/html; charset=utf-8")]
    [InlineData("style.css", "text/css; charset=utf-8")]
    [InlineData("app.js", "application/javascript; charset=utf-8")]
    [InlineData("data.bin", "application/octet-stream")]
    public void MimeTypeIsInferredFromExtension(string path, string expected)
    {
        Assert.Equal(expected, WebViewResource.GetMimeType(path));
    }
}
