using System.Globalization;
using Ignite.Data;
using Ignite.Data.Dom;
using Ignite.LayoutLab;
using Ignite.Parser;
using Ignite.Parser.Objects;
using Ignite.UILab;

namespace Ignite.Inspector;

/// <summary>Props for <see cref="InspectorPanel"/>.</summary>
public sealed class InspectorPanelProps
{
    public DevToolsState State { get; init; } = null!;

    /// <summary>True when the panel owns its own native window.</summary>
    public bool Standalone { get; init; }
}

/// <summary>
/// The Chrome-style devtools panel. It is a normal Ignite component and reads
/// the live mounted DOM through <see cref="DevToolsState.AppRoot"/>.
/// </summary>
public sealed class InspectorPanel : Component<InspectorPanelProps>
{
    private Action<int>? _setTick;
    private int _tick;
    private long _lastVersion = -1;
    private int _addingSelector = -1;
    private string _addProperty = string.Empty;
    private string _addValue = string.Empty;
    private const int ElementStyleAddKey = -2;

    /// <summary>Called by the host frame loop to pull fresh data into the panel.</summary>
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

    public override Node Render()
    {
        var state = Props.State;
        var (tick, setTick) = State(0);
        _tick = tick;
        _setTick = setTick;

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
            className += " dt-dock-right";
        }
        else
        {
            className += " dt-dock-bottom";
        }

