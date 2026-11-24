namespace Donkeh.Windowing;

/// <summary>
/// Options for exposing a .NET object to a webview's JavaScript context.
/// </summary>
public sealed class WebViewBindingOptions
{
    /// <summary>
    /// Gets or sets whether method calls return promises instead of blocking
    /// the browser thread. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Async { get; init; } = true;

    /// <summary>
    /// Gets or sets whether member names are camel-cased for JavaScript.
    /// Defaults to <see langword="true"/>.
    /// </summary>
    public bool CamelCaseNames { get; init; } = true;

    /// <summary>
    /// Gets or sets an optional predicate limiting binding to specific URLs.
    /// </summary>
    public Func<Uri, bool>? Filter { get; init; }

    internal static readonly WebViewBindingOptions Default = new();
}
