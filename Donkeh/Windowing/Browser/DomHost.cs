using System.Text;
using System.Text.Json;
using Donkeh.Data.Dom;
using Donkeh.Hosting;
using Donkeh.UILab;
using Microsoft.JSInterop;

namespace Donkeh.Windowing.Browser;

/// <summary>
/// Browser presentation host. Mounts an <see cref="IDonkehApp"/> into UILab and
/// mirrors the live virtual tree into real DOM elements.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <c>DonkehWindow</c>, this host never runs the layout engine or paints
/// into a framebuffer. The browser owns CSS layout, focus, carets, text selection,
/// scrolling, form controls and pseudo-classes. The host only keeps the DOM in
/// sync with the reconciled virtual tree and forwards events to the same
/// delegates a desktop host would invoke.
/// </para>
/// <para>
/// The bridge is intentionally stateful: each virtual node is assigned a stable
/// integer id, and each DOM node keeps the same id. A sync walks the live tree,
/// emits only the differences from the previous snapshot, and lets JavaScript
/// apply the batch. Virtual node identity is never lost, so keyed reorders move
/// existing DOM nodes instead of recreating them.
/// </para>
/// </remarks>
public sealed class DomHost
{
    private readonly IJSRuntime _js;
    private readonly Renderer _renderer = new();
    private readonly Dictionary<Node, int> _ids = [];
    private readonly Dictionary<int, Node> _nodes = [];
    private readonly Dictionary<Node, NodeSnapshot> _snapshots = [];
    private readonly object _syncGate = new();

    private IJSObjectReference? _module;
    private DotNetObjectReference<DomHost>? _self;
    private AppRootDefinition? _app;
    private int[] _rootChildren = [];
    private int _nextId = 1;
    private bool _started;
    private bool _syncQueued;
    private bool _syncing;

    /// <summary>Creates a host bound to the browser JavaScript runtime.</summary>
    /// <param name="js">The Blazor JavaScript runtime.</param>
    public DomHost(IJSRuntime js)
    {
        _js = js;
    }

    /// <summary>Gets the mounted application root, or null before <see cref="StartAsync"/>.</summary>
    public AppRootDefinition? App => _app;

    /// <summary>Gets the renderer that owns the application tree.</summary>
    public Renderer Renderer => _renderer;

    /// <summary>
    /// Starts the browser host: imports the DOM bridge, installs the application
    /// stylesheet and mounts the application tree.
    /// </summary>
    /// <param name="containerId">The id of the DOM element that receives the app.</param>
    public async Task StartAsync(string containerId = "app")
    {
        if (_started)
        {
            return;
        }

        _app = AppRootLocator.Find();
        _self = DotNetObjectReference.Create(this);

        // The bridge is an embedded resource, so consumers never have to know
        // about _content paths, static web assets or a wwwroot folder.
        _module = await _js.InvokeAsync<IJSObjectReference>(
            "import",
            BuildModuleDataUrl());

        await _module.InvokeVoidAsync("init", containerId, _self);
        Host.UsePermissions(new BrowserPermissionService(_module));

        var styles = _app.Styles;
        await _module.InvokeVoidAsync("setStyle", styles ?? string.Empty);

        var title = _app.Settings.Title;
        if (!string.IsNullOrWhiteSpace(title))
        {
            await _module.InvokeVoidAsync("setTitle", title);
        }

        _renderer.AfterUpdate += OnAfterUpdate;
        _renderer.Mount(_app.CreateRoot());
        _started = true;

        await SyncAsync();
    }

