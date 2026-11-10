using Ignite.UILab;
using Ignite.Windowing;
using static Ignite.UILab._;

var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "bootstrap.min.css");

if (!File.Exists(bootstrapPath))
{
    Console.Error.WriteLine($"Bootstrap stylesheet not found at {bootstrapPath}.");
    return 1;
}

var bootstrap = File.ReadAllText(bootstrapPath);
var window = new IgniteWindow(WindowSettings.Default with
{
    Width = 1280,
    Height = 900,
    Title = "Bootstrap + Ignite"
});

var captureIndex = Array.FindIndex(args, a => string.Equals(a, "--capture", StringComparison.OrdinalIgnoreCase));

if (captureIndex >= 0 && captureIndex + 1 < args.Length)
{
    var path = Path.GetFullPath(args[captureIndex + 1]);
    window.BeforeFrame += (host, _) =>
    {
        if (host.FrameCount == 2)
        {
            host.RequestScreenshot(path);
        }
    };
}

if (args.Contains("--smoke", StringComparer.OrdinalIgnoreCase))
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

window.Run(
    H("html",
        new Props { Attributes = new Dictionary<string, string> { ["lang"] = "en" } },
        H("head", H("title", Text("Bootstrap + Ignite"))),
        H("body", new Props { ClassName = "bg-body-tertiary" },
            C<BootstrapDemo>())),
    bootstrap + "\n" + DemoCss.Value);

return 0;

internal sealed class BootstrapDemo : Component
{
    public override Node Render()
    {
        var (count, setCount) = State(0);
        var (search, setSearch) = State(string.Empty);
        var (activeTab, setActiveTab) = State("Overview");

        var footer = H("footer", new Props { ClassName = "d-flex flex-wrap justify-content-between align-items-center py-4 my-4 border-top" },
            H("div", new Props { ClassName = "col-md-6 mb-0 text-body-secondary" },
                Text("Bootstrap 5.3 rendered by Ignite's CSS engine, layout engine and OpenGL display list.")),
            H("div", new Props { ClassName = "col-md-6" },
                H("ul", new Props { ClassName = "nav justify-content-end" },
                    H("li", new Props { ClassName = "nav-item" },
                        H("a", new Props { ClassName = "nav-link px-2 text-body-secondary", Href = "#" }, Text("Home"))),
                    H("li", new Props { ClassName = "nav-item" },
                        H("a", new Props { ClassName = "nav-link px-2 text-body-secondary", Href = "#" }, Text("Docs"))),
                    H("li", new Props { ClassName = "nav-item" },
                        H("a", new Props { ClassName = "nav-link px-2 text-body-secondary", Href = "#" }, Text("About"))))));

        var formRow = H("div", new Props { ClassName = "row g-4 mt-1" },
            H("div", new Props { ClassName = "col-lg-7" }, BuildForm(() => setCount(count + 1))),
            H("div", new Props { ClassName = "col-lg-5" }, BuildProgressAndAlerts()));

        var content = H("div", new Props { ClassName = "container py-4" },
            BuildBreadcrumb(),
            BuildHero(count, setCount),
            Show(activeTab is "Overview" or "Layout", BuildStats()),
            BuildTabs(activeTab, setActiveTab),
            Show(activeTab is "Overview" or "Components" or "Utilities", BuildCards(search)),
            Show(activeTab is "Overview" or "Layout", formRow),
            Show(activeTab is "Overview" or "Components", BuildTable(() => setCount(count + 1))),
            Show(activeTab is "Overview" or "Utilities", BuildListAndPagination()),
            footer);

        var main = H("main", new Props { ClassName = "app-main" }, content);

        return H("div", new Props { ClassName = "d-flex flex-column h-100" },
            BuildNavbar(count, setCount, search, setSearch),
            main);
    }

