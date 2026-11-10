using Ignite.UILab;
using Ignite.Windowing;
using static Ignite.UILab._;

var settings = WindowSettings.Default with
{
    Width = 1120,
    Height = 760,
    Title = "Ignite / OpenGL"
};

var window = new IgniteWindow(settings);

var smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);
var perf = args.Contains("--perf", StringComparer.OrdinalIgnoreCase);

if (perf)
{
    var totalLayout = 0d;
    var totalRender = 0d;
    var samples = 0;

    window.BeforeFrame += (host, _) =>
    {
        host.Invalidate();

        if (host.FrameCount > 2)
        {
            totalLayout += host.LastLayoutMilliseconds;
            totalRender += host.LastRenderMilliseconds;
            samples++;
        }

        if (host.FrameCount >= 150)
        {
            host.SilkWindow?.Close();
        }
    };

    window.Closed += _ => Console.WriteLine(
        $"warm layout {totalLayout / Math.Max(1, samples):0.000} ms, " +
        $"render {totalRender / Math.Max(1, samples):0.000} ms, samples={samples}");
}

if (smoke)
{
    window.BeforeFrame += (host, _) =>
    {
        if (host.FrameCount >= 120)
        {
            host.SilkWindow?.Close();
        }
    };

    window.Closed += _ => Console.WriteLine(
        $"frames={window.FrameCount} layoutMs={window.LastLayoutMilliseconds:0.00} " +
        $"renderMs={window.LastRenderMilliseconds:0.00} instructions={window.LastInstructionCount}");
}

if (args.Contains("--minimal", StringComparer.OrdinalIgnoreCase))
{
    var captureIndex = Array.FindIndex(args, a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));

    if (captureIndex >= 0 && captureIndex + 1 < args.Length)
    {
        var path = Path.GetFullPath(args[captureIndex + 1]);
        window.Loaded += host => host.RequestScreenshot(path);
    }

    window.Run(H("div", new Props { ClassName = "minimal" },
        H("h1", Text("Hello Ignite")),
        H("p", Text("The quick brown fox jumps over the lazy dog. 0123456789")),
        H("button", new Props { ClassName = "primary" }, Text("A button"))), """
        .minimal { width: 100%; height: 100vh; padding: 40px; background-color: #ffffff; }
        .minimal h1 { font-size: 42px; margin: 0 0 20px 0; color: #1f2328; }
        .minimal p { font-size: 18px; color: #343a46; }
        .minimal button { margin-top: 20px; background-color: #2f81f7; color: #ffffff; border-color: #2f81f7; }
        """);
    return;
}

var fullCaptureIndex = Array.FindIndex(args, a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));

if (fullCaptureIndex >= 0 && fullCaptureIndex + 1 < args.Length)
{
    var path = Path.GetFullPath(args[fullCaptureIndex + 1]);
    window.BeforeFrame += (host, _) =>
    {
        if (host.FrameCount == 2)
        {
            host.RequestScreenshot(path);
        }
    };
}

window.Run(C<DemoApp>(), DemoStyles.Css);

