using System.Reflection;
using System.Text.Json;
using CefSharp;
using CefSharp.OffScreen;
using CefSharp.JavascriptBinding;
using CefSharp.ModelBinding;

namespace Donkeh.Windowing;

/// <summary>
/// Per-tab plugin state: scripts, bindings, message handlers and injection.
/// All CefSharp types stay inside this class.
/// </summary>
internal sealed class WebViewPluginRuntime
{
    private const string BridgeChannel = "__reply";

    private readonly ChromiumWebBrowser _browser;
    private readonly WebViewHost.WebViewTabTrack _track;
    private readonly WebViewTab? _tab;
    private readonly WebViewPluginContext _context;
    private readonly List<WebViewPlugin> _plugins = [];
    private readonly List<WebViewScript> _scripts = [];
    private readonly List<WebViewObjectBinding> _bindings = [];
    private readonly Dictionary<string, List<Action<WebViewMessage>>> _handlers =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _boundNames = new(StringComparer.Ordinal);
    private bool _disposed;

    public WebViewPluginRuntime(ChromiumWebBrowser browser, WebViewHost.WebViewTabTrack track, WebViewTab? tab)
    {
        _browser = browser;
        _track = track;
        _tab = tab;
        View = tab is null ? new WebView(browser) : tab.View;
        _context = new WebViewPluginContext(this, tab);

        var configured = WebViewOptions.Current;

        foreach (var plugin in configured.Plugins)
        {
            if (!plugin.Enabled) continue;

            _plugins.Add(plugin);
            _scripts.AddRange(plugin.Scripts);
        }

        foreach (var script in configured.Scripts) _scripts.Add(script);
        foreach (var binding in configured.Bindings) _bindings.Add(binding);

        foreach (var pair in configured.MessageHandlers)
        {
            _handlers[pair.Key] = [.. pair.Value];
        }

        if (configured.EnablePluginBridge)
        {
            _scripts.Insert(0, WebViewScript.Preload("donkeh.bridge", BuildBridgeScript(configured.BridgeGlobalName)));
        }

        browser.JavascriptMessageReceived += OnJavascriptMessageReceived;

        // Registered objects are serialized into CEF's extraInfo when the
        // browser is created; without legacy binding they need an explicit
        // BindObjectAsync call from JavaScript instead.
        browser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;

        foreach (var plugin in _plugins.ToArray())
        {
            try { plugin.OnAttached(_context); }
            catch { }
        }

        foreach (var binding in _bindings.ToArray()) Bind(binding);
    }

    internal WebView View { get; }

    internal WebViewPluginContext Context => _context;

    internal IReadOnlyList<WebViewPlugin> Plugins => _plugins;

    internal IReadOnlyList<WebViewScript> Scripts => _scripts;

    internal IReadOnlyList<WebViewObjectBinding> Bindings => _bindings;

    private static string BuildBridgeScript(string globalName)
    {
        var name = string.IsNullOrWhiteSpace(globalName) ? "donkeh" : globalName.Trim();
        return """
        (function(){
          if (window.__donkehBridgeInstalled) return;
          window.__donkehBridgeInstalled = true;
          var pending = {};
          var seq = 0;
          function post(channel, payload, id, reply) {
            var envelope = { __donkeh: 1, channel: channel, payload: payload == null ? null : payload };
            if (id) envelope.id = id;
            if (reply) envelope.reply = true;
            var api = window.CefSharp;
            if (!api || typeof api.PostMessage !== 'function') {
              setTimeout(function(){ post(channel, payload, id, reply); }, 0);
              return;
            }
            api.PostMessage(JSON.stringify(envelope));
          }
          window["__DONKEH_GLOBAL__"] = {
            send: function(channel, payload) { post(channel, payload, null, false); },
            invoke: function(channel, payload) {
              var id = 'r' + (++seq);
              return new Promise(function(resolve, reject) {
                pending[id] = { resolve: resolve, reject: reject };
                post(channel, payload, id, true);
              });
            },
            on: function(channel, handler) {
              if (typeof handler !== 'function') return;
              var wrapped = function(e) { handler(e.detail); };
              handler.__donkehWrapped = wrapped;
              window.addEventListener('donkeh:' + channel, wrapped);
            },
            off: function(channel, handler) {
              var wrapped = handler && handler.__donkehWrapped;
              if (wrapped) window.removeEventListener('donkeh:' + channel, wrapped);
            }
          };
          window.addEventListener('donkeh:__reply', function(e) {
            var detail = e.detail || {};
            var entry = pending[detail.id];
            if (!entry) return;
            delete pending[detail.id];
            entry.resolve(detail.payload);
          });
        })();
        """.Replace("__DONKEH_GLOBAL__", name, StringComparison.Ordinal);
    }