    private static Node BuildNavbar(
        int count,
        Action<int> setCount,
        string search,
        Action<string> setSearch)
    {
        return H("nav",
            new Props
            {
                ClassName = "navbar navbar-expand-lg bg-dark border-bottom border-body",
                Attributes = new Dictionary<string, string> { ["data-bs-theme"] = "dark" }
            },
            H("div", new Props { ClassName = "container-fluid" },
                H("a", new Props { ClassName = "navbar-brand fw-semibold", Href = "#" },
                    H("span", new Props { ClassName = "badge text-bg-primary me-2" }, Text("I")),
                    Text("Ignite Bootstrap")),
                H("button",
                    new Props
                    {
                        ClassName = "navbar-toggler",
                        Type = "button",
                        Attributes = new Dictionary<string, string>
                        {
                            ["data-bs-toggle"] = "collapse",
                            ["data-bs-target"] = "#mainNav"
                        }
                    },
                    H("span", new Props { ClassName = "navbar-toggler-icon" })),
                H("div", new Props { ClassName = "collapse navbar-collapse show", Id = "mainNav" },
                    H("ul", new Props { ClassName = "navbar-nav me-auto mb-2 mb-lg-0" },
                        NavItem("Overview", true),
                        NavItem("Components", false),
                        NavItem("Layout", false),
                        NavItem("Docs", false)),
                    H("form", new Props { ClassName = "d-flex", Role = "search" },
                        H("input",
                            new Props
                            {
                                ClassName = "form-control me-2",
                                Type = "search",
                                Placeholder = "Search components",
                                Value = search,
                                OnInput = setSearch
                            }),
                        H("button",
                            new Props
                            {
                                ClassName = "btn btn-outline-success",
                                Type = "button",
                                OnClick = () => setCount(count + 1)
                            },
                            Text($"Search ({count})"))))));
    }

    private static Node NavItem(string label, bool active)
    {
        var className = active ? "nav-link active" : "nav-link";
        return H("li", new Props { ClassName = "nav-item" },
            H("a", new Props { ClassName = className, Href = "#" }, Text(label)));
    }

    private static Node BuildBreadcrumb()
    {
        return H("nav", new Props { Attributes = new Dictionary<string, string> { ["aria-label"] = "breadcrumb" } },
            H("ol", new Props { ClassName = "breadcrumb" },
                H("li", new Props { ClassName = "breadcrumb-item" },
                    H("a", new Props { Href = "#" }, Text("Home"))),
                H("li", new Props { ClassName = "breadcrumb-item" }, Text("Components")),
                H("li", new Props { ClassName = "breadcrumb-item active", Attributes = new Dictionary<string, string> { ["aria-current"] = "page" } },
                    Text("Overview"))));
    }

    private static Node BuildHero(int count, Action<int> setCount)
    {
        return H("div", new Props { ClassName = "p-5 mb-4 bg-body rounded-3 border shadow-sm" },
            H("div", new Props { ClassName = "container-fluid py-4" },
                H("h1", new Props { ClassName = "display-5 fw-bold" }, Text("Bootstrap, meet Ignite.")),
                H("p", new Props { ClassName = "col-md-9 fs-5 text-body-secondary" },
                    Text("Every card, button, badge and form control below is styled entirely by the real Bootstrap 5.3 stylesheet. "
                        + "The CSS cascade feeds LayoutLab, which emits a compact display list and paints it with OpenGL.")),
                H("div", new Props { ClassName = "d-flex flex-wrap gap-2 align-items-center" },
                    H("button",
                        new Props { ClassName = "btn btn-primary btn-lg px-4", Type = "button", OnClick = () => setCount(count + 1) },
                        Text($"Clicked {count} times")),
                    H("button", new Props { ClassName = "btn btn-outline-secondary btn-lg px-4", Type = "button" }, Text("Secondary")),
                    H("span", new Props { ClassName = "badge rounded-pill text-bg-success ms-2" }, Text("Live")))));
    }

    private static Node BuildStats()
    {
        return H("div", new Props { ClassName = "row g-3 mb-4" },
            StatCard("CSS rules loaded", "10,000+", "primary"),
            StatCard("Flex and grid", "Native", "success"),
            StatCard("Draw calls", "Batched", "warning"),
            StatCard("Text", "TrueType", "info"));
    }