    /// <summary>Receives a DOM event from the browser bridge.</summary>
    /// <param name="handlerId">The nearest virtual node that owns the handler.</param>
    /// <param name="targetId">The deepest virtual node under the physical event target.</param>
    /// <param name="eventName">The normalised event name, such as <c>click</c>.</param>
    /// <param name="x">Event-local X coordinate, or a horizontal delta.</param>
    /// <param name="y">Event-local Y coordinate, a vertical delta, or a checked flag.</param>
    /// <param name="text">The key name, input value or pasted text, depending on the event.</param>
    /// <param name="value">Current control value for key events.</param>
    /// <returns>A task that completes once the handler has run.</returns>
    [JSInvokable("HandleEvent")]
    public Task HandleEventAsync(
        int handlerId,
        int targetId,
        string eventName,
        double x,
        double y,
        string? text,
        string? value)
    {
        if (!_nodes.TryGetValue(handlerId, out var handlerNode)
            || handlerNode is not ElementNode handler)
        {
            return Task.CompletedTask;
        }

        if (!_nodes.TryGetValue(targetId, out var targetNode))
        {
            targetNode = handlerNode;
        }

        switch (eventName)
        {
            case "click":
                InvokeAction(handler, "click");
                break;

            case "mouseover":
            case "mouseout":
            case "focus":
            case "blur":
            case "submit":
            case "play":
            case "pause":
            case "ended":
                InvokeAction(handler, eventName);
                break;

            case "pointerdown":
            case "pointermove":
            case "pointerup":
            case "scroll":
                InvokePointer(handler, eventName, (float)x, (float)y);
                break;

            case "contextmenu":
                InvokeContextMenu(handler, targetNode, (float)x, (float)y);
                break;

            case "input":
                if (targetNode is ElementNode inputTarget)
                {
                    ApplyControlState(inputTarget, text);
                }

                InvokeString(handler, "input", text ?? string.Empty);
                break;

            case "change":
                if (targetNode is ElementNode changeTarget)
                {
                    ApplyControlState(changeTarget, text);
                }

                InvokeString(handler, "change", text ?? string.Empty);
                break;

            case "paste":
                InvokeString(handler, "paste", text ?? string.Empty);
                break;

            case "keydown":
                var key = MapKey(text);
                InvokeString(handler, "keydown", key);

                // Desktop commits an edit and raises change on Enter even when a
                // keydown handler also runs, so the browser path does the same.
                if (key is "Enter" or "NumpadEnter"
                    && targetNode is ElementNode keyTarget
                    && IsTextControl(keyTarget))
                {
                    ApplyControlState(keyTarget, value);
                    InvokeString(keyTarget, "change", value ?? string.Empty);
                }

                break;

            case "timeupdate":
                if (handler.TryGetEventHandler("timeupdate", out var timeHandler)
                    && timeHandler is Action<float> timeAction)
                {
                    InvokeSafely(() => timeAction((float)x));
                }

                break;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the embedded DOM bridge and returns it as an ES module data URL.
    /// </summary>
    /// <remarks>
    /// A data URL keeps the bridge entirely inside the framework package. It
    /// avoids static web asset manifests, content roots and per-app wwwroot files,
    /// which are all host packaging concerns rather than application concerns.
    /// </remarks>
    private static string BuildModuleDataUrl()
    {
        const string resourceName = "Donkeh.Windowing.Browser.donkeh.js";

        using var stream = typeof(DomHost).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded DOM bridge '{resourceName}' was not found.");

        using var reader = new StreamReader(stream, Encoding.UTF8);
        var script = reader.ReadToEnd();
        var bytes = Encoding.UTF8.GetBytes(script);

        return "data:text/javascript;base64," + Convert.ToBase64String(bytes);
    }

    /// <summary>Marks the DOM as out of date after a renderer commit.</summary>
    private void OnAfterUpdate()
    {
        if (!_started)
        {
            return;
        }

        RequestSync();
    }

    /// <summary>Coalesces overlapping renderer updates into one DOM sync loop.</summary>
    private void RequestSync()
    {
        lock (_syncGate)
        {
            _syncQueued = true;

            if (_syncing)
            {
                return;
            }

            _syncing = true;
        }

        var syncTask = DrainSyncAsync();
        GC.KeepAlive(syncTask);
    }

    /// <summary>Applies every queued update until no newer tree is waiting.</summary>
    private async Task DrainSyncAsync()
    {
        try
        {
            while (true)
            {
                lock (_syncGate)
                {
                    if (!_syncQueued)
                    {
                        _syncing = false;
                        return;
                    }

                    _syncQueued = false;
                }

                await SyncAsync();
            }
        }
        catch (Exception exception)
        {
            lock (_syncGate)
            {
                _syncing = false;
            }

            Console.WriteLine($"[Donkeh] DOM sync failed: {exception.Message}");
        }
    }

    /// <summary>Walks the live tree and sends the changed DOM patches.</summary>
    private async Task SyncAsync()
    {
        if (_module is null || _renderer.Root is null)
        {
            return;
        }

        var batch = new DomPatchBatch();
        var seen = new HashSet<Node>();
        var rootChildren = new List<int>();

        Collect(_renderer.Root, rootChildren, seen, batch);

        if (!SequenceEqual(_rootChildren, rootChildren))
        {
            _rootChildren = [.. rootChildren];
            batch.Patches.Add(new DomPatch
            {
                Op = "children",
                Parent = 0,
                Children = [.. rootChildren]
            });
        }

        List<Node>? removed = null;

        foreach (var pair in _snapshots)
        {
            if (!seen.Contains(pair.Key))
            {
                (removed ??= []).Add(pair.Key);
            }
        }

        if (removed is not null)
        {
            foreach (var node in removed)
            {
                if (_ids.TryGetValue(node, out var id))
                {
                    batch.Patches.Add(new DomPatch { Op = "remove", Id = id });
                    _ids.Remove(node);
                    _nodes.Remove(id);
                }

                _snapshots.Remove(node);
            }
        }

        if (batch.Patches.Count == 0)
        {
            return;
        }

        var json = JsonSerializer.Serialize(batch, DomJsonContext.Default.DomPatchBatch);
        await _module.InvokeVoidAsync("apply", json);
    }

    /// <summary>Collects one live node and its flattened descendants.</summary>
    private void Collect(
        Node node,
        List<int> childIds,
        HashSet<Node> seen,
        DomPatchBatch batch)
    {
        seen.Add(node);

        // Fragments have no DOM counterpart, so their children occupy the
        // fragment's slot in the parent.
        if (node is FragmentNode fragment)
        {
            foreach (var child in fragment.Children)
            {
                if (child is Node childNode)
                {
                    Collect(childNode, childIds, seen, batch);
                }
            }

            return;
        }

        if (!_ids.TryGetValue(node, out var id))
        {
            id = _nextId++;
            _ids[node] = id;
            _nodes[id] = node;

            switch (node)
            {
                case TextNode text:
                    _snapshots[node] = new NodeSnapshot { IsText = true, Text = text.Text };
                    batch.Patches.Add(new DomPatch
                    {
                        Op = "create",
                        Id = id,
                        IsText = true,
                        Text = text.Text
                    });
                    break;

                case ElementNode element:
                    _snapshots[node] = new NodeSnapshot { Tag = element.Tag };
                    batch.Patches.Add(new DomPatch
                    {
                        Op = "create",
                        Id = id,
                        Tag = element.Tag
                    });
                    break;
            }
        }

        if (node is TextNode textNode)
        {
            var snapshot = _snapshots[node];

            if (!string.Equals(snapshot.Text, textNode.Text, StringComparison.Ordinal))
            {
                snapshot.Text = textNode.Text;
                batch.Patches.Add(new DomPatch
                {
                    Op = "text",
                    Id = id,
                    Text = textNode.Text
                });
            }
        }
        else if (node is ElementNode elementNode)
        {
            var snapshot = _snapshots[node];
            var attributes = BuildAttributes(elementNode);

            if (!AttributesEqual(snapshot.Attrs, attributes))
            {
                snapshot.Attrs = attributes;
                batch.Patches.Add(new DomPatch
                {
                    Op = "attrs",
                    Id = id,
                    Attrs = attributes
                });
            }

            var events = elementNode
                .EnumerateEventNames()
                .Distinct(StringComparer.Ordinal)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .ToList();

            if (!EventsEqual(snapshot.Events, events))
            {
                snapshot.Events = events;
                batch.Patches.Add(new DomPatch
                {
                    Op = "events",
                    Id = id,
                    Events = events
                });
            }

            // UILab folds an element whose whole child list is text into
            // TextContent instead of TextNode children. Mirror that folded text
            // directly; elements with real child nodes are handled below.
            var content = elementNode.TextContent;

            if (elementNode.Children.Length == 0 && content is not null)
            {
                if (!string.Equals(snapshot.Content, content, StringComparison.Ordinal))
                {
                    snapshot.Content = content;
                    batch.Patches.Add(new DomPatch
                    {
                        Op = "content",
                        Id = id,
                        Text = content
                    });
                }
            }
            else if (snapshot.Content is not null)
            {
                snapshot.Content = null;
                batch.Patches.Add(new DomPatch
                {
                    Op = "content",
                    Id = id,
                    Text = string.Empty
                });
            }

            var elementChildren = new List<int>();

            foreach (var child in elementNode.Children)
            {
                if (child is Node childNode)
                {
                    Collect(childNode, elementChildren, seen, batch);
                }
            }

            if (!SequenceEqual(snapshot.Children, elementChildren))
            {
                snapshot.Children = [.. elementChildren];
                batch.Patches.Add(new DomPatch
                {
                    Op = "children",
                    Id = id,
                    Children = [.. elementChildren]
                });
            }
        }

        childIds.Add(id);
    }

    /// <summary>Builds a full attribute map for an element.</summary>
    private static Dictionary<string, string> BuildAttributes(ElementNode element)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var pair in element.EnumerateAttributes())
        {
            attributes[pair.Key] = pair.Value;
        }

        return attributes;
    }

