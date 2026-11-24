using System.Text.Json;

namespace Donkeh.Windowing;

/// <summary>
/// A message sent from page JavaScript through the injected bridge, or a
/// reply routed back to a JavaScript caller.
/// </summary>
public sealed class WebViewMessage
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    internal WebViewMessage(
        WebViewPluginContext context,
        string channel,
        string? payload,
        Uri? source,
        WebViewTab? tab,
        string? replyChannel)
    {
        Context = context;
        Channel = channel;
        Payload = payload;
        Source = source;
        Tab = tab;
        ReplyChannel = replyChannel;
    }

    /// <summary>Gets the context that received the message.</summary>
    public WebViewPluginContext Context { get; }

    /// <summary>Gets the channel name.</summary>
    public string Channel { get; }

    /// <summary>Gets the raw JSON payload, if any.</summary>
    public string? Payload { get; }

    /// <summary>Gets the URL of the frame that sent the message.</summary>
    public Uri? Source { get; }

    /// <summary>Gets the tab that sent the message.</summary>
    public WebViewTab? Tab { get; }

    internal string? ReplyChannel { get; }

    internal object? ReplyPayload { get; private set; }

    /// <summary>Deserializes the payload into a typed value.</summary>
    public T? Read<T>()
    {
        if (string.IsNullOrWhiteSpace(Payload)) return default;

        try { return JsonSerializer.Deserialize<T>(Payload!, JsonOptions); }
        catch { return default; }
    }

    /// <summary>Deserializes the payload using a specific JSON options object.</summary>
    public T? Read<T>(JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(Payload)) return default;

        try { return JsonSerializer.Deserialize<T>(Payload!, options); }
        catch { return default; }
    }

    /// <summary>
    /// Replies to a JavaScript <c>invoke</c> call. Does nothing for plain
    /// <c>send</c> messages.
    /// </summary>
    public void Reply(object? payload) => ReplyPayload = payload;

    internal static string Serialize(object? payload) =>
        payload is string text ? text : JsonSerializer.Serialize(payload, JsonOptions);
}

/// <summary>An object exposed to a webview's JavaScript context.</summary>
public sealed class WebViewObjectBinding
{
    /// <summary>Creates a binding for a .NET object.</summary>
    public WebViewObjectBinding(
        string name,
        object target,
        WebViewBindingOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(target);

        Name = name;
        Target = target;
        Options = options ?? WebViewBindingOptions.Default;
    }

    /// <summary>Gets the JavaScript global name.</summary>
    public string Name { get; }

    /// <summary>Gets the .NET object backing the binding.</summary>
    public object Target { get; }

    /// <summary>Gets the binding options.</summary>
    public WebViewBindingOptions Options { get; }
}