    private static Node StatCard(string label, string value, string color)
    {
        return H("div", new Props { ClassName = "col-6 col-lg-3" },
            H("div", new Props { ClassName = "card h-100 border-0 shadow-sm" },
                H("div", new Props { ClassName = "card-body" },
                    H("div", new Props { ClassName = $"text-{color} fw-bold fs-3" }, Text(value)),
                    H("div", new Props { ClassName = "text-body-secondary small text-uppercase" }, Text(label)))));
    }

    private static Node BuildTabs(string activeTab, Action<string> setActiveTab)
    {
        return H("ul", new Props { ClassName = "nav nav-tabs mb-3" },
            Tab("Overview", activeTab, setActiveTab),
            Tab("Components", activeTab, setActiveTab),
            Tab("Layout", activeTab, setActiveTab),
            Tab("Utilities", activeTab, setActiveTab));
    }

    private static Node Tab(string label, string activeTab, Action<string> setActiveTab)
    {
        var className = activeTab == label ? "nav-link active" : "nav-link";
        return H("li", new Props { ClassName = "nav-item" },
            H("button", new Props { ClassName = className, Type = "button", OnClick = () => setActiveTab(label) },
                Text(label)));
    }

    private static Node BuildCards(string search)
    {
        var cards = new[]
        {
            ("Cards", "Bootstrap cards", "border-primary", "text-bg-primary", "Rounded corners, borders, shadows and hover states."),
            ("Forms", "Inputs and validation", "border-success", "text-bg-success", "Focus rings, placeholders and disabled controls."),
            ("Tables", "Rows and cells", "border-warning", "text-bg-warning", "Striped rows, hover states and responsive wrappers."),
            ("Alerts", "Contextual feedback", "border-danger", "text-bg-danger", "Inline alerts, badges and progress bars."),
            ("Typography", "Headings and text", "border-info", "text-bg-info", "Display sizes, muted text, lead paragraphs and links."),
            ("Utilities", "Spacing and flex", "border-secondary", "text-bg-secondary", "Gap, alignment, wrapping and responsive columns.")
        };

        return H("div", new Props { ClassName = "row g-4" },
            Each(cards, card =>
            {
                var matches = string.IsNullOrWhiteSpace(search)
                    || card.Item1.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || card.Item2.Contains(search, StringComparison.OrdinalIgnoreCase);

                if (!matches)
                {
                    return null;
                }

                return H("div", new Props { ClassName = "col-md-6 col-xl-4" },
                    H("div", new Props { ClassName = $"card h-100 shadow-sm border-2 {card.Item3}" },
                        H("div", new Props { ClassName = "card-body d-flex flex-column" },
                            H("div", new Props { ClassName = "d-flex justify-content-between align-items-start mb-3" },
                                H("h5", new Props { ClassName = "card-title mb-0" }, Text(card.Item1)),
                                H("span", new Props { ClassName = $"badge rounded-pill {card.Item4}" }, Text(card.Item2))),
                            H("p", new Props { ClassName = "card-text text-body-secondary flex-grow-1" }, Text(card.Item5)),
                            H("a", new Props { ClassName = "btn btn-sm btn-outline-primary align-self-start", Href = "#" },
                                Text("Open " + card.Item1.ToLowerInvariant())))));
            }));
    }

