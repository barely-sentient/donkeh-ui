using System.Globalization;
using Donkeh.Data;
using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Donkeh.Parser;
using Donkeh.Parser.Objects;
using Donkeh.UILab;

namespace Donkeh.Inspector;

/// <summary>Props for <see cref="InspectorPanel"/>.</summary>
/// <remarks>
/// Immutable init-only values passed once when the component is mounted. The
/// panel never replaces them; all changing state lives in
/// <see cref="DevToolsState"/>.
/// </remarks>
public sealed class InspectorPanelProps
{
    /// <summary>Gets the live state shared with the host window.</summary>
    /// <value>
    /// Required. The panel dereferences it during render without a null check.
    /// </value>
    /// <remarks>
    /// Must outlive the panel and be the same instance the host updates.
    /// </remarks>
    public DevToolsState State { get; init; } = null!;

    /// <summary>
    /// Gets a value indicating whether the panel owns its own native window.
    /// </summary>
    /// <remarks>
    /// Standalone panels use the full viewport, have no splitter, and are not
    /// hidden when <see cref="DevToolsDock.Window"/> is selected.
    /// </remarks>
    public bool Standalone { get; init; }
}

/// <summary>
/// The Chrome-style devtools panel. It is a normal Ignite component and reads
/// the live mounted DOM through <see cref="DevToolsState.AppRoot"/>.
/// </summary>
/// <remarks>
/// <para>
/// The panel owns no model of its own beyond transient editor state (the open
/// "Add property" row and its drafts, and a render tick). Everything else is read
/// from <see cref="InspectorPanelProps.State"/>, and every stylesheet mutation is
/// queued on that state for the host frame thread.
/// </para>
/// <para>
/// <see cref="Tick"/> is the entry point used by the host: it polls
/// <see cref="DevToolsState.Version"/> and only bumps the component's tick when
/// the version changed. Call it on the thread that owns the panel; the standalone
/// window calls it from its own frame callback.
/// </para>
/// <para>
/// Selection, hover and expansion use node identity
/// (<see cref="ReferenceEqualityComparer"/>) because <see cref="Node"/> does not
/// define value equality.
/// </para>
/// </remarks>
public sealed class InspectorPanel : Component<InspectorPanelProps>
{
    /// <summary>
    /// Renderer state setter captured from <c>State(0)</c> during render; null
    /// before the first render.
    /// </summary>
    private Action<int>? _setTick;

    /// <summary>
    /// Latest tick value mirrored from the hook; bumping it requests a render.
    /// </summary>
    private int _tick;

    /// <summary>
    /// State version of the last render, or -1 before the first render.
    /// </summary>
    private long _lastVersion = -1;

    /// <summary>Reused visited set for the per-render tree prune and node count.</summary>
    private readonly HashSet<Node> _liveNodes = new(ReferenceEqualityComparer.Instance);

    /// <summary>Number of logically reachable nodes from the last prune pass.</summary>
    private int _liveNodeCount;

    /// <summary>Selected element the style grouping was built for.</summary>
    private ElementNode? _stylesCacheElement;

    /// <summary>Element revision at the time the style grouping was built.</summary>
    private int _stylesCacheRevision;

    /// <summary>State version at the time the style grouping was built.</summary>
    private long _stylesCacheVersion;

    /// <summary>Table the style grouping was built from.</summary>
    private DataTable? _stylesCacheTable;

    /// <summary>Cached matched-rule grouping for the selected element.</summary>
    private StylesGrouping? _stylesCache;

    /// <summary>
    /// Key of the rule whose "Add property" row is open, or -1. Element styles use
    /// <see cref="ElementStyleAddKey"/> because their selector id is not knowable
    /// until the rule is created.
    /// </summary>
    private int _addingSelector = -1;

    /// <summary>Draft property text for the open add row.</summary>
    private string _addProperty = string.Empty;

    /// <summary>Draft value text for the open add row.</summary>
    private string _addValue = string.Empty;

    /// <summary>
    /// Sentinel selector key for the <c>element.style</c> group. Negative so it
    /// cannot collide with a real <see cref="DataTable"/> selector id.
    /// </summary>
    private const int ElementStyleAddKey = -2;

