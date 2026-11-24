namespace Donkeh.Windowing;

/// <summary>When an injected script runs relative to the page document.</summary>
public enum WebViewScriptTiming
{
    /// <summary>Run as soon as the frame's JavaScript context exists, before page scripts.</summary>
    DocumentStart,

    /// <summary>Run after the frame finishes loading.</summary>
    DocumentEnd,

    /// <summary>Not injected automatically; the plugin runs it explicitly.</summary>
    Manual
}

/// <summary>
/// A JavaScript snippet injected into webview frames, similar to an Electron
/// preload script.
/// </summary>
public sealed class WebViewScript
{
    /// <summary>Creates an injected script.</summary>
    public WebViewScript(
        string id,
        string source,
        WebViewScriptTiming runAt = WebViewScriptTiming.DocumentStart,
        string? url = null,
        Func<Uri, bool>? filter = null,
        bool mainFrameOnly = true,
        bool isolate = true,
        bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(source);

        Id = id;
        Source = source;
        RunAt = runAt;
        Url = url ?? "donkeh://script/" + id + ".js";
        Filter = filter;
        MainFrameOnly = mainFrameOnly;
        Isolate = isolate;
        Enabled = enabled;
    }

    /// <summary>Stable identity used for de-duplication and removal.</summary>
    public string Id { get; }

    /// <summary>JavaScript source to inject.</summary>
    public string Source { get; }

    /// <summary>When the script is injected.</summary>
    public WebViewScriptTiming RunAt { get; init; }

    /// <summary>Source URL reported to developer tools.</summary>
    public string? Url { get; init; }

    /// <summary>Optional predicate limiting which documents receive the script.</summary>
    public Func<Uri, bool>? Filter { get; init; }

    /// <summary>Whether only the main frame receives the script.</summary>
    public bool MainFrameOnly { get; init; }

    /// <summary>
    /// Wraps the source in an IIFE so its declarations do not leak into the
    /// page's global scope. Enabled by default.
    /// </summary>
    public bool Isolate { get; init; }

    /// <summary>Whether the script participates in automatic injection.</summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the source wrapped according to <see cref="Isolate"/>.</summary>
    public string GetInjectedSource() =>
        Isolate ? "(function(){\n" + Source + "\n})();" : Source;

    internal bool AppliesTo(Uri uri, bool isMainFrame)
    {
        if (!Enabled) return false;
        if (MainFrameOnly && !isMainFrame) return false;
        return Filter?.Invoke(uri) ?? true;
    }

    /// <summary>Creates a document-start script.</summary>
    public static WebViewScript Preload(string id, string source, Func<Uri, bool>? filter = null) =>
        new(id, source, WebViewScriptTiming.DocumentStart, filter: filter);

    /// <summary>Creates a document-end script.</summary>
    public static WebViewScript AfterLoad(string id, string source, Func<Uri, bool>? filter = null) =>
        new(id, source, WebViewScriptTiming.DocumentEnd, filter: filter);
}