    private static Node BuildForm(Action onSubmit)
    {
        return H("div", new Props { ClassName = "card shadow-sm h-100" },
            H("div", new Props { ClassName = "card-header bg-body fw-semibold" }, Text("Form controls")),
            H("div", new Props { ClassName = "card-body" },
                H("div", new Props { ClassName = "mb-3" },
                    H("label", new Props { ClassName = "form-label" }, Text("Email address")),
                    H("input", new Props { ClassName = "form-control", Type = "email", Placeholder = "name@example.com" }),
                    H("div", new Props { ClassName = "form-text" }, Text("We'll never share your email."))),
                H("div", new Props { ClassName = "row g-2 mb-3" },
                    H("div", new Props { ClassName = "col-md-6" },
                        H("label", new Props { ClassName = "form-label" }, Text("First name")),
                        H("input", new Props { ClassName = "form-control", Type = "text", Placeholder = "Ada" })),
                    H("div", new Props { ClassName = "col-md-6" },
                        H("label", new Props { ClassName = "form-label" }, Text("Last name")),
                        H("input", new Props { ClassName = "form-control", Type = "text", Placeholder = "Lovelace" }))),
                H("div", new Props { ClassName = "mb-3" },
                    H("label", new Props { ClassName = "form-label" }, Text("Workspace")),
                    H("select", new Props { ClassName = "form-select" },
                        H("option", Text("Choose one")),
                        H("option", Text("Personal")))),
                H("div", new Props { ClassName = "form-check mb-2" },
                    H("input", new Props { ClassName = "form-check-input", Type = "checkbox", Checked = true }),
                    H("label", new Props { ClassName = "form-check-label" }, Text("Remember this device"))),
                H("div", new Props { ClassName = "form-check mb-3" },
                    H("input", new Props { ClassName = "form-check-input", Type = "checkbox", Disabled = true }),
                    H("label", new Props { ClassName = "form-check-label text-body-secondary" }, Text("Disabled option"))),
                H("div", new Props { ClassName = "d-flex gap-2" },
                    H("button", new Props { ClassName = "btn btn-primary", Type = "button", OnClick = onSubmit }, Text("Submit")),
                    H("button", new Props { ClassName = "btn btn-outline-secondary", Type = "button" }, Text("Reset")))));
    }

    private static Node BuildProgressAndAlerts()
    {
        return H("div", new Props { ClassName = "d-flex flex-column gap-3 h-100" },
            H("div", new Props { ClassName = "card shadow-sm" },
                H("div", new Props { ClassName = "card-body" },
                    H("div", new Props { ClassName = "d-flex justify-content-between mb-2" },
                        H("span", new Props { ClassName = "fw-semibold" }, Text("Bootstrap progress")),
                        H("span", new Props { ClassName = "text-body-secondary" }, Text("72%"))),
                    H("div", new Props { ClassName = "progress", Role = "progressbar" },
                        H("div", new Props { ClassName = "progress-bar demo-progress-72" }, Text("72%"))),
                    H("div", new Props { ClassName = "progress mt-3" },
                        H("div", new Props { ClassName = "progress-bar bg-success demo-progress-38" }, Text("38%"))))),
            H("div", new Props { ClassName = "alert alert-info d-flex align-items-center gap-2 mb-0", Role = "alert" },
                H("strong", Text("Info.")),
                Text(" Alerts use Bootstrap background and border variables.")),
            H("div", new Props { ClassName = "alert alert-warning mb-0", Role = "alert" },
                Text("This warning is styled by the same cascade.")),
            H("div", new Props { ClassName = "alert alert-success mb-0", Role = "alert" },
                Text("Success feedback with a rounded border.")));
    }

