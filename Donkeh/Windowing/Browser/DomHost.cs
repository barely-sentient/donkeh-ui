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

    // Scratch reused by every sync. Only one sync runs at a time (guarded by
    // _syncing), so a single set of buffers is enough and a steady-state sync
    // allocates nothing beyond the patches that actually changed.
    private readonly DomPatchBatch _batch = new();
    private readonly HashSet<Node> _seen = [];
    private readonly List<int> _rootChildIds = [];
    private readonly List<Node> _removed = [];
    private readonly List<List<int>> _childIdLists = [];
    private readonly List<string> _eventNames = [];
    private readonly Dictionary<string, string> _scratchAttributes = new(StringComparer.Ordinal);
    private int _childListDepth;

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
    /// <param name="modifiers">
    /// The modifier keys held during a key event, or zero elsewhere.
    /// </param>
    /// <returns>A task that completes once the handler has run.</returns>
    [JSInvokable("HandleEvent")]
    public Task HandleEventAsync(
        int handlerId,
        int targetId,
        string eventName,
        double x,
        double y,
        string? text,
        string? value,
        int modifiers = 0)
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

        var target = targetNode as ElementNode ?? handler;
        var pointerX = (float)x;
        var pointerY = (float)y;
        var eventModifiers = (KeyModifiers)modifiers;

        switch (eventName)
        {
            case "click":
                InvokeMouseEvent(handler, target, new ClickEvent(pointerX, pointerY, pointerX, pointerY, 0, eventModifiers));
                break;

            case "mousedown":
                InvokeMouseEvent(handler, target, new MouseDownEvent(pointerX, pointerY, pointerX, pointerY, 0, eventModifiers));
                break;

            case "mouseup":
                InvokeMouseEvent(handler, target, new MouseUpEvent(pointerX, pointerY, pointerX, pointerY, 0, eventModifiers));
                break;

            case "mouseover":
                InvokeMouseEvent(handler, target, new MouseOverEvent(pointerX, pointerY, pointerX, pointerY, eventModifiers));
                break;

            case "mouseout":
                InvokeMouseEvent(handler, target, new MouseOutEvent(pointerX, pointerY, pointerX, pointerY, eventModifiers));
                break;

            case "focus":
                InvokeEvent(handler, target, new FocusEvent());
                break;

            case "blur":
                InvokeEvent(handler, target, new BlurEvent());
                break;

            case "submit":
                InvokeEvent(handler, target, new SubmitEvent());
                break;

            case "play":
                InvokeEvent(handler, target, new PlayEvent());
                break;

            case "pause":
                InvokeEvent(handler, target, new PauseEvent());
                break;

            case "ended":
                InvokeEvent(handler, target, new EndedEvent());
                break;

            case "pointerdown":
                InvokeCoordinateEvent(
                    handler,
                    target,
                    new PointerDownEvent(pointerX, pointerY, pointerX, pointerY, 0, eventModifiers),
                    pointerX,
                    pointerY);
                break;

            case "pointermove":
                InvokeCoordinateEvent(
                    handler,
                    target,
                    new PointerMoveEvent(pointerX, pointerY, pointerX, pointerY, -1, eventModifiers),
                    pointerX,
                    pointerY);
                break;

            case "pointerup":
                InvokeCoordinateEvent(
                    handler,
                    target,
                    new PointerUpEvent(pointerX, pointerY, pointerX, pointerY, 0, eventModifiers),
                    pointerX,
                    pointerY);
                break;

            case "scroll":
                InvokeCoordinateEvent(
                    handler,
                    target,
                    new ScrollEvent(pointerX, pointerY, eventModifiers),
                    pointerX,
                    pointerY);
                break;

            case "contextmenu":
                InvokeContextMenu(handler, target, pointerX, pointerY, eventModifiers);
                break;

            case "input":
                if (target is ElementNode inputTarget)
                {
                    ApplyControlState(inputTarget, text);
                }

                InvokeEvent(handler, target, new InputEvent(text ?? string.Empty), text ?? string.Empty);
                break;

            case "change":
                if (target is ElementNode changeTarget)
                {
                    ApplyControlState(changeTarget, text);
                }

                InvokeEvent(handler, target, new ChangeEvent(text ?? string.Empty), text ?? string.Empty);
                break;

            case "paste":
                InvokeEvent(handler, target, new PasteEvent(text ?? string.Empty), text ?? string.Empty);
                break;

            case "keydown":
                var key = MapKey(text);
                var chord = KeyChord.Format(key, eventModifiers);
                InvokeEvent(handler, target, new KeyDownEvent(key, eventModifiers, chord));

                // Desktop commits an edit and raises change on Enter even when a
                // keydown handler also runs, so the browser path does the same.
                if (key is "Enter" or "NumpadEnter"
                    && target is ElementNode keyTarget
                    && IsTextControl(keyTarget))
                {
                    ApplyControlState(keyTarget, value);
                    InvokeEvent(keyTarget, keyTarget, new ChangeEvent(value ?? string.Empty), value ?? string.Empty);
                }

                break;

            case "timeupdate":
                InvokeTimeEvent(handler, target, new TimeUpdateEvent(pointerX));
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

        var batch = _batch;
        batch.Patches.Clear();

        var seen = _seen;
        seen.Clear();

        var rootChildren = _rootChildIds;
        rootChildren.Clear();

        _childListDepth = 0;
        Collect(_renderer.Root, rootChildren, seen, batch);

        if (!SequenceEqual(_rootChildren, rootChildren))
        {
            var children = rootChildren.ToArray();
            _rootChildren = children;
            batch.Patches.Add(new DomPatch
            {
                Op = "children",
                Parent = 0,
                Children = [.. children]
            });
        }

        var removed = _removed;
        removed.Clear();

        foreach (var pair in _snapshots)
        {
            if (!seen.Contains(pair.Key))
            {
                removed.Add(pair.Key);
            }
        }

        if (removed.Count > 0)
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
                // The scratch map is reused by the next element, so the patch and
                // the snapshot need their own copy of the changed values.
                var changed = new Dictionary<string, string>(attributes, StringComparer.Ordinal);
                snapshot.Attrs = changed;
                batch.Patches.Add(new DomPatch
                {
                    Op = "attrs",
                    Id = id,
                    Attrs = changed
                });
            }

            var events = CollectEventNames(elementNode);

            if (!EventsEqual(snapshot.Events, events))
            {
                var changed = new List<string>(events);
                snapshot.Events = changed;
                batch.Patches.Add(new DomPatch
                {
                    Op = "events",
                    Id = id,
                    Events = changed
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

            var elementChildren = RentChildIds();

            try
            {
                foreach (var child in elementNode.Children)
                {
                    if (child is Node childNode)
                    {
                        Collect(childNode, elementChildren, seen, batch);
                    }
                }

                if (!SequenceEqual(snapshot.Children, elementChildren))
                {
                    var changed = elementChildren.ToArray();
                    snapshot.Children = changed;
                    batch.Patches.Add(new DomPatch
                    {
                        Op = "children",
                        Id = id,
                        Children = [.. changed]
                    });
                }
            }
            finally
            {
                ReturnChildIds();
            }
        }

        childIds.Add(id);
    }

    /// <summary>
    /// Fills the reusable scratch map with an element's current attributes.
    /// </summary>
    /// <param name="element">The element to read.</param>
    /// <returns>The scratch map, valid until the next element is visited.</returns>
    /// <remarks>
    /// The map is only used for the comparison and is copied when a patch is
    /// emitted, which keeps an unchanged element from allocating a dictionary on
    /// every sync.
    /// </remarks>
    private Dictionary<string, string> BuildAttributes(ElementNode element)
    {
        var attributes = _scratchAttributes;
        attributes.Clear();

        foreach (var pair in element.EnumerateAttributes())
        {
            attributes[pair.Key] = pair.Value;
        }

        return attributes;
    }

    /// <summary>
    /// Collects an element's event names into the reusable scratch list, in
    /// ordinal order.
    /// </summary>
    /// <param name="element">The element whose event names are wanted.</param>
    /// <returns>The scratch list, valid until the next element is visited.</returns>
    /// <remarks>
    /// The event map is keyed by interned name id, so its names are already
    /// distinct; only the ordering that the bridge relies on has to be applied.
    /// The old LINQ pipeline allocated an iterator, a set, a buffer and the final
    /// list for every element on every sync.
    /// </remarks>
    private List<string> CollectEventNames(ElementNode element)
    {
        var names = _eventNames;
        names.Clear();

        foreach (var name in element.EnumerateEventNames())
        {
            names.Add(name);
        }

        if (names.Count > 1)
        {
            names.Sort(StringComparer.Ordinal);
        }

        return names;
    }

    /// <summary>
    /// Borrows a child-id list for one element, reusing the buffer assigned to the
    /// current nesting depth.
    /// </summary>
    /// <returns>An empty list owned by the caller until <see cref="ReturnChildIds"/>.</returns>
    /// <remarks>
    /// A parent keeps filling its own list while its children recurse, so each
    /// nesting depth needs a distinct buffer. The depth always returns to zero
    /// when a sync finishes, which keeps the pool bounded by tree depth.
    /// </remarks>
    private List<int> RentChildIds()
    {
        if (_childListDepth == _childIdLists.Count)
        {
            _childIdLists.Add([]);
        }

        var ids = _childIdLists[_childListDepth++];
        ids.Clear();
        return ids;
    }

    /// <summary>Returns the buffer borrowed by <see cref="RentChildIds"/>.</summary>
    private void ReturnChildIds() => _childListDepth--;

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

    /// <summary>Invokes a handler with a concrete event payload and an optional string value.</summary>
    private static void InvokeEvent(ElementNode currentTarget, ElementNode target, Event uiEvent, string? value = null)
    {
        if (!currentTarget.TryGetEventHandler(uiEvent.Type, out var handler) || handler is null)
        {
            return;
        }

        uiEvent.Target = target;
        uiEvent.CurrentTarget = currentTarget;

        InvokeSafely(() =>
        {
            if (uiEvent.TryInvoke(handler))
            {
                return;
            }

            if (handler is Action action)
            {
                action();
            }
            else if (value is not null && handler is Action<string> stringAction)
            {
                stringAction(value);
            }
            else if (value is null)
            {
                handler.DynamicInvoke();
            }
            else
            {
                handler.DynamicInvoke(value);
            }
        });
    }

    /// <summary>Invokes a mouse handler with client and element-local coordinates.</summary>
    private static void InvokeMouseEvent(ElementNode currentTarget, ElementNode target, MouseEvent uiEvent)
    {
        InvokeEvent(currentTarget, target, uiEvent);
    }

    /// <summary>Invokes a pointer or scroll handler that also accepts two floats.</summary>
    private static void InvokeCoordinateEvent(
        ElementNode currentTarget,
        ElementNode target,
        Event uiEvent,
        float x,
        float y)
    {
        if (!currentTarget.TryGetEventHandler(uiEvent.Type, out var handler) || handler is null)
        {
            return;
        }

        uiEvent.Target = target;
        uiEvent.CurrentTarget = currentTarget;

        InvokeSafely(() =>
        {
            if (uiEvent.TryInvoke(handler))
            {
                return;
            }

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

    /// <summary>Invokes a media timeupdate handler.</summary>
    private static void InvokeTimeEvent(ElementNode currentTarget, ElementNode target, TimeUpdateEvent uiEvent)
    {
        if (!currentTarget.TryGetEventHandler("timeupdate", out var handler) || handler is null)
        {
            return;
        }

        uiEvent.Target = target;
        uiEvent.CurrentTarget = currentTarget;

        InvokeSafely(() =>
        {
            if (uiEvent.TryInvoke(handler))
            {
                return;
            }

            if (handler is Action<float> action)
            {
                action(uiEvent.Time);
            }
            else
            {
                handler.DynamicInvoke(uiEvent.Time);
            }
        });
    }

    /// <summary>Invokes a right-button context-menu handler with its target node.</summary>
    /// <param name="currentTarget">The virtual element that owns the handler.</param>
    /// <param name="targetNode">The deepest virtual node under the DOM event target.</param>
    /// <param name="x">Event-local X coordinate.</param>
    /// <param name="y">Event-local Y coordinate.</param>
    /// <param name="modifiers">The modifier keys held during the event.</param>
    /// <remarks>
    /// The browser bridge reports element-local coordinates, so they are used for
    /// both the local and window positions. The desktop and Android hosts provide
    /// true window coordinates.
    /// </remarks>
    private static void InvokeContextMenu(
        ElementNode currentTarget,
        Node targetNode,
        float x,
        float y,
        KeyModifiers modifiers)
    {
        if (!currentTarget.TryGetEventHandler("contextmenu", out var handler) || handler is null)
        {
            return;
        }

        InvokeSafely(() =>
        {
            var target = targetNode as ElementNode ?? currentTarget;
            var contextMenuEvent = new ContextMenuEvent(target, currentTarget, x, y, x, y, modifiers);

            if (contextMenuEvent.TryInvoke(handler))
            {
                return;
            }

            if (handler is Action<float, float> legacy)
            {
                legacy(x, y);
            }
            else
            {
                handler.DynamicInvoke(x, y);
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