        if (hidden)
        {
            className += " dt-hidden";
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
            picker);
    }

    private void RequestUpdate()
    {
        Props.State.Refresh();
        Tick();
    }

    // ---------------------------------------------------------------- toolbar

    private Node BuildToolbar(DevToolsState state)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-toolbar" },
            _.H(
                "div",
                new Props { ClassName = "dt-tabs" },
                BuildTab(state, DevToolsTab.Elements, "Elements"),
                BuildTab(state, DevToolsTab.Performance, "Performance")),
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

    private Node BuildBody(DevToolsState state)
    {
        if (state.Tab == DevToolsTab.Performance)
        {
            return BuildPerformance(state);
        }

        return _.H(
            "div",
            new Props { ClassName = "dt-elements" },
            _.H("div", new Props { ClassName = "dt-dom-pane" }, BuildDomTree(state)),
            _.H("div", new Props { ClassName = "dt-styles-pane" }, BuildStyles(state)));
    }

    // --------------------------------------------------------------- dom tree

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

    private Node BuildComponentRow(DevToolsState state, ComponentNode component)
    {
        var name = component.ComponentType.Name;
        var children = new List<Node?>();

        if (component.OutputVdom is { } output)
        {
            AddLogicalRows(state, [output], children);
        }

        var expanded = state.Expanded.Contains(component);

        var label = _.F(
            _.H("span", new Props { ClassName = "dt-component" }, _.Text("<" + name + ">")),
            _.H("span", new Props { ClassName = "dt-component-badge" }, _.Text("component")));

        var row = BuildRow(state, component, children.Count > 0, expanded, label);

        if (!expanded || children.Count == 0)
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

    private Node BuildElementRow(DevToolsState state, ElementNode element)
    {
        var children = new List<Node?>();
        var logical = element.VdomChildren.Length > 0
            ? element.VdomChildren
            : element.Children.OfType<Node>().ToArray();

        AddLogicalRows(state, logical, children);

        var expanded = state.Expanded.Contains(element);
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

        var row = BuildRow(state, element, children.Count > 0, expanded, _.F(label.ToArray()));

        if (!expanded || children.Count == 0)
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

            output.Add(BuildRuleGroup(state, elementStyle));

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

            if (groups.Count == 0)
            {
                output.Add(_.H("div", new Props { ClassName = "dt-dim" }, _.Text("No matching rules.")));
            }

            foreach (var group in groups)
            {
                output.Add(BuildRuleGroup(state, group));
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

    private static readonly string[] PresetColors =
    [
        "#000000", "#ffffff", "#212529", "#6c757d", "#adb5bd", "#e9ecef",
        "#0d6efd", "#6610f2", "#6f42c1", "#d63384", "#dc3545", "#fd7e14",
        "#ffc107", "#198754", "#20c997", "#0dcaf0"
    ];

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

    private void OpenColorPicker(DevToolsState state, int ruleIndex, string value)
    {
        state.ColorPickerRuleIndex = ruleIndex;
        state.ColorPickerValue = ColorPreview.TryParse(value, out var argb)
            ? ColorPreview.Format(argb)
            : value;
        RequestUpdate();
    }

    private void CloseColorPicker(DevToolsState state)
    {
        state.ColorPickerRuleIndex = -1;
        RequestUpdate();
    }

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

    private void BeginAdd(RuleGroup group)
    {
        _addingSelector = group.IsElementStyle ? ElementStyleAddKey : group.SelectorId;
        _addProperty = string.Empty;
        _addValue = string.Empty;
        Props.State.PendingFocusClass = "dt-add-input";
        RequestUpdate();
    }

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

    private void CommitValueLive(DevToolsState state, RuleGroup group, int ruleIndex, string value)
    {
        if (!LooksComplete(value))
        {
            return;
        }

        CommitValue(state, group, ruleIndex, value);
    }

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
                new Ignite.DataEngine.DataEngine(
                        Ignite.DataEngine.MediaConditionCompiler.Instance)
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

    private static string Px(float value) =>
        value.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    private static string ThicknessValue(Thickness value)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{value.Top:0.##}px {value.Right:0.##}px {value.Bottom:0.##}px {value.Left:0.##}px");
    }

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

    private sealed class RuleGroup(
        string origin,
        string selector,
        int selectorId,
        int conditionId,
        int layerId,
        bool isElementStyle = false)
    {
        public string Origin { get; } = origin;
        public string Selector { get; } = selector;
        public int SelectorId { get; } = selectorId;
        public int ConditionId { get; } = conditionId;
        public int LayerId { get; } = layerId;
        public bool IsElementStyle { get; } = isElementStyle;
        public ElementNode? Element { get; init; }
        public List<Declaration> Declarations { get; } = [];
    }

    private readonly record struct Declaration(int RuleIndex, string Property, string Value);

    // ------------------------------------------------------------ performance

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

    private static Node BuildCard(string label, string value, string sub)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-card" },
            _.H("span", new Props { ClassName = "dt-card-label" }, _.Text(label)),
            _.H("span", new Props { ClassName = "dt-card-value" }, _.Text(value)),
            _.H("span", new Props { ClassName = "dt-card-sub" }, _.Text(sub)));
    }

    private static Node BuildLegend()
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-legend" },
            BuildLegendItem("dt-dot-frame", "frame"),
            BuildLegendItem("dt-dot-layout", "layout"),
            BuildLegendItem("dt-dot-render", "render"));
    }

    private static Node BuildLegendItem(string colorClass, string label)
    {
        return _.H(
            "div",
            new Props { ClassName = "dt-legend-item" },
            _.H("div", new Props { ClassName = "dt-legend-dot " + colorClass }),
            _.H("span", new Props { ClassName = "dt-dim" }, _.Text(label)));
    }

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

    private static string Fps(double frameMs) =>
        frameMs <= 0 ? "\u2014" : (1000.0 / frameMs).ToString("0", CultureInfo.InvariantCulture);

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

    private Node BuildStatusBar(DevToolsState state)
    {
        var count = CountNodes(state.AppRoot);
        var tab = state.Tab == DevToolsTab.Elements ? "Elements" : "Performance";

        return _.H(
            "div",
            new Props { ClassName = "dt-status" },
            _.Text(tab),
            _.H("span", new Props { ClassName = "dt-status-gap" }),
            _.Text($"{count} nodes"),
            _.Text("  \u00B7  Ctrl+Shift+I to toggle"));
    }

    private static Node[] LogicalChildren(Node node)
    {
        return node switch
        {
            ComponentNode component => component.OutputVdom is { } output ? [output] : [],
            FragmentNode fragment => fragment.VdomChildren,
            ProviderNode provider => provider.ProviderChildren,
            ElementNode element => element.VdomChildren.Length > 0
                ? element.VdomChildren
                : element.Children.OfType<Node>().ToArray(),
            _ => []
        };
    }

    /// <summary>
    /// Expands every logical ancestor of <paramref name="target"/> so its row is
    /// visible in the DOM tree.
    /// </summary>
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

    private static int CountNodes(Node? node)
    {
        if (node is null)
        {
            return 0;
        }

        var count = 1;

        switch (node)
        {
            case ComponentNode component:
                count += CountNodes(component.OutputVdom);
                break;

            case ProviderNode provider:
                for (var i = 0; i < provider.ProviderChildren.Length; i++)
                {
                    count += CountNodes(provider.ProviderChildren[i]);
                }

                break;

            case FragmentNode fragment:
                for (var i = 0; i < fragment.VdomChildren.Length; i++)
                {
                    count += CountNodes(fragment.VdomChildren[i]);
                }

                break;

            case ElementNode element:
            {
                var logical = element.VdomChildren.Length > 0
                    ? element.VdomChildren
                    : element.Children.OfType<Node>().ToArray();

                for (var i = 0; i < logical.Length; i++)
                {
                    count += CountNodes(logical[i]);
                }

                break;
            }
        }

        return count;
    }
}
