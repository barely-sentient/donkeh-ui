using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.Demo;

/// <summary>Small browser smoke test that exercises the DOM bridge on every target.</summary>
public sealed class BrowserSample : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        var (name, setName) = State(string.Empty);
        var (count, setCount) = State(0);
        var (checkedState, setCheckedState) = State(false);
        var (lastEvent, setLastEvent) = State("none");
        var (scrollY, setScrollY) = State(0f);
        var (pointer, setPointer) = State("none");

        var card = new Dictionary<string, string>
        {
            ["style"] = "background:#fff;border:1px solid #d0d7de;border-radius:12px;padding:18px;margin:16px 0"
        };

        var button = new Dictionary<string, string>
        {
            ["style"] = "font:inherit;padding:8px 14px;border-radius:8px;border:1px solid #0969da;background:#0969da;color:#fff;cursor:pointer"
        };

        var field = new Dictionary<string, string>
        {
            ["style"] = "font:inherit;padding:8px 10px;border:1px solid #d0d7de;border-radius:8px;width:100%;box-sizing:border-box"
        };

        var scrollBox = new Dictionary<string, string>
        {
            ["style"] = "height:120px;overflow:auto;border:1px solid #d0d7de;border-radius:8px;padding:8px"
        };

        var pointerBox = new Dictionary<string, string>
        {
            ["style"] = "height:110px;border:1px dashed #8c959f;border-radius:8px;margin-top:12px;display:flex;align-items:center;justify-content:center;user-select:none;touch-action:none"
        };

        var rows = new List<Node>();

        for (var i = 0; i < 20; i++)
        {
            rows.Add(H("div", new Props
            {
                Key = i.ToString(),
                Attributes = new Dictionary<string, string>
                {
                    ["style"] = "padding:6px 8px;border-bottom:1px solid #f0f3f6"
                }
            }, Text($"Scrollable row {i + 1}")));
        }

        return H("div", new Props
        {
            Attributes = new Dictionary<string, string>
            {
                ["style"] = "max-width:760px;margin:0 auto;padding:24px;font-family:system-ui,sans-serif;color:#1f2328;background:#f6f8fa;min-height:100%"
            }
        },
            H("h1", new Props
            {
                Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 4px" }
            }, Text("Donkeh — native DOM host")),
            H("p", new Props
            {
                Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 8px;color:#57606a" }
            }, Text("The same virtual tree is rendered by the desktop/OpenGL host or mirrored into DOM elements here.")),

            H("div", new Props { Attributes = card },
                H("h2", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 12px;font-size:18px" }
                }, Text("Text input")),
                H("input", new Props
                {
                    Value = name,
                    Placeholder = "Type your name",
                    Attributes = field,
                    OnInput = setName,
                    OnKeyDown = key => setLastEvent($"keydown: {key}")
                }),
                H("p", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:10px 0 0" }
                }, Text(string.IsNullOrEmpty(name) ? "Type something…" : $"Hello, {name}!"))),

            H("div", new Props { Attributes = card },
                H("h2", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 12px;font-size:18px" }
                }, Text("Click and state")),
                H("button", new Props
                {
                    Attributes = button,
                    OnClick = () => setCount(count + 1)
                }, Text($"Clicked {count} time{(count == 1 ? string.Empty : "s")}")),
                H("label", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "display:flex;gap:8px;align-items:center;margin-top:14px" }
                },
                    H("input", new Props
                    {
                        Type = "checkbox",
                        Checked = checkedState,
                        OnChange = value => setCheckedState(value == "true")
                    }),
                    Text(checkedState ? "Checkbox is checked" : "Checkbox is unchecked"))),

            H("div", new Props { Attributes = card },
                H("h2", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 12px;font-size:18px" }
                }, Text("Pointer and context menu")),
                H("div", new Props
                {
                    Attributes = pointerBox,
                    OnPointerDown = (x, y) => setPointer($"down {x:0},{y:0}"),
                    OnPointerMove = (x, y) => setPointer($"move {x:0},{y:0}"),
                    OnPointerUp = (x, y) => setPointer($"up {x:0},{y:0}"),
                    OnContextMenu = (x, y) => setLastEvent($"context menu at {x:0},{y:0}")
                }, Text(pointer)),
                H("p", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:10px 0 0;color:#57606a" }
                }, Text($"Last event: {lastEvent}"))),

            H("div", new Props { Attributes = card },
                H("h2", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:0 0 12px;font-size:18px" }
                }, Text("Native scrolling")),
                H("div", new Props
                {
                    Attributes = scrollBox,
                    OnScroll = (_, deltaY) => setScrollY(Math.Max(0, scrollY + deltaY))
                }, rows.ToArray()),
                H("p", new Props
                {
                    Attributes = new Dictionary<string, string> { ["style"] = "margin:10px 0 0;color:#57606a" }
                }, Text($"Scroll offset: {scrollY:0}px"))));
    }
}