internal sealed class DemoApp : Component
{
    public override Node Render()
    {
        var (count, setCount) = State(0);
        var (page, setPage) = State("Overview");
        var (filter, setFilter) = State(string.Empty);

        return H("div", new Props { ClassName = "app" },
            H("header", new Props { ClassName = "topbar" },
                H("div", new Props { ClassName = "brand" },
                    H("span", new Props { ClassName = "mark" }, Text("I")),
                    H("div",
                        H("h1", Text("Ignite")),
                        H("span", new Props { ClassName = "subtitle" }, Text("CSS engine / layout / display list")))),
                H("nav", new Props { ClassName = "nav" },
                    NavButton("Overview", page, setPage),
                    NavButton("Layout", page, setPage),
                    NavButton("Paint", page, setPage)),
                H("input", new Props
                {
                    ClassName = "search",
                    Placeholder = "Filter components...",
                    Value = filter,
                    OnInput = setFilter
                })),
            H("div", new Props { ClassName = "body" },
                H("aside", new Props { ClassName = "sidebar" },
                    H("h2", Text("Primitives")),
                    H("ul", new Props { ClassName = "tree" },
                        Each(new[]
                        {
                            "Block flow", "Flexbox", "Grid", "Floats", "Text wrapping",
                            "Rounded corners", "Gradients", "Shadows", "Borders", "Scroll containers"
                        }, item => H("li", new Props { ClassName = "tree-item" }, Text(item)))),
                    H("h2", Text("Controls")),
                    H("div", new Props { ClassName = "control-row" },
                        H("input", new Props { Type = "checkbox", Checked = true }),
                        H("label", Text("Checked"))),
                    H("div", new Props { ClassName = "control-row" },
                        H("input", new Props { Type = "checkbox" }),
                        H("label", Text("Unchecked"))),
                    H("div", new Props { ClassName = "control-row" },
                        H("progress", new Props { Attributes = new Dictionary<string, string> { ["value"] = "68", ["max"] = "100" } })),
                    H("div", new Props { ClassName = "control-row" },
                        H("input", new Props { Type = "range", Value = "42", Attributes = new Dictionary<string, string> { ["min"] = "0", ["max"] = "100" } }))),
                H("main", new Props { ClassName = "page-content" },
                    H("section", new Props { ClassName = "hero" },
                        H("h2", Text(page)),
                        H("p", Text("This window is painted entirely from LayoutLab's 24-byte instruction stream. The CSS parser, cascade, layout and paint pipeline all run before a single OpenGL draw call.")),
                        H("div", new Props { ClassName = "hero-actions" },
                            H("button", new Props { ClassName = "primary", OnClick = () => setCount(count + 1) },
                                Text($"Interactions: {count}")),
                            H("button", new Props { ClassName = "ghost" }, Text("Secondary")))),
                    H("section", new Props { ClassName = "cards" },
                        Card("Display list", "24-byte instructions", "DrawRect", "rounded", "text", "gradient"),
                        Card("Cascade", "Interned selectors", "type", "class", "id", "#root", "attr"),
                        Card("Layout", "Block / flex / grid", "margin", "padding", "border", "gap", "align"),
                        Card("Text", "TrueType metrics", "wrap", "clamp", "ellipsis", "baseline")),
                    H("section", new Props { ClassName = "table-card" },
                        H("h3", Text("HTML elements")),
                        H("table",
                            H("thead", H("tr",
                                H("th", Text("Tag")),
                                H("th", Text("Default display")),
                                H("th", Text("Paint path")))),
                            H("tbody",
                                TableRow("div", "block", "background / border / children"),
                                TableRow("span", "inline", "text run"),
                                TableRow("img", "inline-block", "texture quad (intrinsic size)"),
                                TableRow("input", "inline-block", "UA chrome + caret text"),
                                TableRow("table", "block rows", "inline-block cells"),
                                TableRow("hr", "block", "one-pixel border")))),
                    H("footer", new Props { ClassName = "footer" },
                        Text("Rendered with Silk.NET + OpenGL 3.3 · "),
                        H("a", new Props { Href = "https://github.com" }, Text("Ignite")),
                        Text(" · CSS → layout → display list → OpenGL"))))
        );
    }

    private static Node NavButton(string label, string page, Action<string> setPage)
    {
        var className = page == label ? "nav-link active" : "nav-link";
        return H("button", new Props { ClassName = className, OnClick = () => setPage(label) }, Text(label));
    }

    private static Node Card(string title, string subtitle, params string[] tags)
    {
        return H("article", new Props { ClassName = "card" },
            H("h3", Text(title)),
            H("p", new Props { ClassName = "muted" }, Text(subtitle)),
            H("div", new Props { ClassName = "tags" },
                Each(tags, tag => H("span", new Props { ClassName = "tag" }, Text(tag)))));
    }

    private static Node TableRow(string tag, string display, string paint)
    {
        return H("tr",
            H("td", H("code", Text(tag))),
            H("td", Text(display)),
            H("td", Text(paint)));
    }
}