    internal void AddPlugin(WebViewPlugin plugin)
    {
        ArgumentNullException.ThrowIfNull(plugin);
        if (_plugins.Any(p => string.Equals(p.Name, plugin.Name, StringComparison.Ordinal))) return;

        _plugins.Add(plugin);
        try { plugin.OnAttached(_context); }
        catch { }

        foreach (var script in plugin.Scripts) _scripts.Add(script);
    }

    internal void RemovePlugin(string name)
    {
        var plugin = _plugins.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        if (plugin is null) return;

        _plugins.Remove(plugin);
        foreach (var script in plugin.Scripts) _scripts.Remove(script);

        try { plugin.OnDetached(_context); }
        catch { }
    }

    internal void AddScript(WebViewScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        var existing = _scripts.FindIndex(s =>
            string.Equals(s.Id, script.Id, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0) _scripts[existing] = script;
        else _scripts.Add(script);
    }

    internal void RemoveScript(string id) =>
        _scripts.RemoveAll(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

    internal void RegisterObject(string name, object target, WebViewBindingOptions? options)
    {
        var binding = new WebViewObjectBinding(name, target, options);
        var existing = _bindings.FindIndex(b =>
            string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        if (existing >= 0) _bindings[existing] = binding;
        else _bindings.Add(binding);

        Bind(binding);
    }

    internal void UnregisterObject(string name)
    {
        _bindings.RemoveAll(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));

        if (!_boundNames.Remove(name)) return;
        try { _browser.JavascriptObjectRepository.UnRegister(name); }
        catch { }
    }

    internal void OnMessage(string channel, Action<WebViewMessage> handler)
    {
        if (!_handlers.TryGetValue(channel, out var handlers))
        {
            handlers = [];
            _handlers[channel] = handlers;
        }

        handlers.Add(handler);
    }

    internal void Emit(string channel, object? payload)
    {
        if (_disposed) return;

        var script = "window.dispatchEvent(new CustomEvent("
            + JsonSerializer.Serialize("donkeh:" + channel)
            + ",{detail:" + WebViewMessage.Serialize(payload) + "}));";

        try { _browser.GetMainFrame()?.ExecuteJavaScriptAsync(script); }
        catch { }
    }

    internal void OnContextCreated(CefSharp.IFrame frame)
    {
        Uri? uri = null;
        try { uri = new Uri(frame.Url); }
        catch { }

        foreach (var plugin in _plugins.ToArray())
        {
            if (!plugin.Enabled) continue;
            try { plugin.OnContextCreated(_context, uri ?? new Uri("about:blank"), true); }
            catch { }
        }

        Inject(WebViewScriptTiming.DocumentStart, frame, uri, true);
    }

    internal void OnLoadEnd(Uri? uri)
    {
        foreach (var plugin in _plugins.ToArray())
        {
            if (!plugin.Enabled) continue;
            try { plugin.OnLoad(_context, uri ?? new Uri("about:blank")); }
            catch { }
        }
    }

    internal void Inject(
        WebViewScriptTiming timing,
        CefSharp.IFrame frame,
        Uri? uri,
        bool isMainFrame)
    {
        if (_disposed) return;

        foreach (var script in _scripts.ToArray())
        {
            if (script.RunAt != timing) continue;
            if (uri is not null && !script.AppliesTo(uri, isMainFrame)) continue;

            try
            {
                frame.ExecuteJavaScriptAsync(script.GetInjectedSource(), script.Url, 0);
            }
            catch
            {
                // The frame can be released between the callback and injection.
            }
        }
    }

    private void Bind(WebViewObjectBinding binding)
    {
        try
        {
            var options = CreateBindingOptions(binding.Options);
            _browser.JavascriptObjectRepository.Register(binding.Name, binding.Target, options);
            _boundNames.Add(binding.Name);

            // Registrations made after browser creation need the explicit
            // async binding call; legacy bindings are automatic at startup.
            if (_browser.IsBrowserInitialized)
            {
                _browser.GetMainFrame()?.ExecuteJavaScriptAsync(
                    "CefSharp.BindObjectAsync(" + System.Text.Json.JsonSerializer.Serialize(binding.Name) + ");");
            }
        }
        catch
        {
            // Invalid bindings should not stop the browser from loading.
        }
    }

    private static BindingOptions CreateBindingOptions(WebViewBindingOptions options)
    {
        IJavascriptNameConverter converter = options.CamelCaseNames
            ? new CamelCaseJavascriptNameConverter()
            : new PassthroughNameConverter();

        var result = new BindingOptions { Binder = new DefaultBinder(converter) };

        if (options.Async) result.MethodInterceptor = new AsyncInterceptor();

        return result;
    }

    private void OnJavascriptMessageReceived(object? sender, JavascriptMessageReceivedEventArgs args)
    {
        var raw = args.Message?.ToString();
        if (string.IsNullOrWhiteSpace(raw)) return;

        Uri? source = null;
        try { source = new Uri(args.Frame?.Url ?? "about:blank"); }
        catch { }

        string channel = "message";
        string? payload = raw;
        string? replyId = null;

        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("__donkeh", out _))
            {
                if (root.TryGetProperty("channel", out var channelElement))
                {
                    channel = channelElement.GetString() ?? "message";
                }

                if (root.TryGetProperty("payload", out var payloadElement))
                {
                    payload = payloadElement.ValueKind == JsonValueKind.Null
                        ? null
                        : payloadElement.GetRawText();
                }

                if (root.TryGetProperty("reply", out var replyElement)
                    && replyElement.ValueKind == JsonValueKind.True
                    && root.TryGetProperty("id", out var idElement))
                {
                    replyId = idElement.GetString();
                }
            }
        }
        catch
        {
            // Treat non-JSON messages as plain messages on the default channel.
        }

        var message = new WebViewMessage(
            _context,
            channel,
            payload,
            source,
            _tab,
            replyId is null ? null : BridgeChannel);

        if (_handlers.TryGetValue(channel, out var handlers))
        {
            foreach (var handler in handlers.ToArray())
            {
                try { handler(message); }
                catch { }
            }
        }

        foreach (var plugin in _plugins.ToArray())
        {
            if (!plugin.Enabled) continue;
            try { plugin.OnMessage(message); }
            catch { }
        }

        if (replyId is not null)
        {
            Emit(BridgeChannel, new ReplyEnvelope(replyId, message.ReplyPayload));
        }
    }

    internal void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _browser.JavascriptMessageReceived -= OnJavascriptMessageReceived;

        foreach (var plugin in _plugins.ToArray())
        {
            try { plugin.OnDetached(_context); }
            catch { }
        }
    }

    private sealed record ReplyEnvelope(string Id, object? Payload);

    private sealed class AsyncInterceptor : IAsyncMethodInterceptor
    {
        public object Intercept(Func<object[], object> method, object[] parameters, string methodName) =>
            method(parameters);

        public async Task<object> InterceptAsync(
            Func<object[], object> method,
            object[] parameters,
            string methodName)
        {
            var result = await Task.Run(() => method(parameters)).ConfigureAwait(false);

            if (result is Task task)
            {
                await task.ConfigureAwait(false);
                var property = task.GetType().GetProperty("Result");
                return property?.GetValue(task)!;
            }

            return result!;
        }
    }

    private sealed class PassthroughNameConverter : IJavascriptNameConverter
    {
        public string ConvertToJavascript(MemberInfo member) => member.Name;

        public string ConvertReturnedObjectPropertyAndFieldToNameJavascript(MemberInfo member) =>
            member.Name;
    }
}