    /// <summary>Compares two attribute maps from the previous and current tree.</summary>
    private static bool AttributesEqual(
        Dictionary<string, string>? previous,
        Dictionary<string, string> current)
    {
        if (previous is null || previous.Count != current.Count)
        {
            return false;
        }

        foreach (var pair in current)
        {
            if (!previous.TryGetValue(pair.Key, out var value)
                || !string.Equals(value, pair.Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares event-name lists from the previous and current tree.</summary>
    private static bool EventsEqual(List<string>? previous, List<string> current)
    {
        if (previous is null || previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(previous[i], current[i], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Compares two child-id sequences.</summary>
    private static bool SequenceEqual(int[] previous, List<int> current)
    {
        if (previous.Length != current.Count)
        {
            return false;
        }

        for (var i = 0; i < previous.Length; i++)
        {
            if (previous[i] != current[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Updates the virtual control state that the browser changed.</summary>
    /// <param name="target">The control under the event.</param>
    /// <param name="value">The new text or checked state reported by the DOM.</param>
    private static void ApplyControlState(ElementNode target, string? value)
    {
        if (target.Tag == "textarea")
        {
            target.TextContent = value ?? string.Empty;
            return;
        }

        if (target.Tag != "input")
        {
            return;
        }

        var type = GetAttribute(target, "type");

        if (type is "checkbox" or "radio")
        {
            var isChecked = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
            target.IsChecked = isChecked;

            if (isChecked)
            {
                target.SetAttribute("checked", string.Empty);
            }
            else
            {
                target.RemoveAttribute("checked");
            }

            return;
        }

        if (type is not ("file" or "image" or "button" or "submit" or "reset"))
        {
            target.SetAttribute("value", value ?? string.Empty);
        }
    }

    /// <summary>Returns whether an element accepts typed text.</summary>
    private static bool IsTextControl(ElementNode element)
    {
        if (element.Tag == "textarea")
        {
            return true;
        }

        if (element.Tag != "input")
        {
            return false;
        }

        var type = GetAttribute(element, "type");
        return type is null or "text" or "search" or "email" or "url" or "tel"
            or "password" or "number";
    }

    /// <summary>Reads an attribute, treating a missing value as null.</summary>
    private static string? GetAttribute(ElementNode element, string name)
    {
        return element.TryGetAttribute(name, out var value) ? value : null;
    }

    /// <summary>Invokes a parameterless handler.</summary>
    private static void InvokeAction(ElementNode element, string eventName)
    {
        if (!element.TryGetEventHandler(eventName, out var handler) || handler is null)
        {
            return;
        }

        InvokeSafely(() =>
        {
            if (handler is Action action)
            {
                action();
            }
            else
            {
                handler.DynamicInvoke();
            }
        });
    }

    /// <summary>Invokes a handler that receives one string.</summary>
    private static void InvokeString(ElementNode element, string eventName, string value)
    {
        if (!element.TryGetEventHandler(eventName, out var handler) || handler is null)
        {
            return;
        }

        InvokeSafely(() =>
        {
            if (handler is Action<string> action)
            {
                action(value);
            }
            else
            {
                handler.DynamicInvoke(value);
            }
        });
    }

    /// <summary>Invokes a handler that receives two floats.</summary>
    private static void InvokePointer(ElementNode element, string eventName, float x, float y)
    {
        if (!element.TryGetEventHandler(eventName, out var handler) || handler is null)
        {
            return;
        }

        InvokeSafely(() =>
        {
            if (handler is Action<float, float> action)
            {
                action(x, y);
            }
            else
            {
                handler.DynamicInvoke(x, y);
            }
        });
    }

    /// <summary>Invokes a right-button context-menu handler with its target node.</summary>
    /// <param name="currentTarget">The virtual element that owns the handler.</param>
    /// <param name="targetNode">The deepest virtual node under the DOM event target.</param>
    /// <param name="x">Event-local X coordinate.</param>
    /// <param name="y">Event-local Y coordinate.</param>
    /// <remarks>
    /// The browser bridge reports element-local coordinates, so they are used for
    /// both the local and window positions. The desktop and Android hosts provide
    /// true window coordinates.
    /// </remarks>
    private static void InvokeContextMenu(ElementNode currentTarget, Node targetNode, float x, float y)
    {
        if (!currentTarget.TryGetEventHandler("contextmenu", out var handler)
            || handler is not (Action<ContextMenuEvent> or Action<float, float>))
        {
            return;
        }

        InvokeSafely(() =>
        {
            if (handler is Action<ContextMenuEvent> action)
            {
                var target = targetNode as ElementNode ?? currentTarget;
                action(new ContextMenuEvent(target, currentTarget, x, y, x, y));
            }
            else if (handler is Action<float, float> legacy)
            {
                legacy(x, y);
            }
        });
    }

    /// <summary>Runs an application handler without letting it tear down the host.</summary>
    private static void InvokeSafely(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Match the desktop host: a throwing event handler is contained.
        }
    }

    /// <summary>
    /// Maps a browser <c>event.key</c> value to the key names used by the
    /// desktop host.
    /// </summary>
    private static string MapKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        return key switch
        {
            " " => "Space",
            "ArrowLeft" => "Left",
            "ArrowRight" => "Right",
            "ArrowUp" => "Up",
            "ArrowDown" => "Down",
            "Esc" => "Escape",
            _ => key.Length == 1 && char.IsLetter(key[0])
                ? char.ToUpperInvariant(key[0]).ToString()
                : key
        };
    }

    /// <summary>Snapshot of the DOM-facing state of one virtual node.</summary>
    private sealed class NodeSnapshot
    {
        /// <summary>Whether this is a text node.</summary>
        public bool IsText;

        /// <summary>Element tag name, or null for a text node.</summary>
        public string? Tag;

        /// <summary>Text content for a text node.</summary>
        public string? Text;

        /// <summary>Last applied attribute map.</summary>
        public Dictionary<string, string>? Attrs;

        /// <summary>Last applied event-name list.</summary>
        public List<string>? Events;

        /// <summary>Last applied child-id order.</summary>
        public int[] Children = [];

        /// <summary>Last applied folded text content for an element.</summary>
        public string? Content;
    }
}