    /// <summary>Called by the host frame loop to pull fresh data into the panel.</summary>
    /// <remarks>
    /// No-op when the panel is not mounted or the state version is unchanged.
    /// Otherwise it records the version and increments the tick through the setter
    /// captured during render, which schedules another render. It does not touch
    /// the stylesheet or the DOM.
    /// </remarks>
    public void Tick()
    {
        if (!Mounted)
        {
            return;
        }

        var version = Props.State.Version;

        if (version == _lastVersion)
        {
            return;
        }

        _lastVersion = version;
        _setTick?.Invoke(++_tick);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// On the first render with a mounted root and no expansion state, the tree is
    /// opened down to the first branching point (at most eight levels), following
    /// lone fragment and provider nodes so a component's real HTML is visible
    /// immediately.
    /// </para>
    /// <para>
    /// Each render prunes expansion, selection and hover entries for nodes that
    /// have unmounted. The returned tree is a standard
    /// <c>div#ignite-devtools</c> whose classes encode standalone, dock and hidden
    /// state; the splitter is only emitted in docked mode and the colour picker
    /// only on the Elements tab.
    /// </para>
    /// <para>
    /// This method has side effects on <see cref="DevToolsState"/> (expansion and
    /// the cached version) and must run on the thread that owns the panel.
    /// </para>
    /// </remarks>
    public override Node Render()
    {
        var state = Props.State;
        var (tick, setTick) = State(0);
        _tick = tick;
        _setTick = setTick;

        PruneExpanded(state);

        if (_lastVersion < 0)
        {
            _lastVersion = state.Version;
        }

        if (state.AppRoot is not null && state.Expanded.Count == 0)
        {
            // Open the tree down to the first branching point, like DevTools
            // does with <html> and <body>. Components are followed through
            // their rendered output so their real HTML is visible immediately.
            Node? node = state.AppRoot;

            for (var depth = 0; depth < 8 && node is not null; depth++)
            {
                state.Expanded.Add(node);

                var children = LogicalChildren(node);

                // Fragments and providers are transparent in the tree, so look
                // through a lone one instead of stopping the chain.
                if (children.Length == 1 && children[0] is FragmentNode or ProviderNode)
                {
                    children = LogicalChildren(children[0]);
                }

                if (children.Length != 1)
                {
                    break;
                }

                node = children[0] is ElementNode or ComponentNode ? children[0] : null;
            }
        }

        var hidden = !state.Visible || (state.Dock == DevToolsDock.Window && !Props.Standalone);
        var className = "dt-root";

        if (Props.Standalone)
        {
            className += " dt-standalone";
        }
        else if (state.Dock == DevToolsDock.Right)
        {
            className += " dt-dock-right dt-dockw-" + state.DockRightWidth;
        }
        else
        {
            className += " dt-dock-bottom dt-dockh-" + state.DockBottomHeight;
        }

        if (hidden)
        {
            className += " dt-hidden";
        }

        Node? splitter = null;

        if (!Props.Standalone && state.Dock != DevToolsDock.Window)
        {
            splitter = state.Dock == DevToolsDock.Right
                ? _.H(
                    "div",
                    new Props
                    {
                        ClassName = "dt-splitter dt-splitter-right",
                        OnPointerDown = (x, y) =>
                        {
                            state.IsResizingDock = true;
                            ResizeRight(state);
                        },
                        OnPointerMove = (x, y) =>
                        {
                            if (state.IsResizingDock)
                            {
                                ResizeRight(state);
                            }
                        },
                        OnPointerUp = (x, y) =>
                        {
                            if (state.IsResizingDock)
                            {
                                ResizeRight(state);
                                state.IsResizingDock = false;
                            }
                        }
                    })
                : _.H(
                    "div",
                    new Props
                    {
                        ClassName = "dt-splitter dt-splitter-bottom",
                        OnPointerDown = (x, y) =>
                        {
                            state.IsResizingDock = true;
                            ResizeBottom(state);
                        },
                        OnPointerMove = (x, y) =>
                        {
                            if (state.IsResizingDock)
                            {
                                ResizeBottom(state);
                            }
                        },
                        OnPointerUp = (x, y) =>
                        {
                            if (state.IsResizingDock)
                            {
                                ResizeBottom(state);
                                state.IsResizingDock = false;
                            }
                        }
                    });
        }

        var picker = state.Tab == DevToolsTab.Elements && state.ColorPickerRuleIndex >= 0
            ? BuildColorPicker(state)
            : null;
        var bodyClass = state.Tab == DevToolsTab.Performance
            ? "dt-body dt-body-perf"
            : "dt-body";

        return _.H(
            "div",
            new Props { Id = "ignite-devtools", ClassName = className },
            BuildToolbar(state),
            _.H("div", new Props { ClassName = bodyClass }, BuildBody(state)),
            BuildStatusBar(state),
            splitter,
            picker);
    }

    /// <summary>Resizes the bottom dock from the current pointer position.</summary>
    /// <param name="state">
    /// The state to mutate; its <see cref="DevToolsState.PointerY"/> must be
    /// current.
    /// </param>
    /// <remarks>
    /// Height is <c>ViewportHeight - PointerY</c>, clamped to 160..min(900,
    /// ViewportHeight - 120) so the toolbar and status bar remain usable. No
    /// update is requested when the height is unchanged. Called on splitter
    /// pointer-down, move and up; hover moves only resize while
    /// <see cref="DevToolsState.IsResizingDock"/> is set.
    /// </remarks>
    private void ResizeBottom(DevToolsState state)
    {
        var max = Math.Clamp(state.ViewportHeight - 120, 160, 900);
        var height = Math.Clamp(
            state.ViewportHeight - (int)MathF.Round(state.PointerY),
            160,
            max);

        if (height == state.DockBottomHeight)
        {
            return;
        }

        state.DockBottomHeight = height;
        RequestUpdate();
    }

    /// <summary>Resizes the right dock from the current pointer position.</summary>
    /// <param name="state">
    /// The state to mutate; its <see cref="DevToolsState.PointerX"/> must be
    /// current.
    /// </param>
    /// <remarks>
    /// Width is <c>ViewportWidth - PointerX</c>, clamped to 280..min(1000,
    /// ViewportWidth - 320), leaving at least 320 px for the app. No update is
    /// requested when the width is unchanged.
    /// </remarks>
    private void ResizeRight(DevToolsState state)
    {
        var max = Math.Clamp(state.ViewportWidth - 320, 280, 1000);
        var width = Math.Clamp(
            state.ViewportWidth - (int)MathF.Round(state.PointerX),
            280,
            max);

        if (width == state.DockRightWidth)
        {
            return;
        }

        state.DockRightWidth = width;
        RequestUpdate();
    }

    /// <summary>Bumps the state version and immediately ticks the panel.</summary>
    /// <remarks>
    /// Used by local UI actions after mutating <see cref="DevToolsState"/>, so the
    /// change is visible without waiting for the host's next refresh. The actual
    /// rerender is scheduled by the component runtime.
    /// </remarks>
    private void RequestUpdate()
    {
        Props.State.Refresh();
        Tick();
    }

    // ---------------------------------------------------------------- toolbar

    /// <summary>
    /// Builds the tab strip, inspect toggle, dock buttons and close button.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>The toolbar node.</returns>
    /// <remarks>
    /// The inspect toggle clears <see cref="DevToolsState.Hovered"/> when it
    /// changes mode. The close button only sets
    /// <see cref="DevToolsState.Visible"/> to <see langword="false"/>; it does not
    /// shut down a standalone window.
    /// </remarks>
    private Node BuildToolbar(DevToolsState state)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-toolbar" },
            _.H(
                "div",
                new Props { ClassName = "dt-tabs" },
                BuildTab(state, DevToolsTab.Elements, "Elements"),
                BuildTab(state, DevToolsTab.Performance, "Performance"),
                BuildTab(state, DevToolsTab.Animations, "Animations")),
            _.H(
                "div",
                new Props
                {
                    ClassName = state.InspectMode
                        ? "dt-icon-btn dt-inspect-btn dt-inspect-active"
                        : "dt-icon-btn dt-inspect-btn",
                    Title = "Select an element in the page to inspect it",
                    OnClick = () =>
                    {
                        state.InspectMode = !state.InspectMode;
                        state.Hovered = null;
                        RequestUpdate();
                    }
                },
                _.H("span", new Props { ClassName = "dt-inspect-glyph" }, _.Text("\u2316"))),
            _.H("div", new Props { ClassName = "dt-spacer" }),
            BuildDockButton(state, DevToolsDock.Bottom),
            BuildDockButton(state, DevToolsDock.Right),
            BuildDockButton(state, DevToolsDock.Window),
            _.H(
                "div",
                new Props
                {
                    ClassName = "dt-icon-btn",
                    OnClick = () =>
                    {
                        state.Visible = false;
                        RequestUpdate();
                    }
                },
                _.H("span", new Props { ClassName = "dt-close" }, _.Text("\u00D7"))));
    }

    /// <summary>Builds one tab button and its click handler.</summary>
    /// <param name="state">
    /// The state whose <see cref="DevToolsState.Tab"/> is switched.
    /// </param>
    /// <param name="tab">The tab represented by the button.</param>
    /// <param name="label">The visible label.</param>
    /// <returns>The tab node.</returns>
    /// <remarks>
    /// Clicking closes the colour picker before switching tabs. The label is
    /// wrapped in a <c>span</c> because a flex container only lays out element
    /// children, not loose text.
    /// </remarks>
    private Node BuildTab(DevToolsState state, DevToolsTab tab, string label)
    {
        var active = state.Tab == tab;
        var className = active ? "dt-tab dt-tab-active" : "dt-tab";

        return _.H(
            "div",
            new Props
            {
                ClassName = className,
                OnClick = () =>
                {
                    state.ColorPickerRuleIndex = -1;
                    state.Tab = tab;
                    RequestUpdate();
                }
            },
            // A flex container only lays out element children, so the label
            // needs its own span rather than being merged into the tab's text.
            _.H("span", new Props { ClassName = "dt-tab-label" }, _.Text(label)));
    }

    /// <summary>
    /// Builds a toolbar button that docks the panel to the given edge.
    /// </summary>
    /// <param name="state">The state to update.</param>
    /// <param name="dock">Target dock; also selects the glyph style.</param>
    /// <returns>The button node.</returns>
    /// <remarks>
    /// Clicking sets <see cref="DevToolsState.Dock"/> and makes the panel visible.
    /// Selecting <see cref="DevToolsDock.Window"/> causes the host to open the
    /// standalone window on its next update.
    /// </remarks>
    private Node BuildDockButton(DevToolsState state, DevToolsDock dock)
    {
        Node glyph;

        if (dock == DevToolsDock.Bottom)
        {
            glyph = _.H(
                "div",
                new Props { ClassName = "dt-glyph" },
                _.H("div", new Props { ClassName = "dt-glyph-bar-bottom" }));
        }
        else if (dock == DevToolsDock.Right)
        {
            glyph = _.H(
                "div",
                new Props { ClassName = "dt-glyph" },
                _.H("div", new Props { ClassName = "dt-glyph-bar-right" }));
        }
        else
        {
            glyph = _.H(
                "div",
                new Props { ClassName = "dt-glyph-window" },
                _.H("div", new Props { ClassName = "dt-glyph-window-bar" }));
        }

        return _.H(
            "div",
            new Props
            {
                ClassName = "dt-icon-btn",
                OnClick = () =>
                {
                    state.Dock = dock;
                    state.Visible = true;
                    RequestUpdate();
                }
            },
            glyph);
    }

    // ------------------------------------------------------------------- body

    /// <summary>Builds the body for the active tab.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>
    /// A performance dashboard, an animation browser, or the two-pane DOM/style
    /// editor.
    /// </returns>
    private Node BuildBody(DevToolsState state)
    {
        if (state.Tab == DevToolsTab.Performance)
        {
            return BuildPerformance(state);
        }

        if (state.Tab == DevToolsTab.Animations)
        {
            return BuildAnimations(state);
        }

        return _.H(
            "div",
            new Props { ClassName = "dt-elements" },
            _.H("div", new Props { ClassName = "dt-dom-pane" }, BuildDomTree(state)),
            _.H("div", new Props { ClassName = "dt-styles-pane" }, BuildStyles(state)));
    }

    // --------------------------------------------------------------- dom tree

    /// <summary>Builds the flattened logical DOM rows for the mounted root.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>
    /// The row fragment, or a "No mounted tree." placeholder when
    /// <see cref="DevToolsState.AppRoot"/> is null.
    /// </returns>
    /// <remarks>
    /// Fragment and provider nodes are expanded in place rather than shown as
    /// rows, so the tree mirrors the logical DOM the app renders.
    /// </remarks>
    private Node BuildDomTree(DevToolsState state)
    {
        if (state.AppRoot is null)
        {
            return _.H("div", new Props { ClassName = "dt-dim" }, _.Text("No mounted tree."));
        }

        var rows = new List<Node?>();
        AddLogicalRows(state, [state.AppRoot], rows);
        return _.F(rows.ToArray());
    }

    /// <summary>Recursively appends rows for a list of logical nodes.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="nodes">Nodes to emit, in document order.</param>
    /// <param name="output">
    /// Destination list; may receive <see langword="null"/> entries from row
    /// builders for unsupported node types.
    /// </param>
    /// <remarks>
    /// Fragments and providers are transparent: their children are appended in
    /// place. Expansion state determines whether children are emitted at all.
    /// </remarks>
    private void AddLogicalRows(DevToolsState state, IReadOnlyList<Node> nodes, List<Node?> output)
    {
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            switch (node)
            {
                case FragmentNode fragment:
                    AddLogicalRows(state, fragment.VdomChildren, output);
                    break;

                case ProviderNode provider:
                    AddLogicalRows(state, provider.ProviderChildren, output);
                    break;

                default:
                    output.Add(BuildNodeRow(state, node));
                    break;
            }
        }
    }

    /// <summary>
    /// Builds the row for a single node based on its runtime type.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="node">The node to render.</param>
    /// <returns>
    /// The row, or <see langword="null"/> for unsupported node types.
    /// </returns>
    /// <remarks>
    /// Text, component and element nodes get dedicated rows; fragments and
    /// providers are flattened into their children.
    /// </remarks>
    private Node? BuildNodeRow(DevToolsState state, Node node)
    {
        switch (node)
        {
            case TextNode text:
                return BuildTextRow(state, text);

            case ComponentNode component:
                return BuildComponentRow(state, component);

            case ElementNode element:
                return BuildElementRow(state, element);

            case FragmentNode fragment:
            {
                var rows = new List<Node?>();
                AddLogicalRows(state, fragment.VdomChildren, rows);
                return _.F(rows.ToArray());
            }

            case ProviderNode provider:
            {
                var rows = new List<Node?>();
                AddLogicalRows(state, provider.ProviderChildren, rows);
                return _.F(rows.ToArray());
            }

            default:
                return null;
        }
    }

    /// <summary>Builds a quoted, non-expandable row for a text node.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="text">The text node to render.</param>
    /// <returns>The row node.</returns>
    /// <remarks>
    /// Text longer than 80 characters is truncated with an ellipsis.
    /// </remarks>
    private Node BuildTextRow(DevToolsState state, TextNode text)
    {
        var value = text.Text;

        if (value.Length > 80)
        {
            value = value[..80] + "\u2026";
        }

        return BuildRow(
            state,
            text,
            hasChildren: false,
            expanded: false,
            _.H("span", new Props { ClassName = "dt-text-node" }, _.Text("\"" + value + "\"")));
    }

    /// <summary>
    /// Builds a row for a mounted component and, when expanded, its output.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="component">The component node to render.</param>
    /// <returns>
    /// The row, or a row plus an indented children block when expanded.
    /// </returns>
    /// <remarks>
    /// The label shows the component type name plus a "component" badge. Children
    /// come from <see cref="ComponentNode.OutputVdom"/>; a component that has not
    /// rendered yet has none and therefore no caret.
    /// </remarks>
    private Node BuildComponentRow(DevToolsState state, ComponentNode component)
    {
        var name = component.ComponentType.Name;
        var expanded = state.Expanded.Contains(component);
        var output = component.OutputVdom;
        var hasChildren = output is not null;
        var children = new List<Node?>();

        if (expanded && output is not null)
        {
            AddLogicalRows(state, [output], children);
        }

        var label = _.F(
            _.H("span", new Props { ClassName = "dt-component" }, _.Text("<" + name + ">")),
            _.H("span", new Props { ClassName = "dt-component-badge" }, _.Text("component")));

        var row = BuildRow(state, component, hasChildren, expanded, label);

        if (!expanded || !hasChildren)
        {
            return row;
        }

        var block = new List<Node?>
        {
            row,
            _.H("div", new Props { ClassName = "dt-tree-children" }, _.F(children.ToArray()))
        };

        return _.F(block.ToArray());
    }

    /// <summary>
    /// Builds a row for an element, with its id, classes and up to eight
    /// attributes.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="element">The element to render.</param>
    /// <returns>
    /// The row, or a row, children block and closing-tag row when expanded.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Attributes named <c>data-ignite-*</c> are skipped because they are the
    /// inspector's own bookkeeping, and at most eight other attributes are shown
    /// to keep rows readable.
    /// </para>
    /// <para>
    /// Children are taken from the element's <c>VdomChildren</c> when present,
    /// falling back to the rendered child nodes.
    /// </para>
    /// </remarks>
    private Node BuildElementRow(DevToolsState state, ElementNode element)
    {
        var expanded = state.Expanded.Contains(element);
        var logical = RenderedChildren(element);
        var hasChildren = logical.Length > 0;
        var children = new List<Node?>();

        if (expanded)
        {
            AddLogicalRows(state, logical, children);
        }

        var tag = element.Tag;

        var label = new List<Node?>
        {
            _.H("span", new Props { ClassName = "dt-dim" }, _.Text("<")),
            _.H("span", new Props { ClassName = "dt-tag" }, _.Text(tag))
        };

        if (element.Id is { } id)
        {
            var value = id.StartsWith('#') ? id[1..] : id;
            label.Add(_.H("span", new Props { ClassName = "dt-attr" }, _.Text("\u00A0id=")));
            label.Add(_.H("span", new Props { ClassName = "dt-val" }, _.Text("\"" + value + "\"")));
        }

        var classes = element.ClassName;

        if (classes.Length > 0)
        {
            label.Add(_.H("span", new Props { ClassName = "dt-attr" }, _.Text("\u00A0class=")));
            label.Add(_.H("span", new Props { ClassName = "dt-val" }, _.Text("\"" + string.Join(' ', classes) + "\"")));
        }

        var attributeNames = element.InternedAttributeNames;
        var shown = 0;

        foreach (var nameId in attributeNames)
        {
            if (!element.TryGetAttribute(nameId, out var valueId))
            {
                continue;
            }

            var name = InternTable.GetString(nameId) ?? string.Empty;

            // The inspector tags elements it owns with a private attribute; it
            // is an implementation detail, not something to show in the tree.
            if (name.StartsWith("data-ignite-", StringComparison.Ordinal)
                || shown++ >= 8)
            {
                continue;
            }

            var value = InternTable.GetString(valueId) ?? string.Empty;

            label.Add(_.H("span", new Props { ClassName = "dt-attr" }, _.Text("\u00A0" + name)));
            label.Add(_.H("span", new Props { ClassName = "dt-dim" }, _.Text("=")));
            label.Add(_.H("span", new Props { ClassName = "dt-val" }, _.Text("\"" + value + "\"")));
        }

        label.Add(_.H("span", new Props { ClassName = "dt-dim" }, _.Text(">")));

        var row = BuildRow(state, element, hasChildren, expanded, _.F(label.ToArray()));

        if (!expanded || !hasChildren)
        {
            return row;
        }

        var block = new List<Node?>
        {
            row,
            _.H("div", new Props { ClassName = "dt-tree-children" }, _.F(children.ToArray())),
            _.H(
                "div",
                new Props { ClassName = "dt-tree-row" },
                _.H("span", new Props { ClassName = "dt-caret dt-caret-empty" }, _.Text(" ")),
                _.H("span", new Props { ClassName = "dt-dim" }, _.Text("</")),
                _.H("span", new Props { ClassName = "dt-tag" }, _.Text(tag)),
                _.H("span", new Props { ClassName = "dt-dim" }, _.Text(">")))
        };

        return _.F(block.ToArray());
    }

    /// <summary>
    /// Builds a DOM-tree row shell with caret, selection and hover handling.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="node">The node the row represents.</param>
    /// <param name="hasChildren">Whether the row gets an expand/collapse caret.</param>
    /// <param name="expanded">Whether the node is currently expanded.</param>
    /// <param name="content">The row's label content.</param>
    /// <returns>The row node.</returns>
    /// <remarks>
    /// Clicking selects the node by reference and closes the colour picker.
    /// Mouse-over sets <see cref="DevToolsState.Hovered"/> (used for the in-app
    /// highlight), and mouse-out only clears it if it still points at this node.
    /// The caret toggles the entry in <see cref="DevToolsState.Expanded"/>.
    /// </remarks>
    private Node BuildRow(DevToolsState state, Node node, bool hasChildren, bool expanded, Node content)
    {
        var selected = ReferenceEquals(state.Selected, node);
        var className = selected ? "dt-tree-row dt-tree-selected" : "dt-tree-row";
        var caret = hasChildren
            ? (expanded ? "\u25BE" : "\u25B8")
            : " ";

        return _.H(
            "div",
            new Props
            {
                ClassName = className,
                OnClick = () =>
                {
                    state.ColorPickerRuleIndex = -1;
                    state.Selected = node;
                    RequestUpdate();
                },
                OnMouseOver = () => state.Hovered = node,
                OnMouseOut = () =>
                {
                    if (ReferenceEquals(state.Hovered, node))
                    {
                        state.Hovered = null;
                    }
                }
            },
            _.H(
                "span",
                new Props
                {
                    ClassName = hasChildren ? "dt-caret" : "dt-caret dt-caret-empty",
                    OnClick = hasChildren
                        ? () =>
                        {
                            if (!state.Expanded.Add(node))
                            {
                                state.Expanded.Remove(node);
                            }

                            RequestUpdate();
                        }
                        : null
                },
                _.Text(caret)),
            content);
    }

    // ----------------------------------------------------------------- styles

    /// <summary>Builds the styles pane for the selected node.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>
    /// A placeholder when nothing suitable is selected, a component summary, or
    /// the matched-rule list plus computed values for an element.
    /// </returns>
    /// <remarks>
    /// <para>
    /// For elements, the pane always shows an <c>element.style</c> group first. It
    /// is populated from the inspector-created rule's matched declarations and
    /// uses the negative <see cref="ElementStyleAddKey"/> when its add row opens.
    /// </para>
    /// <para>
    /// Matched rules are grouped by origin and selector, then reversed so the
    /// highest-priority group is listed first. Computed values come from
    /// <see cref="DevToolsState.Layout"/>; when the element has no box, the pane
    /// explains that it is not laid out.
    /// </para>
    /// </remarks>
    private Node BuildStyles(DevToolsState state)
    {
        var selected = state.Selected;

        if (selected is null)
        {
            return _.H(
                "div",
                new Props { ClassName = "dt-dim" },
                _.Text("Select an element to inspect its styles."));
        }

        if (selected is ComponentNode component)
        {
            var rows = new List<Node?>
            {
                _.H(
                    "div",
                    new Props { ClassName = "dt-selected-head" },
                    _.Text("<" + component.ComponentType.Name + ">")),
                _.H("div", new Props { ClassName = "dt-section-title" }, _.Text("COMPONENT")),
                BuildComputedRow("type", component.ComponentType.FullName ?? component.ComponentType.Name),
                BuildComputedRow("renders", component.OutputVdom?.GetType().Name ?? "(not rendered)")
            };

            return _.F(rows.ToArray());
        }

        if (selected is not ElementNode element)
        {
            return _.H("div", new Props { ClassName = "dt-dim" }, _.Text("This node has no styles."));
        }

        var output = new List<Node?>
        {
            _.H(
                "div",
                new Props { ClassName = "dt-selected-head" },
                _.Text(ElementLabel(element))),
            _.H("div", new Props { ClassName = "dt-section-title" }, _.Text("STYLES"))
        };

        var table = state.Table;

        if (table is null)
        {
            output.Add(_.H("div", new Props { ClassName = "dt-dim" }, _.Text("No stylesheet.")));
        }
        else
        {
            var grouping = GetStyleGrouping(state, element, table);
            output.Add(BuildRuleGroup(state, grouping.ElementStyle));

            if (grouping.Groups.Count == 0)
            {
                output.Add(_.H("div", new Props { ClassName = "dt-dim" }, _.Text("No matching rules.")));
            }

            for (var i = 0; i < grouping.Groups.Count; i++)
            {
                output.Add(BuildRuleGroup(state, grouping.Groups[i]));
            }
        }

        output.Add(_.H("div", new Props { ClassName = "dt-section-title" }, _.Text("COMPUTED")));

        if (state.Layout is not null && state.Layout.TryGetBox(element, out var box))
        {
            var style = box.Style;

            output.Add(BuildComputedRow("display", Kebab(style.Display.ToString())));
            output.Add(BuildComputedRow("position", Kebab(style.Position.ToString())));
            output.Add(BuildComputedRow("width", Px(box.BorderBox.Width)));
            output.Add(BuildComputedRow("height", Px(box.BorderBox.Height)));
            output.Add(BuildComputedRow("margin", ThicknessValue(box.Margin)));
            output.Add(BuildComputedRow("padding", ThicknessValue(box.Padding)));
            output.Add(BuildComputedRow("border", ThicknessValue(box.Border)));
            output.Add(BuildComputedRow("color", FormatColor(style.Color), color: true));
            output.Add(BuildComputedRow("background-color", FormatColor(style.BackgroundColor), color: true));
            output.Add(BuildComputedRow("font-family", style.FontFamily));
            output.Add(BuildComputedRow("font-size", style.FontSize.ToString()));
            output.Add(BuildComputedRow("font-weight", style.FontWeight.ToString(CultureInfo.InvariantCulture)));
            output.Add(BuildComputedRow("line-height", style.LineHeight.ToString()));
            output.Add(BuildComputedRow("flex-direction", Kebab(style.FlexDirection.ToString())));
            output.Add(BuildComputedRow("justify-content", Kebab(style.JustifyContent.ToString())));
            output.Add(BuildComputedRow("align-items", Kebab(style.AlignItems.ToString())));
            output.Add(BuildComputedRow("row-gap", style.RowGap.ToString()));
            output.Add(BuildComputedRow("column-gap", style.ColumnGap.ToString()));
            output.Add(BuildComputedRow("overflow-x", Kebab(style.OverflowX.ToString())));
            output.Add(BuildComputedRow("overflow-y", Kebab(style.OverflowY.ToString())));
            output.Add(BuildComputedRow("opacity", style.Opacity.ToString("0.##", CultureInfo.InvariantCulture)));
            output.Add(BuildComputedRow("z-index", style.HasZIndex ? style.ZIndex.ToString(CultureInfo.InvariantCulture) : "auto"));
            output.Add(BuildComputedRow("box", $"{Px(box.BorderBox.X)} {Px(box.BorderBox.Y)} {Px(box.BorderBox.Width)} {Px(box.BorderBox.Height)}"));
        }
        else
        {
            output.Add(_.H("div", new Props { ClassName = "dt-dim" }, _.Text("No layout box (display:none or not laid out).")));
        }

        return _.F(output.ToArray());
    }

    /// <summary>
    /// Builds or reuses the matched-rule grouping for the selected element.
    /// </summary>
    /// <param name="state">Inspector state supplying the cascade context.</param>
    /// <param name="element">The selected element.</param>
    /// <param name="table">The stylesheet table being inspected.</param>
    /// <returns>The cached grouping, rebuilt when the element, its revision, the
    /// table or the state version changes.</returns>
    /// <remarks>
    /// Matching, origin lookup, selector text and interned declaration strings are
    /// the expensive part of the Styles pane. Node construction still runs every
    /// render so selection and edit controls stay live.
    /// </remarks>
    private StylesGrouping GetStyleGrouping(DevToolsState state, ElementNode element, DataTable table)
    {
        if (_stylesCache is not null
            && ReferenceEquals(_stylesCacheElement, element)
            && _stylesCacheRevision == element.Revision
            && _stylesCacheVersion == state.Version
            && ReferenceEquals(_stylesCacheTable, table))
        {
            return _stylesCache;
        }

        var matches = table.MatchRules(element, state.Context);
        var hasElementStyle = state.TryGetElementStyleSelector(element, out var elementStyleSelectorId);
        var elementStyle = new RuleGroup(
            "inspector",
            "element.style",
            elementStyleSelectorId,
            -1,
            -1,
            isElementStyle: true)
        {
            Element = element
        };

        if (hasElementStyle)
        {
            for (var i = 0; i < matches.Length; i++)
            {
                var match = matches[i];

                if (match.SelectorId == elementStyleSelectorId)
                {
                    elementStyle.Declarations.Add(new Declaration(
                        match.Order,
                        InternTable.GetString(match.Rule.PropertyId) ?? "?",
                        InternTable.GetString(match.Rule.ValueId) ?? "?"));
                }
            }
        }

        var groups = new List<RuleGroup>();
        var index = new Dictionary<(string Origin, int Selector), RuleGroup>();

        for (var i = 0; i < matches.Length; i++)
        {
            var match = matches[i];

            if (hasElementStyle && match.SelectorId == elementStyleSelectorId)
            {
                continue;
            }

            var origin = OriginFor(state, match.Order);

            if (!index.TryGetValue((origin, match.SelectorId), out var group))
            {
                group = new RuleGroup(
                    origin,
                    SelectorText(table, match.SelectorId),
                    match.SelectorId,
                    match.Rule.ConditionId,
                    match.Rule.LayerId);
                index[(origin, match.SelectorId)] = group;
                groups.Add(group);
            }

            group.Declarations.Add(new Declaration(
                match.Order,
                InternTable.GetString(match.Rule.PropertyId) ?? "?",
                InternTable.GetString(match.Rule.ValueId) ?? "?"));
        }

        groups.Reverse();

        _stylesCacheElement = element;
        _stylesCacheRevision = element.Revision;
        _stylesCacheVersion = state.Version;
        _stylesCacheTable = table;
        _stylesCache = new StylesGrouping(elementStyle, groups);
        return _stylesCache;
    }

    /// <summary>
    /// Builds one rule card with its selector, origin, declarations and add row.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The declarations to render.</param>
    /// <returns>The rule card node.</returns>
    /// <remarks>
    /// The card shows its add row when <see cref="_addingSelector"/> matches the
    /// group's key; element styles use <see cref="ElementStyleAddKey"/> instead of
    /// a selector id.
    /// </remarks>
    private Node BuildRuleGroup(DevToolsState state, RuleGroup group)
    {
        var declarations = new List<Node?>();

        foreach (var declaration in group.Declarations)
        {
            declarations.Add(BuildDeclaration(state, group, declaration));
        }

        var addKey = group.IsElementStyle ? ElementStyleAddKey : group.SelectorId;

        if (_addingSelector == addKey)
        {
            declarations.Add(BuildAddRow(state, group));
        }
        else
        {
            declarations.Add(
                _.H(
                    "div",
                    new Props { ClassName = "dt-add-row" },
                    _.H(
                        "div",
                        new Props
                        {
                            ClassName = "dt-add",
                            OnClick = () => BeginAdd(group)
                        },
                        _.Text("+ Add property"))));
        }

        return _.H(
            "div",
            new Props { ClassName = "dt-rule" },
            _.H(
                "div",
                new Props { ClassName = "dt-rule-head" },
                _.H("span", new Props { ClassName = "dt-selector" }, _.Text(group.Selector)),
                _.H("span", new Props { ClassName = "dt-origin" }, _.Text(group.Origin))),
            _.F(declarations.ToArray()));
    }

    /// <summary>Builds one editable property/value declaration row.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule containing the declaration.</param>
    /// <param name="declaration">The declaration to render.</param>
    /// <returns>The declaration row node.</returns>
    /// <remarks>
    /// The property input commits on change (Enter or focus loss). The value input
    /// commits live on input when the text looks complete and again on change, so
    /// valid values update the app as the user types. Values that parse as colours
    /// get a swatch that opens the picker.
    /// </remarks>
    private Node BuildDeclaration(DevToolsState state, RuleGroup group, Declaration declaration)
    {
        var parts = new List<Node?>();
        var isColor = ColorPreview.IsColor(declaration.Value);

        parts.Add(_.H("input", new Props
        {
            ClassName = "dt-prop-input",
            Type = "text",
            Value = declaration.Property,
            OnChange = value => CommitProperty(state, declaration.RuleIndex, value)
        }));
        parts.Add(_.H("span", new Props { ClassName = "dt-colon" }, _.Text(":")));

        if (isColor)
        {
            parts.Add(BuildSwatch(
                declaration.Value,
                () => OpenColorPicker(state, declaration.RuleIndex, declaration.Value)));
        }

        parts.Add(_.H("input", new Props
        {
            ClassName = "dt-value-input",
            Type = "text",
            Value = declaration.Value,
            OnInput = value => CommitValueLive(state, group, declaration.RuleIndex, value),
            OnChange = value => CommitValue(state, group, declaration.RuleIndex, value)
        }));
        parts.Add(_.H("span", new Props { ClassName = "dt-dim" }, _.Text(";")));

        return _.H("div", new Props { ClassName = "dt-decl" }, _.F(parts.ToArray()));
    }

    /// <summary>Builds the inline "property: value" entry row.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule that will receive the declaration.</param>
    /// <returns>The add row node.</returns>
    /// <remarks>
    /// Drafts are kept in <see cref="_addProperty"/> and <see cref="_addValue"/>.
    /// Committing happens on the value field's change event or the plus button.
    /// Pasting into the property field parses one or more declarations, or a whole
    /// rule block, and applies them to the same group.
    /// </remarks>
    private Node BuildAddRow(DevToolsState state, RuleGroup group)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-decl dt-add-row" },
            _.H("input", new Props
            {
                ClassName = "dt-prop-input dt-add-input",
                Type = "text",
                Placeholder = "property",
                Value = _addProperty,
                OnInput = value => _addProperty = value,
                OnChange = value => _addProperty = value,
                OnPaste = text => PasteDeclarations(state, group, text)
            }),
            _.H("span", new Props { ClassName = "dt-colon" }, _.Text(":")),
            _.H("input", new Props
            {
                ClassName = "dt-value-input dt-add-input",
                Type = "text",
                Placeholder = "value",
                Value = _addValue,
                OnInput = value => _addValue = value,
                OnChange = value =>
                {
                    _addValue = value;
                    CommitAdd(state, group);
                }
            }),
            _.H(
                "div",
                new Props { ClassName = "dt-add", OnClick = () => CommitAdd(state, group) },
                _.Text("+")));
    }

    /// <summary>Builds a colour swatch element for the given value.</summary>
    /// <param name="value">
    /// The CSS colour text, stored as the <c>data-color</c> attribute.
    /// </param>
    /// <param name="onClick">
    /// Optional click handler; <see langword="null"/> for a static swatch.
    /// </param>
    /// <returns>The swatch node.</returns>
    /// <remarks>
    /// The engine has no inline styles, so the host paints the background by
    /// reading <c>data-color</c>; the value must therefore remain valid CSS.
    /// </remarks>
    private static Node BuildSwatch(string value, Action? onClick = null)
    {
        return _.H(
            "span",
            new Props
            {
                ClassName = "dt-swatch",
                Attributes = new Dictionary<string, string> { ["data-color"] = value },
                OnClick = onClick
            });
    }

    /// <summary>
    /// Preset swatches shown in the colour picker, in display order.
    /// </summary>
    private static readonly string[] PresetColors =
    [
        "#000000", "#ffffff", "#212529", "#6c757d", "#adb5bd", "#e9ecef",
        "#0d6efd", "#6610f2", "#6f42c1", "#d63384", "#dc3545", "#fd7e14",
        "#ffc107", "#198754", "#20c997", "#0dcaf0"
    ];

    /// <summary>Builds the modal colour picker popup and its backdrop.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>A fragment containing the backdrop and the popup.</returns>
    /// <remarks>
    /// The wheel is a fixed 110x110 circle whose pointer events map angle to hue
    /// and distance to saturation at full value. The hex field accepts any value
    /// <see cref="ColorPreview"/> understands. Presets are laid out eight per row.
    /// </remarks>
    private Node BuildColorPicker(DevToolsState state)
    {
        var ruleIndex = state.ColorPickerRuleIndex;
        var value = state.ColorPickerValue;

        var backdrop = _.H(
            "div",
            new Props
            {
                ClassName = "dt-color-backdrop",
                OnClick = () => CloseColorPicker(state)
            });

        var header = _.H(
            "div",
            new Props { ClassName = "dt-color-head" },
            _.H("span", new Props { ClassName = "dt-color-title" }, _.Text("Color")),
            _.H(
                "div",
                new Props { ClassName = "dt-color-close", OnClick = () => CloseColorPicker(state) },
                _.Text("\u00D7")));

        var wheel = _.H(
            "div",
            new Props
            {
                ClassName = "dt-color-wheel",
                Attributes = new Dictionary<string, string> { ["data-color"] = value },
                OnPointerDown = (x, y) => UpdateColorWheel(state, ruleIndex, x, y),
                OnPointerMove = (x, y) => UpdateColorWheel(state, ruleIndex, x, y),
                OnPointerUp = (x, y) => UpdateColorWheel(state, ruleIndex, x, y)
            });

        var preview = _.H(
            "div",
            new Props
            {
                ClassName = "dt-swatch dt-color-preview",
                Attributes = new Dictionary<string, string> { ["data-color"] = value }
            });

        var hex = _.H(
            "input",
            new Props
            {
                ClassName = "dt-color-hex",
                Type = "text",
                Value = value,
                OnInput = text => CommitColor(state, ruleIndex, text),
                OnChange = text => CommitColor(state, ruleIndex, text)
            });

        var valueRow = _.H(
            "div",
            new Props { ClassName = "dt-color-value-row" },
            preview,
            hex);

        var rows = new List<Node?>();

        for (var start = 0; start < PresetColors.Length; start += 8)
        {
            var row = new List<Node?>();

            for (var i = start; i < Math.Min(start + 8, PresetColors.Length); i++)
            {
                var color = PresetColors[i];
                row.Add(BuildSwatch(color, () =>
                {
                    CommitColor(state, ruleIndex, color);
                    CloseColorPicker(state);
                }));
            }

            rows.Add(_.H(
                "div",
                new Props { ClassName = "dt-color-row" },
                _.F(row.ToArray())));
        }

        var grid = _.H(
            "div",
            new Props { ClassName = "dt-color-grid" },
            _.F(rows.ToArray()));

        var popup = _.H(
            "div",
            new Props { ClassName = "dt-color-popup" },
            header,
            wheel,
            valueRow,
            grid);

        return _.F(backdrop, popup);
    }

    /// <summary>
    /// Maps a pointer position on the colour wheel to a hue/saturation colour.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="ruleIndex">The rule being edited.</param>
    /// <param name="x">Pointer X relative to the 110 px wheel.</param>
    /// <param name="y">Pointer Y relative to the 110 px wheel.</param>
    /// <remarks>
    /// Hue comes from <c>atan2</c> around the centre and saturation from the
    /// distance divided by the usable radius (51 px); value is fixed at 1. Every
    /// move queues an edit, though identical interned values are skipped by
    /// <see cref="CommitColor"/>.
    /// </remarks>
    private void UpdateColorWheel(DevToolsState state, int ruleIndex, float x, float y)
    {
        const float size = 110f;
        const float center = size / 2f;
        var dx = x - center;
        var dy = y - center;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        var saturation = Math.Clamp(distance / (center - 4f), 0, 1);
        var hue = MathF.Atan2(dy, dx) / (MathF.PI * 2f);

        if (hue < 0)
        {
            hue += 1;
        }

        CommitColor(state, ruleIndex, ColorPreview.Format(
            ColorPreview.FromHsv(hue, saturation, 1)));
    }

    /// <summary>Opens the colour picker for a declaration.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="ruleIndex">
    /// Index of the rule in <see cref="DevToolsState.Table"/>.
    /// </param>
    /// <param name="value">The current declaration value.</param>
    /// <remarks>
    /// The value is normalised through <see cref="ColorPreview"/> so named colours
    /// appear as hex; unparseable text is kept verbatim.
    /// </remarks>
    private void OpenColorPicker(DevToolsState state, int ruleIndex, string value)
    {
        state.ColorPickerRuleIndex = ruleIndex;
        state.ColorPickerValue = ColorPreview.TryParse(value, out var argb)
            ? ColorPreview.Format(argb)
            : value;
        RequestUpdate();
    }

    /// <summary>Closes the colour picker without reverting committed values.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <remarks>
    /// Sets <see cref="DevToolsState.ColorPickerRuleIndex"/> to -1 and requests a
    /// render. Edits already queued remain queued.
    /// </remarks>
    private void CloseColorPicker(DevToolsState state)
    {
        state.ColorPickerRuleIndex = -1;
        RequestUpdate();
    }

    /// <summary>Commits a colour value to the rule being edited.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="ruleIndex">
    /// Index of the rule in <see cref="DevToolsState.Table"/>.
    /// </param>
    /// <param name="value">The colour text to commit.</param>
    /// <remarks>
    /// Invalid text is ignored. Valid text updates
    /// <see cref="DevToolsState.ColorPickerValue"/> immediately and queues a rule
    /// value change; the edit is skipped when the interned value is unchanged.
    /// <paramref name="ruleIndex"/> must still be valid for the current table; it
    /// is not re-validated here.
    /// </remarks>
    private void CommitColor(DevToolsState state, int ruleIndex, string value)
    {
        if (state.Table is not { } table || !ColorPreview.IsColor(value))
        {
            return;
        }

        var text = value.Trim();
        state.ColorPickerValue = text;
        var valueId = InternTable.Intern(text);

        state.QueueEdit(() =>
        {
            if (table.GetRule(ruleIndex).ValueId == valueId)
            {
                return;
            }

            table.SetRuleValue(ruleIndex, valueId);
            state.NotifyStylesChanged();
        });
    }

    /// <summary>Opens the add row for a rule and prepares an empty draft.</summary>
    /// <param name="group">The rule that will receive the declaration.</param>
    /// <remarks>
    /// Records the group key, clears both drafts and asks the host to focus the
    /// next <c>dt-add-input</c> through
    /// <see cref="DevToolsState.PendingFocusClass"/>.
    /// </remarks>
    private void BeginAdd(RuleGroup group)
    {
        _addingSelector = group.IsElementStyle ? ElementStyleAddKey : group.SelectorId;
        _addProperty = string.Empty;
        _addValue = string.Empty;
        Props.State.PendingFocusClass = "dt-add-input";
        RequestUpdate();
    }

    /// <summary>Commits an edited declaration value to the stylesheet.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule being edited; used when advancing.</param>
    /// <param name="ruleIndex">
    /// Index of the rule in <see cref="DevToolsState.Table"/>.
    /// </param>
    /// <param name="value">The raw text from the value input.</param>
    /// <remarks>
    /// Trims surrounding whitespace and trailing semicolons; empty text is
    /// ignored. The edit is queued and skipped when the interned value is
    /// unchanged. Consumes <see cref="DevToolsState.AdvanceAfterCommit"/>; when it
    /// was set, a fresh add row is opened for the same group.
    /// </remarks>
    private void CommitValue(DevToolsState state, RuleGroup group, int ruleIndex, string value)
    {
        var advance = state.AdvanceAfterCommit;
        state.AdvanceAfterCommit = false;

        if (state.Table is not { } table)
        {
            return;
        }

        var text = value.Trim().TrimEnd(';').Trim();

        if (text.Length == 0)
        {
            return;
        }

        var valueId = InternTable.Intern(text);

        state.QueueEdit(() =>
        {
            if (table.GetRule(ruleIndex).ValueId == valueId)
            {
                return;
            }

            table.SetRuleValue(ruleIndex, valueId);
            state.NotifyStylesChanged();
        });

        if (advance)
        {
            BeginAdd(group);
        }
    }

    /// <summary>Commits a value from an input event when it looks complete.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule being edited.</param>
    /// <param name="ruleIndex">
    /// Index of the rule in <see cref="DevToolsState.Table"/>.
    /// </param>
    /// <param name="value">The current input text.</param>
    /// <remarks>
    /// Throttles live editing through <see cref="LooksComplete"/> so partially
    /// typed functions such as <c>rgb(</c> are not applied. Final validation is
    /// left to the stylesheet parser.
    /// </remarks>
    private void CommitValueLive(DevToolsState state, RuleGroup group, int ruleIndex, string value)
    {
        if (!LooksComplete(value))
        {
            return;
        }

        CommitValue(state, group, ruleIndex, value);
    }

    /// <summary>Performs a cheap check for obviously incomplete CSS values.</summary>
    /// <param name="value">The candidate value text.</param>
    /// <returns>
    /// <see langword="true"/> when quotes and parentheses are balanced and the
    /// text is not a lone prefix.
    /// </returns>
    /// <remarks>
    /// Rejects empty text, the partial tokens <c>-</c>, <c>--</c>, <c>.</c> and
    /// <c>#</c>, and any text ending in <c>;</c>. This is a heuristic, not a CSS
    /// validator.
    /// </remarks>
    private static bool LooksComplete(string value)
    {
        var text = value.Trim();

        if (text.Length == 0
            || text is "-" or "--" or "." or "#"
            || text.EndsWith(';'))
        {
            return false;
        }

        var open = 0;
        var quote = '\0';

        foreach (var c in text)
        {
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '(')
            {
                open++;
            }
            else if (c == ')')
            {
                if (open == 0)
                {
                    return false;
                }

                open--;
            }
        }

        return open == 0 && quote == '\0';
    }

    /// <summary>Commits an edited property name to the stylesheet.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="ruleIndex">
    /// Index of the rule in <see cref="DevToolsState.Table"/>.
    /// </param>
    /// <param name="value">The raw text from the property input.</param>
    /// <remarks>
    /// Empty text is ignored. The edit is queued and skipped when the interned
    /// property id is unchanged. Renaming a property does not revalidate its
    /// value.
    /// </remarks>
    private void CommitProperty(DevToolsState state, int ruleIndex, string value)
    {
        if (state.Table is not { } table)
        {
            return;
        }

        var text = value.Trim();

        if (text.Length == 0)
        {
            return;
        }

        var propertyId = InternTable.Intern(text);

        state.QueueEdit(() =>
        {
            if (table.GetRule(ruleIndex).PropertyId == propertyId)
            {
                return;
            }

            table.SetRuleProperty(ruleIndex, propertyId);
            state.NotifyStylesChanged();
        });
    }

    /// <summary>Commits the drafts in the add row to the rule.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule that receives the declaration.</param>
    /// <remarks>
    /// Both drafts must be non-empty after trimming. Element styles go through
    /// <see cref="DevToolsState.AddElementStyleDeclaration"/>; other rules append
    /// with <c>AddRule</c>, preserving the group's selector, condition and layer.
    /// Consumes <see cref="DevToolsState.AdvanceAfterCommit"/>: when set, the row
    /// stays open with cleared drafts; otherwise it closes.
    /// </remarks>
    private void CommitAdd(DevToolsState state, RuleGroup group)
    {
        var advance = state.AdvanceAfterCommit;
        state.AdvanceAfterCommit = false;

        if (state.Table is not { } table)
        {
            return;
        }

        var property = _addProperty.Trim();
        var value = _addValue.Trim();

        if (property.Length == 0 || value.Length == 0)
        {
            return;
        }

        var propertyId = InternTable.Intern(property);
        var valueId = InternTable.Intern(value);

        if (group.IsElementStyle)
        {
            if (group.Element is not { } element)
            {
                return;
            }

            state.QueueEdit(() =>
            {
                state.AddElementStyleDeclaration(element, propertyId, valueId);
                state.NotifyStylesChanged();
            });
        }
        else
        {
            var selectorId = group.SelectorId;
            var conditionId = group.ConditionId;
            var layerId = group.LayerId;

            state.QueueEdit(() =>
            {
                table.AddRule(selectorId, propertyId, valueId, conditionId, layerId);
                state.NotifyStylesChanged();
            });
        }

        if (advance)
        {
            BeginAdd(group);
            return;
        }

        _addingSelector = -1;
        _addProperty = string.Empty;
        _addValue = string.Empty;
    }

    /// <summary>Applies pasted declaration text to a rule.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="group">The rule that receives the declarations.</param>
    /// <param name="text">The pasted text.</param>
    /// <remarks>
    /// <para>
    /// Text containing both <c>{</c> and <c>}</c> is treated as a full rule block
    /// and compiled by the data engine into the shared table, so it can introduce
    /// selectors as well as declarations.
    /// </para>
    /// <para>
    /// Otherwise the text is split by <see cref="ParseDeclarations"/> and each
    /// declaration is appended to the target group. All mutation happens inside a
    /// single queued edit, followed by one styles-changed notification.
    /// </para>
    /// </remarks>
    private void PasteDeclarations(DevToolsState state, RuleGroup group, string text)
    {
        if (state.Table is not { } table || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var trimmed = text.Trim();

        if (trimmed.Contains('{') && trimmed.Contains('}'))
        {
            state.QueueEdit(() =>
            {
                new Donkeh.DataEngine.DataEngine(
                        Donkeh.DataEngine.MediaConditionCompiler.Instance)
                    .Build(Tokenizer.Parse(trimmed), table);
                state.NotifyStylesChanged();
            });
            return;
        }

        var declarations = ParseDeclarations(trimmed);

        if (declarations.Count == 0)
        {
            return;
        }

        var element = group.Element;
        var selectorId = group.SelectorId;
        var conditionId = group.ConditionId;
        var layerId = group.LayerId;
        var isElementStyle = group.IsElementStyle;

        state.QueueEdit(() =>
        {
            foreach (var (property, value) in declarations)
            {
                var propertyId = InternTable.Intern(property);
                var valueId = InternTable.Intern(value);

                if (isElementStyle && element is not null)
                {
                    state.AddElementStyleDeclaration(element, propertyId, valueId);
                }
                else
                {
                    table.AddRule(selectorId, propertyId, valueId, conditionId, layerId);
                }
            }

            state.NotifyStylesChanged();
        });
    }

    /// <summary>Splits CSS declaration text into property/value pairs.</summary>
    /// <param name="text">
    /// Declaration text, with or without a trailing semicolon.
    /// </param>
    /// <returns>
    /// The well-formed declarations in source order; malformed fragments are
    /// skipped.
    /// </returns>
    /// <remarks>
    /// Splits only on top-level semicolons, ignoring separators inside quotes or
    /// parentheses. The first colon separates property from value; fragments with
    /// an empty side are dropped. A missing final semicolon is tolerated.
    /// </remarks>
    private static List<(string Property, string Value)> ParseDeclarations(string text)
    {
        var result = new List<(string Property, string Value)>();
        var start = 0;
        var depth = 0;
        var quote = '\0';

        for (var i = 0; i <= text.Length; i++)
        {
            var c = i < text.Length ? text[i] : ';';

            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (c is '"' or '\'')
            {
                quote = c;
                continue;
            }

            if (c == '(')
            {
                depth++;
                continue;
            }

            if (c == ')')
            {
                depth = Math.Max(0, depth - 1);
                continue;
            }

            if (c != ';' || depth != 0)
            {
                continue;
            }

            var declaration = text[start..i].Trim();
            start = i + 1;

            if (declaration.Length == 0)
            {
                continue;
            }

            var colon = declaration.IndexOf(':');

            if (colon <= 0)
            {
                continue;
            }

            var property = declaration[..colon].Trim();
            var value = declaration[(colon + 1)..].Trim();

            if (property.Length > 0 && value.Length > 0)
            {
                result.Add((property, value));
            }
        }

        return result;
    }

    /// <summary>Builds a read-only computed-style row.</summary>
    /// <param name="name">The CSS property name shown in the left column.</param>
    /// <param name="value">The formatted value shown on the right.</param>
    /// <param name="color">
    /// Whether to add a swatch when <paramref name="value"/> parses as a colour.
    /// </param>
    /// <returns>The row node.</returns>
    private static Node BuildComputedRow(string name, string value, bool color = false)
    {
        var parts = new List<Node?>();

        parts.Add(_.H("span", new Props { ClassName = "dt-computed-name" }, _.Text(name)));

        if (color && ColorPreview.IsColor(value))
        {
            parts.Add(BuildSwatch(value));
        }

        parts.Add(_.H("span", new Props { ClassName = "dt-computed-value" }, _.Text(value)));

        return _.H("div", new Props { ClassName = "dt-computed-row" }, _.F(parts.ToArray()));
    }

    /// <summary>
    /// Formats an element as <c>&lt;tag#id.class&gt;</c> for pane headings.
    /// </summary>
    /// <param name="element">The element to label.</param>
    /// <returns>
    /// The tag, optional id and each class, enclosed in angle brackets.
    /// </returns>
    private static string ElementLabel(ElementNode element)
    {
        var label = "<" + element.Tag;

        if (element.Id is { } id)
        {
            label += "#" + (id.StartsWith('#') ? id[1..] : id);
        }

        foreach (var className in element.ClassName)
        {
            label += "." + className;
        }

        return label + ">";
    }

    /// <summary>
    /// Finds the display name of the stylesheet that produced a rule.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <param name="ruleIndex">The index of the rule in the table.</param>
    /// <returns>
    /// The matching range's <see cref="StyleSheetRange.Name"/>, or
    /// <c>inspector</c> when no range contains the index.
    /// </returns>
    /// <remarks>
    /// Scans the ranges linearly; the list is short and in cascade order.
    /// </remarks>
    private static string OriginFor(DevToolsState state, int ruleIndex)
    {
        for (var i = 0; i < state.StyleSheets.Count; i++)
        {
            if (state.StyleSheets[i].Contains(ruleIndex))
            {
                return state.StyleSheets[i].Name;
            }
        }

        return "inspector";
    }

    /// <summary>
    /// Reconstructs selector text from the table's interned token span.
    /// </summary>
    /// <param name="table">The stylesheet table that owns the selector.</param>
    /// <param name="selectorId">The selector id to render.</param>
    /// <returns>The selector as CSS text, with spaces at token gaps.</returns>
    /// <remarks>
    /// <see cref="DataTable.HasGap"/> distinguishes descendant combinators from
    /// adjacent tokens, because the token stream does not store whitespace.
    /// </remarks>
    private static string SelectorText(DataTable table, int selectorId)
    {
        var tokens = table.GetSelectorSpan(selectorId);
        var builder = new System.Text.StringBuilder();

        for (var i = 0; i < tokens.Length; i++)
        {
            if (i > 0 && DataTable.HasGap(tokens, i))
            {
                builder.Append(' ');
            }

            builder.Append(InternTable.GetString(tokens[i].InternedId));
        }

        return builder.ToString();
    }

    /// <summary>Formats a length as CSS pixels with at most two decimals.</summary>
    /// <param name="value">The length in CSS pixels.</param>
    /// <returns>The invariant-formatted number followed by <c>px</c>.</returns>
    private static string Px(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    /// <summary>Formats a four-sided thickness in CSS shorthand order.</summary>
    /// <param name="value">The thickness to format, in CSS pixels.</param>
    /// <returns>
    /// <c>top right bottom left</c>, each suffixed with <c>px</c>.
    /// </returns>
    private static string ThicknessValue(Thickness value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.Top:0.##}px {value.Right:0.##}px {value.Bottom:0.##}px {value.Left:0.##}px");
    }

    /// <summary>Formats a computed colour for the computed-style list.</summary>
    /// <param name="color">The packed ARGB colour.</param>
    /// <returns>
    /// <c>currentColor</c> for the sentinel value, <c>#RRGGBB</c> for opaque
    /// colours, otherwise <c>rgba(r, g, b, a)</c> with alpha in 0..1.
    /// </returns>
    /// <remarks>
    /// Uses the engine's <see cref="CssColor"/> accessors rather than
    /// <see cref="ColorPreview.Format"/> because the <c>currentColor</c> sentinel
    /// and the <c>rgba()</c> form need to be distinguished.
    /// </remarks>
    private static string FormatColor(uint color)
    {
        if (color == ComputedStyle.CurrentColor)
        {
            return "currentColor";
        }

        var alpha = CssColor.Alpha(color);

        if (alpha == 255)
        {
            return $"#{CssColor.Red(color):X2}{CssColor.Green(color):X2}{CssColor.Blue(color):X2}";
        }

        return $"rgba({CssColor.Red(color)}, {CssColor.Green(color)}, {CssColor.Blue(color)}, {alpha / 255f:0.##})";
    }

    /// <summary>Converts a PascalCase enum name to kebab-case CSS text.</summary>
    /// <param name="value">The identifier to convert.</param>
    /// <returns>
    /// The lower-case name with a hyphen before each interior uppercase letter.
    /// </returns>
    /// <remarks>
    /// Acronyms are not collapsed (<c>URL</c> becomes <c>u-r-l</c>); the enum names
    /// here are single words such as <c>FlexDirection</c>, so that is acceptable.
    /// </remarks>
    private static string Kebab(string value)
    {
        var builder = new System.Text.StringBuilder(value.Length + 4);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Cached matched-rule grouping for one selected element.</summary>
    private sealed class StylesGrouping(RuleGroup elementStyle, List<RuleGroup> groups)
    {
        /// <summary>Gets the inspector-owned <c>element.style</c> group.</summary>
        public RuleGroup ElementStyle { get; } = elementStyle;

        /// <summary>Gets the real matched groups in display order.</summary>
        public List<RuleGroup> Groups { get; } = groups;
    }

    /// <summary>
    /// One selector's declarations as displayed in the styles pane, with the
    /// cascade data needed to append more declarations to the same rule.
    /// </summary>
    /// <param name="origin">Display name of the stylesheet that produced the rule.</param>
    /// <param name="selector">Selector text shown in the rule head.</param>
    /// <param name="selectorId">Table selector id used when appending declarations.</param>
    /// <param name="conditionId">
    /// At-rule or media condition id carried into new declarations; -1 for an
    /// unconditional rule.
    /// </param>
    /// <param name="layerId">
    /// Cascade layer id carried into new declarations; -1 when not in a layer.
    /// </param>
    /// <param name="isElementStyle">
    /// Whether this group represents the inspector-owned <c>element.style</c>
    /// rule.
    /// </param>
    /// <remarks>
    /// A display grouping rebuilt on every render; it is not the table's own rule
    /// type. <see cref="Declarations"/> is filled by <see cref="BuildStyles"/> and
    /// mutated only while the pane is being built.
    /// </remarks>
    private sealed class RuleGroup(
        string origin,
        string selector,
        int selectorId,
        int conditionId,
        int layerId,
        bool isElementStyle = false)
    {
        /// <summary>
        /// Gets the stylesheet label shown at the right of the rule head.
        /// </summary>
        public string Origin { get; } = origin;

        /// <summary>
        /// Gets the selector text shown at the left of the rule head.
        /// </summary>
        public string Selector { get; } = selector;

        /// <summary>
        /// Gets the table selector id used when appending declarations. Only
        /// meaningful for a real rule or after the element style rule exists.
        /// </summary>
        public int SelectorId { get; } = selectorId;

        /// <summary>
        /// Gets the at-rule or media condition id carried into new declarations;
        /// -1 for an unconditional rule.
        /// </summary>
        public int ConditionId { get; } = conditionId;

        /// <summary>
        /// Gets the cascade layer id carried into new declarations; -1 when the
        /// rule is not in a layer.
        /// </summary>
        public int LayerId { get; } = layerId;

        /// <summary>
        /// Gets a value indicating whether this group represents the
        /// inspector-owned <c>element.style</c> rule.
        /// </summary>
        public bool IsElementStyle { get; } = isElementStyle;

        /// <summary>
        /// Gets the element whose own rule receives new declarations. Set only for
        /// <c>element.style</c> groups.
        /// </summary>
        public ElementNode? Element { get; init; }

        /// <summary>
        /// Gets the declarations in display order.
        /// </summary>
        public List<Declaration> Declarations { get; } = [];
    }

    /// <summary>One declaration line in a rule group.</summary>
    /// <param name="RuleIndex">
    /// The rule's index in the stylesheet table, used for edits.
    /// </param>
    /// <param name="Property">
    /// The property text, or <c>?</c> when interning failed.
    /// </param>
    /// <param name="Value">
    /// The value text, or <c>?</c> when interning failed.
    /// </param>
    /// <remarks>
    /// Built per render from matched rules; it is not stored on the table.
    /// </remarks>
    private readonly record struct Declaration(int RuleIndex, string Property, string Value);

    // ------------------------------------------------------------- animations

    /// <summary>
    /// Builds the Animations tab listing keyframe groups and their stops.
    /// </summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>
    /// The two-pane animation browser, or a placeholder when unavailable.
    /// </returns>
    /// <remarks>
    /// Groups are keyed by animation name and keep first-seen order. The selected
    /// name comes from <see cref="DevToolsState.SelectedAnimation"/>; unknown or
    /// null selections fall back to the first group. Stops are grouped by their
    /// selector text (for example <c>0%</c> or <c>from</c>) so duplicate stops from
    /// multiple sheets appear together.
    /// </remarks>
    private Node BuildAnimations(DevToolsState state)
    {
        if (state.Table is not { } table)
        {
            return _.H("div", new Props { ClassName = "dt-dim" }, _.Text("No stylesheet."));
        }

        var frames = table.Keyframes;
        var groups = new List<AnimationGroup>();
        var index = new Dictionary<string, AnimationGroup>(StringComparer.Ordinal);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames[i];
            var name = InternTable.GetString(frame.NameId) ?? "?";

            if (!index.TryGetValue(name, out var group))
            {
                group = new AnimationGroup(name);
                index[name] = group;
                groups.Add(group);
            }

            group.Keyframes.Add(frame);
        }

        if (groups.Count == 0)
        {
            return _.H("div", new Props { ClassName = "dt-dim" }, _.Text("No @keyframes registered."));
        }

        AnimationGroup? selected = null;

        if (state.SelectedAnimation is { } selectedName)
        {
            index.TryGetValue(selectedName, out selected);
        }

        selected ??= groups[0];

        var list = new List<Node?>();

        foreach (var group in groups)
        {
            var name = group.Name;
            var className = ReferenceEquals(group, selected)
                ? "dt-anim-row dt-anim-selected"
                : "dt-anim-row";

            list.Add(
                _.H(
                    "div",
                    new Props
                    {
                        ClassName = className,
                        OnClick = () =>
                        {
                            state.SelectedAnimation = name;
                            RequestUpdate();
                        }
                    },
                    _.H("span", new Props { ClassName = "dt-anim-name" }, _.Text("@" + name)),
                    _.H(
                        "span",
                        new Props { ClassName = "dt-anim-count" },
                        _.Text(group.Keyframes.Count.ToString(CultureInfo.InvariantCulture)))));
        }

        var detail = new List<Node?>
        {
            _.H("div", new Props { ClassName = "dt-anim-title" }, _.Text("@keyframes " + selected.Name)),
            _.H(
                "div",
                new Props { ClassName = "dt-dim" },
                _.Text(selected.Keyframes.Count + " declarations"))
        };

        var stops = new List<(string Key, List<CssKeyframe> Frames)>();
        var stopIndex = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var frame in selected.Keyframes)
        {
            var key = SelectorText(table, frame.KeyId);

            if (!stopIndex.TryGetValue(key, out var stop))
            {
                stop = stops.Count;
                stopIndex[key] = stop;
                stops.Add((key, []));
            }

            stops[stop].Frames.Add(frame);
        }

        foreach (var (key, keyframes) in stops)
        {
            detail.Add(_.H("div", new Props { ClassName = "dt-anim-stop" }, _.Text(key)));

            foreach (var frame in keyframes)
            {
                var property = InternTable.GetString(frame.PropertyId) ?? "?";
                var value = InternTable.GetString(frame.ValueId) ?? "?";

                detail.Add(
                    _.H(
                        "div",
                        new Props { ClassName = "dt-anim-decl" },
                        _.H("span", new Props { ClassName = "dt-anim-prop" }, _.Text(property)),
                        _.H("span", new Props { ClassName = "dt-dim" }, _.Text(": ")),
                        _.H("span", new Props { ClassName = "dt-anim-value" }, _.Text(value))));
            }
        }

        return _.H(
            "div",
            new Props { ClassName = "dt-animations" },
            _.H("div", new Props { ClassName = "dt-anim-list" }, _.F(list.ToArray())),
            _.H("div", new Props { ClassName = "dt-anim-detail" }, _.F(detail.ToArray())));
    }

    /// <summary>
    /// All keyframe stops registered under one <c>@keyframes</c> name.
    /// </summary>
    /// <param name="name">The animation name without the leading <c>@</c>.</param>
    /// <remarks>Built per render, preserving first-seen order.</remarks>
    private sealed class AnimationGroup(string name)
    {
        /// <summary>Gets the animation name without the leading <c>@</c>.</summary>
        public string Name { get; } = name;

        /// <summary>Gets the keyframe stops in table order.</summary>
        public List<CssKeyframe> Keyframes { get; } = [];
    }

    // ------------------------------------------------------------ performance

    /// <summary>Builds the Performance tab dashboard.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>The cards, legend and three history graphs.</returns>
    /// <remarks>
    /// Reads the most recent <see cref="DevToolsState.Performance"/> sample; it
    /// never samples directly. Graphs use fixed scales of 33 ms for frame time and
    /// 16 ms for layout and render time.
    /// </remarks>
    private Node BuildPerformance(DevToolsState state)
    {
        var stats = state.Performance;
        var output = new List<Node?>
        {
            _.H(
                "div",
                new Props { ClassName = "dt-cards" },
                BuildCard("FPS", Fps(stats.FrameMilliseconds), $"frame {stats.FrameMilliseconds:0.##} ms"),
                BuildCard("CPU", $"{stats.CpuPercent:0.#}%", $"{stats.ThreadCount} threads"),
                BuildCard("Managed", Bytes(stats.ManagedMemory), $"working set {Bytes(stats.WorkingSet)}"),
                BuildCard("GC", $"{stats.Gen0}/{stats.Gen1}/{stats.Gen2}", "gen0 / gen1 / gen2")),
            _.H(
                "div",
                new Props { ClassName = "dt-cards" },
                BuildCard("Layout", $"{stats.LayoutMilliseconds:0.##} ms", $"{stats.LayoutBoxCount} boxes"),
                BuildCard("Render", $"{stats.RenderMilliseconds:0.##} ms", $"{stats.InstructionCount} ops"),
                BuildCard("Update", $"{stats.UpdateMilliseconds:0.##} ms", $"{stats.NodeCount} nodes"),
                BuildCard("Frames", stats.FrameCount.ToString(CultureInfo.InvariantCulture), $"avg {stats.AverageFrameMilliseconds:0.##} ms")),
            BuildLegend(),
            _.H("div", new Props { ClassName = "dt-section-title" }, _.Text("FRAME TIME (ms)")),
            BuildGraph(stats.FrameHistory, "dt-bar", 33f),
            _.H("div", new Props { ClassName = "dt-section-title" }, _.Text("LAYOUT TIME (ms)")),
            BuildGraph(stats.LayoutHistory, "dt-bar dt-bar-layout", 16f),
            _.H("div", new Props { ClassName = "dt-section-title" }, _.Text("RENDER TIME (ms)")),
            BuildGraph(stats.RenderHistory, "dt-bar dt-bar-render", 16f)
        };

        return _.H("div", new Props { ClassName = "dt-perf" }, _.F(output.ToArray()));
    }

    /// <summary>Builds one metric card with a label, value and sub-label.</summary>
    /// <param name="label">The metric name.</param>
    /// <param name="value">The formatted primary value.</param>
    /// <param name="sub">The secondary detail line.</param>
    /// <returns>The card node.</returns>
    private static Node BuildCard(string label, string value, string sub)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-card" },
            _.H("span", new Props { ClassName = "dt-card-label" }, _.Text(label)),
            _.H("span", new Props { ClassName = "dt-card-value" }, _.Text(value)),
            _.H("span", new Props { ClassName = "dt-card-sub" }, _.Text(sub)));
    }

    /// <summary>Builds the frame/layout/render colour legend.</summary>
    /// <returns>The legend node.</returns>
    private static Node BuildLegend()
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-legend" },
            BuildLegendItem("dt-dot-frame", "frame"),
            BuildLegendItem("dt-dot-layout", "layout"),
            BuildLegendItem("dt-dot-render", "render"));
    }

    /// <summary>Builds one legend entry.</summary>
    /// <param name="colorClass">
    /// The colour swatch class (<c>dt-dot-frame</c>, <c>dt-dot-layout</c> or
    /// <c>dt-dot-render</c>).
    /// </param>
    /// <param name="label">The text shown next to the swatch.</param>
    /// <returns>The legend item node.</returns>
    private static Node BuildLegendItem(string colorClass, string label)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-legend-item" },
            _.H("div", new Props { ClassName = "dt-legend-dot " + colorClass }),
            _.H("span", new Props { ClassName = "dt-dim" }, _.Text(label)));
    }

    /// <summary>Builds a bar graph from a history span.</summary>
    /// <param name="values">Sample values in milliseconds.</param>
    /// <param name="className">
    /// Base class for the bars, which also sets their colour.
    /// </param>
    /// <param name="max">
    /// Value mapped to a 120 px bar; floored at 0.01 to avoid division by zero.
    /// </param>
    /// <returns>
    /// The graph node, or a "collecting..." placeholder when there are no samples.
    /// </returns>
    /// <remarks>
    /// Bar heights are quantised to whole pixels and emitted as <c>dt-hN</c>
    /// utility classes, because the engine does not support inline styles.
    /// </remarks>
    private static Node BuildGraph(ReadOnlySpan<float> values, string className, float max)
    {
        var bars = new List<Node?>();

        for (var i = 0; i < values.Length; i++)
        {
            var height = Math.Clamp((int)MathF.Round(values[i] / MathF.Max(0.01f, max) * 120f), 0, 120);
            bars.Add(_.H("div", new Props { ClassName = className + " dt-h" + height.ToString(CultureInfo.InvariantCulture) }));
        }

        if (bars.Count == 0)
        {
            bars.Add(_.H("div", new Props { ClassName = "dt-dim" }, _.Text("collecting...")));
        }

        return _.H("div", new Props { ClassName = "dt-graph" }, _.F(bars.ToArray()));
    }

    /// <summary>Formats a frame time as whole frames per second.</summary>
    /// <param name="frameMs">Frame time in milliseconds.</param>
    /// <returns>
    /// An em dash for non-positive input, otherwise <c>1000 / frameMs</c> rounded.
    /// </returns>
    private static string Fps(double frameMs) =>
        frameMs <= 0 ? "\u2014" : (1000.0 / frameMs).ToString("0", CultureInfo.InvariantCulture);

    /// <summary>Formats a byte count with binary units.</summary>
    /// <param name="bytes">The byte count.</param>
    /// <returns>A value in B, KB, MB or GB using 1024-based units.</returns>
    private static string Bytes(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }

        if (bytes < 1024 * 1024)
        {
            return (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB";
        }

        if (bytes < 1024L * 1024 * 1024)
        {
            return (bytes / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }

        return (bytes / 1073741824.0).ToString("0.00", CultureInfo.InvariantCulture) + " GB";
    }

    /// <summary>Builds the bottom status strip.</summary>
    /// <param name="state">The current inspector state.</param>
    /// <returns>
    /// The status bar showing the active tab, node count and shortcut hint.
    /// </returns>
    /// <remarks>
    /// The node count walks the logical tree on every render and is therefore
    /// linear in the size of the mounted app.
    /// </remarks>
    private Node BuildStatusBar(DevToolsState state)
    {
        var count = _liveNodeCount;
        var tab = state.Tab == DevToolsTab.Elements ? "Elements" : "Performance";

        return _.H(
            "div",
            new Props { ClassName = "dt-status" },
            _.Text(tab),
            _.H("span", new Props { ClassName = "dt-status-gap" }),
            _.Text($"{count} nodes"),
            _.Text("  \u00B7  Ctrl+Shift+I to toggle"));
    }

    /// <summary>
    /// Returns a node's logical children, looking through its rendered output.
    /// </summary>
    /// <param name="node">The node to inspect.</param>
    /// <returns>
    /// A component's output, a fragment's or provider's children, an element's
    /// virtual children (falling back to rendered children), or an empty array.
    /// </returns>
    /// <remarks>
    /// Used consistently by tree seeding, expansion, pruning and node counting so
    /// all of them see the same logical structure.
    /// </remarks>
    private static Node[] LogicalChildren(Node node)
    {
        return node switch
        {
            ComponentNode component => component.OutputVdom is { } output ? [output] : [],
            FragmentNode fragment => fragment.VdomChildren,
            ProviderNode provider => provider.ProviderChildren,
            ElementNode element => RenderedChildren(element),
            _ => []
        };
    }

    /// <summary>
    /// Returns the logical children of an element without LINQ or a temporary
    /// iterator over the DOM node array.
    /// </summary>
    /// <param name="element">Element whose children are wanted.</param>
    /// <returns>
    /// <c>VdomChildren</c> when populated, otherwise the rendered <see cref="Node"/>
    /// entries from the DOM child array.
    /// </returns>
    private static Node[] RenderedChildren(ElementNode element)
    {
        if (element.VdomChildren.Length > 0)
        {
            return element.VdomChildren;
        }

        var children = element.Children;
        var count = 0;

        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is Node)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return [];
        }

        var result = new Node[count];
        var index = 0;

        for (var i = 0; i < children.Length; i++)
        {
            if (children[i] is Node node)
            {
                result[index++] = node;
            }
        }

        return result;
    }

    /// <summary>
    /// Removes expansion entries for nodes that have left the app tree.
    /// Expansion is keyed by live nodes, so conditional components (fade
    /// panels, modal bodies, carousel slides) would otherwise stay reachable
    /// through the inspector's HashSet after they unmount.
    /// </summary>
    /// <param name="state">The state to prune in place.</param>
    /// <remarks>
    /// Reuses a single visited set, drops expansion entries, element-style
    /// bookkeeping and selections pointing at nodes that are no longer reachable,
    /// and records the live node count for the status bar so the tree is walked
    /// only once per render.
    /// </remarks>
    private void PruneExpanded(DevToolsState state)
    {
        var live = _liveNodes;
        live.Clear();

        if (state.AppRoot is null)
        {
            _liveNodeCount = 0;
            return;
        }

        CollectLogicalNodes(state.AppRoot, live);
        _liveNodeCount = live.Count;

        if (state.Expanded.Count > 0)
        {
            state.Expanded.RemoveWhere(node => !live.Contains(node));
        }

        state.PruneElementStyles(live);

        if (state.Selected is not null && !live.Contains(state.Selected))
        {
            state.Selected = null;
        }

        if (state.Hovered is not null && !live.Contains(state.Hovered))
        {
            state.Hovered = null;
        }
    }

    /// <summary>Collects every logically reachable node into a set.</summary>
    /// <param name="node">The root of the subtree to walk.</param>
    /// <param name="live">
    /// The destination set, also used to avoid revisiting shared nodes.
    /// </param>
    /// <remarks>
    /// Depth-first and recursive; the visited guard prevents cycles from looping
    /// forever.
    /// </remarks>
    private static void CollectLogicalNodes(Node node, HashSet<Node> live)
    {
        if (!live.Add(node))
        {
            return;
        }

        var children = LogicalChildren(node);

        for (var i = 0; i < children.Length; i++)
        {
            CollectLogicalNodes(children[i], live);
        }
    }

    /// <summary>
    /// Expands every logical ancestor of <paramref name="target"/> so its row is
    /// visible in the DOM tree.
    /// </summary>
    /// <param name="state">The state whose expansion set is updated.</param>
    /// <param name="target">The node that must become visible.</param>
    /// <remarks>
    /// Expands the target and each logical ancestor, matched by reference. Does
    /// nothing when <see cref="DevToolsState.AppRoot"/> is null or the target is
    /// not reachable from it. Does not refresh the state or request scrolling;
    /// callers do that themselves.
    /// </remarks>
    public static void ExpandPathTo(DevToolsState state, Node target)
    {
        if (state.AppRoot is not null)
        {
            ExpandPathTo(
                state.AppRoot,
                target,
                state.Expanded,
                new HashSet<Node>(ReferenceEqualityComparer.Instance));
        }
    }

    /// <summary>Recursively finds a target and expands the path back to it.</summary>
    /// <param name="node">The current subtree root.</param>
    /// <param name="target">The node to find.</param>
    /// <param name="expanded">The expansion set to update.</param>
    /// <param name="visited">
    /// Nodes already visited, guarding against shared or cyclic references.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if <paramref name="target"/> was found.
    /// </returns>
    /// <remarks>
    /// Ancestors are added while unwinding, so a failed search leaves the set
    /// untouched.
    /// </remarks>
    private static bool ExpandPathTo(
        Node node,
        Node target,
        HashSet<Node> expanded,
        HashSet<Node> visited)
    {
        if (ReferenceEquals(node, target))
        {
            expanded.Add(node);
            return true;
        }

        if (!visited.Add(node))
        {
            return false;
        }

        var children = LogicalChildren(node);

        for (var i = 0; i < children.Length; i++)
        {
            if (ExpandPathTo(children[i], target, expanded, visited))
            {
                expanded.Add(node);
                return true;
            }
        }

        return false;
    }
}