internal static class DemoStyles
{
    public const string Css = """
        body {
            background: linear-gradient(160deg, #f4f7fb 0%, #e8eef7 100%);
        }

        .app {
            display: flex;
            flex-direction: column;
            width: 100%;
            height: 100vh;
            overflow: hidden;
            background: linear-gradient(160deg, #f4f7fb 0%, #e8eef7 100%);
        }

        .topbar {
            display: flex;
            align-items: center;
            flex-shrink: 0;
            gap: 24px;
            padding: 14px 24px;
            background-color: #ffffff;
            border-bottom-width: 1px;
            border-bottom-style: solid;
            border-bottom-color: #d8e0ea;
            box-shadow: 0 6px 24px rgba(31, 35, 40, 0.06);
        }

        .brand { display: flex; align-items: center; gap: 12px; min-width: 260px; }
        .mark {
            display: inline-block;
            width: 36px;
            height: 36px;
            border-radius: 10px;
            background: linear-gradient(135deg, #0969da 0%, #7c3aed 100%);
            color: #ffffff;
            font-size: 22px;
            font-weight: 700;
            text-align: center;
            line-height: 36px;
        }
        .brand h1 { font-size: 18px; margin: 0; line-height: 1.1; }
        .subtitle { color: #697586; font-size: 12px; }

        .nav { display: flex; gap: 6px; flex: 1; }
        .nav-link {
            background-color: transparent;
            border: 0;
            border-radius: 8px;
            padding: 8px 12px;
            color: #57606a;
            font-weight: 600;
        }
        .nav-link:hover { background-color: #eef3f9; color: #1f2328; }
        .nav-link:focus-visible { outline-width: 2px; outline-style: solid; outline-color: #0969da; outline-offset: 2px; }
        .nav-link:active { background-color: #dbe8f8; }
        .nav-link.active { background-color: #0969da; color: #ffffff; }

        .search {
            width: 240px;
            background-color: #f6f8fa;
            border-color: #d8e0ea;
        }
        .search:focus { border-color: #0969da; background-color: #ffffff; outline-width: 3px; outline-style: solid; outline-color: rgba(9, 105, 218, 0.16); outline-offset: 0; }

        .body { display: flex; flex-direction: row; flex: 1; min-height: 0; }

        .sidebar {
            width: 260px;
            flex-shrink: 0;
            padding: 20px;
            background-color: #ffffff;
            border-right-width: 1px;
            border-right-style: solid;
            border-right-color: #d8e0ea;
            overflow-y: auto;
        }
        .sidebar h2 {
            font-size: 11px;
            color: #8c959f;
            text-transform: uppercase;
            letter-spacing: 1px;
            margin: 0 0 8px 0;
        }
        .tree { list-style: none; margin: 0 0 24px 0; padding: 0; }
        .tree-item {
            padding: 7px 10px;
            border-radius: 7px;
            color: #3d4652;
            font-size: 13px;
        }
        .tree-item:hover { background-color: #f0f5fb; color: #0969da; }
        .tree-item:active { background-color: #e2ecf9; }

        .control-row { display: flex; align-items: center; gap: 10px; margin: 10px 0; font-size: 13px; }
        .control-row input[type="checkbox"] { width: 18px; height: 18px; min-height: 18px; padding: 0; }
        .control-row progress { width: 140px; }

        .page-content { flex: 1; padding: 24px; overflow-y: auto; min-width: 0; }

        .hero {
            padding: 26px;
            border-radius: 18px;
            background: linear-gradient(120deg, #0b1220 0%, #16233a 58%, #243b63 100%);
            color: #ffffff;
            box-shadow: 0 18px 48px rgba(11, 18, 32, 0.24);
        }
        .hero h2 { font-size: 30px; margin: 0 0 10px 0; }
        .hero p { max-width: 720px; color: #c9d5e6; margin: 0 0 20px 0; }
        .hero-actions { display: flex; gap: 10px; }
        .primary {
            background-color: #2f81f7;
            border-color: #2f81f7;
            color: #ffffff;
        }
        .primary:hover { background-color: #1f6fe5; }
        .primary:active { background-color: #1559bd; }
        .primary:focus-visible, .ghost:focus-visible { outline-width: 2px; outline-style: solid; outline-color: #9cc4f5; outline-offset: 2px; }
        .ghost {
            background-color: rgba(255, 255, 255, 0.08);
            border-color: rgba(255, 255, 255, 0.22);
            color: #ffffff;
        }

        .cards {
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 14px;
            margin: 18px 0;
        }
        .card {
            background-color: #ffffff;
            border-width: 1px;
            border-style: solid;
            border-color: #dde5ef;
            border-radius: 14px;
            padding: 18px;
            box-shadow: 0 8px 22px rgba(31, 35, 40, 0.05);
        }
        .card:hover {
            border-color: #9cc4f5;
            box-shadow: 0 12px 30px rgba(9, 105, 218, 0.12);
        }
        .card:focus-within {
            border-color: #0969da;
            box-shadow: 0 0 0 3px rgba(9, 105, 218, 0.14);
        }
        .card h3 { margin: 0 0 4px 0; font-size: 15px; }
        .card .muted { margin: 0 0 12px 0; color: #697586; font-size: 13px; }
        .tags { display: flex; flex-wrap: wrap; gap: 6px; }
        .tag {
            display: inline-block;
            flex-shrink: 0;
            padding: 3px 8px;
            border-radius: 999px;
            background-color: #eef4fb;
            color: #0969da;
            font-size: 11px;
            font-weight: 600;
        }

        .table-card {
            background-color: #ffffff;
            border-width: 1px;
            border-style: solid;
            border-color: #dde5ef;
            border-radius: 14px;
            padding: 18px;
            margin-bottom: 18px;
        }
        .table-card table { width: 100%; margin: 10px 0 0 0; }
        .table-card th, .table-card td { width: 32%; border-color: #e6ebf2; font-size: 13px; }
        .table-card th { text-align: left; }
        .table-card code { background-color: #f6f8fa; border-radius: 4px; padding: 2px 5px; }
        .footer { color: #697586; font-size: 12px; padding: 4px 0 18px 0; }
        """;
}