    private static Node BuildTable(Action onRowClick)
    {
        return H("div", new Props { ClassName = "card shadow-sm mt-4" },
            H("div", new Props { ClassName = "card-header bg-body d-flex justify-content-between align-items-center" },
                H("span", new Props { ClassName = "fw-semibold" }, Text("HTML table")),
                H("span", new Props { ClassName = "badge text-bg-secondary" }, Text("6 rows"))),
            H("div", new Props { ClassName = "table-responsive" },
                H("table", new Props { ClassName = "table table-striped table-hover align-middle mb-0" },
                    H("thead",
                        H("tr",
                            H("th", new Props { Attributes = new Dictionary<string, string> { ["scope"] = "col" } }, Text("#")),
                            H("th", new Props { Attributes = new Dictionary<string, string> { ["scope"] = "col" } }, Text("Feature")),
                            H("th", new Props { Attributes = new Dictionary<string, string> { ["scope"] = "col" } }, Text("Status")),
                            H("th", new Props { Attributes = new Dictionary<string, string> { ["scope"] = "col" } }, Text("Notes")))),
                    H("tbody",
                        TableRow("1", "Cascade", "stable", "Specificity and source order", onRowClick),
                        TableRow("2", "Block flow", "stable", "Margins, padding and borders", onRowClick),
                        TableRow("3", "Flexbox", "stable", "Grow, shrink, wrap and gap", onRowClick),
                        TableRow("4", "Grid", "stable", "Tracks, spans and auto flow", onRowClick),
                        TableRow("5", "Paint", "stable", "24-byte instructions", onRowClick),
                        TableRow("6", "OpenGL", "stable", "Batched shapes and glyphs", onRowClick)))));
    }

    private static Node TableRow(string index, string feature, string status, string notes, Action onRowClick)
    {
        return H("tr",
            new Props { OnClick = onRowClick },
            H("th", new Props { Attributes = new Dictionary<string, string> { ["scope"] = "row" } }, Text(index)),
            H("td", Text(feature)),
            H("td", H("span", new Props { ClassName = "badge text-bg-success" }, Text(status))),
            H("td", new Props { ClassName = "text-body-secondary" }, Text(notes)));
    }

    private static Node BuildListAndPagination()
    {
        return H("div", new Props { ClassName = "row g-4 mt-1" },
            H("div", new Props { ClassName = "col-lg-7" },
                H("div", new Props { ClassName = "card shadow-sm h-100" },
                    H("div", new Props { ClassName = "card-header bg-body fw-semibold" }, Text("List group")),
                    H("ul", new Props { ClassName = "list-group list-group-flush" },
                        ListItem("Cascade and inheritance", "12 rules"),
                        ListItem("Layout and text", "3.6 ms warm"),
                        ListItem("Display list", "120 instructions"),
                        ListItem("OpenGL", "0.7 ms render")))),
            H("div", new Props { ClassName = "col-lg-5" },
                H("div", new Props { ClassName = "card shadow-sm h-100" },
                    H("div", new Props { ClassName = "card-body d-flex flex-column" },
                        H("h5", new Props { ClassName = "card-title" }, Text("Pagination")),
                        H("p", new Props { ClassName = "card-text text-body-secondary" }, Text("Bootstrap's pagination is a flex container with nested page links.")),
                        H("nav", new Props { ClassName = "mt-auto", Attributes = new Dictionary<string, string> { ["aria-label"] = "pagination" } },
                            H("ul", new Props { ClassName = "pagination pagination-sm mb-0" },
                                H("li", new Props { ClassName = "page-item disabled" },
                                    H("a", new Props { ClassName = "page-link", Href = "#" }, Text("Previous"))),
                                PageItem("1", true),
                                PageItem("2", false),
                                PageItem("3", false),
                                H("li", new Props { ClassName = "page-item" },
                                    H("a", new Props { ClassName = "page-link", Href = "#" }, Text("Next")))))))));
    }

    private static Node ListItem(string title, string value)
    {
        return H("li", new Props { ClassName = "list-group-item d-flex justify-content-between align-items-center" },
            Text(title),
            H("span", new Props { ClassName = "badge text-bg-primary rounded-pill" }, Text(value)));
    }

    private static Node PageItem(string label, bool active)
    {
        return H("li", new Props { ClassName = active ? "page-item active" : "page-item" },
            H("a", new Props { ClassName = "page-link", Href = "#" }, Text(label)));
    }
}

internal static class DemoCss
{
    public const string Value = """
        html, body {
            height: 100%;
        }

        body {
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }

        .app-main {
            flex: 1;
            min-height: 0;
            overflow-y: auto;
        }

        .demo-progress-72 {
            width: 72%;
        }

        .demo-progress-38 {
            width: 38%;
        }
        """;
}
