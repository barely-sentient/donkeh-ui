using System.Globalization;
using Donkeh.Data.Dom;

namespace Donkeh.Data.Benchmarks;

/// <summary>
/// Builds a Bootstrap-shaped document: html/head/body, a navbar, a hero, a
/// footer, and N repeated component sections. Each section contains a card, a
/// list group, a table and a form, so the matcher sees realistic ancestor
/// chains, sibling groups and the attribute/property combinations real pages
/// use. Text nodes are omitted because the engine matches elements only.
/// </summary>
public static class PageTree
{
    /// <summary>
    /// Builds the tree. Each section adds roughly sixty elements, so the caller
    /// controls the stress level through <paramref name="sections"/>.
    /// </summary>
    public static IgniteSupportedDomNode Build(int sections)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sections);

        var body = new List<IgniteSupportedDomNode>(sections + 4)
        {
            Navbar(),
            Hero()
        };

        for (var i = 0; i < sections; i++)
        {
            body.Add(Section(i));
        }

        body.Add(Footer());

        var head = El("head", null,
            Attrs(El("meta"), ("charset", "utf-8")),
            Attrs(El("meta"),
                ("name", "viewport"),
                ("content", "width=device-width, initial-scale=1")),
            El("title"),
            Attrs(El("link"), ("rel", "stylesheet"), ("href", BootstrapCss.CdnUrl)));

        return El("html", null,
            head,
            Attrs(
                El("body", "bg-body-tertiary", body.ToArray()),
                ("data-bs-theme", "light")));
    }

    /// <summary>
    /// Depth-first, document-order flattening of the element tree.
    /// </summary>
    public static IgniteSupportedDomNode[] Flatten(IgniteSupportedDomNode root)
    {
        var nodes = new List<IgniteSupportedDomNode>(1024);
        var stack = new Stack<IgniteSupportedDomNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            nodes.Add(node);

            var children = node.Children;

            for (var i = children.Length - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return nodes.ToArray();
    }

    /// <summary>
    /// Returns the last element carrying <paramref name="className"/>, which is
    /// the deepest representative of that component in the page.
    /// </summary>
    public static IgniteSupportedDomNode? FindLastWithClass(
        IgniteSupportedDomNode[] nodes,
        string className)
    {
        for (var i = nodes.Length - 1; i >= 0; i--)
        {
            if (nodes[i].HasClass(className))
            {
                return nodes[i];
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the first element with <paramref name="tagName"/> in document
    /// order. Used to grab a node (such as <c>title</c>) that should match
    /// almost nothing.
    /// </summary>
    public static IgniteSupportedDomNode? FindFirstTag(
        IgniteSupportedDomNode[] nodes,
        string tagName)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            if (string.Equals(nodes[i].TagName, tagName, StringComparison.Ordinal))
            {
                return nodes[i];
            }
        }

        return null;
    }

    private static IgniteSupportedDomNode Navbar()
    {
        var toggle = Attrs(
            El("button", "navbar-toggler",
                El("span", "navbar-toggler-icon")),
            ("type", "button"),
            ("data-bs-toggle", "collapse"),
            ("data-bs-target", "#benchmark-nav"),
            ("aria-controls", "benchmark-nav"),
            ("aria-expanded", "false"),
            ("aria-label", "Toggle navigation"));

        var links = El("ul", "navbar-nav me-auto mb-2 mb-lg-0",
            El("li", "nav-item",
                Attrs(El("a", "nav-link active"), ("aria-current", "page"), ("href", "#"))),
            El("li", "nav-item",
                Attrs(El("a", "nav-link"), ("href", "#"))),
            El("li", "nav-item dropdown",
                Attrs(
                    El("a", "nav-link dropdown-toggle"),
                    ("href", "#"),
                    ("role", "button"),
                    ("data-bs-toggle", "dropdown"),
                    ("aria-expanded", "false"))));

        var search = Attrs(
            El("form", "d-flex",
                Attrs(
                    El("input", "form-control me-2"),
                    ("type", "search"),
                    ("placeholder", "Search"),
                    ("aria-label", "Search")),
                Attrs(El("button", "btn btn-outline-success"), ("type", "submit"))),
            ("role", "search"));

        var collapse = WithId(
            El("div", "collapse navbar-collapse", links, search),
            "benchmark-nav");

        return El("nav", "navbar navbar-expand-lg bg-body-tertiary",
            El("div", "container-fluid",
                Attrs(El("a", "navbar-brand"), ("href", "#")),
                toggle,
                collapse));
    }

    private static IgniteSupportedDomNode Hero() =>
        El("div", "p-5 mb-4 bg-body-tertiary rounded-3",
            El("div", "container-fluid py-5",
                El("h1", "display-5 fw-bold"),
                El("p", "col-md-8 fs-4"),
                Attrs(El("a", "btn btn-primary btn-lg"), ("href", "#"), ("role", "button")),
                Attrs(El("a", "btn btn-outline-secondary btn-lg"), ("href", "#"), ("role", "button"))));

    private static IgniteSupportedDomNode Section(int index)
    {
        var suffix = index.ToString(CultureInfo.InvariantCulture);

        var card = El("div", "card shadow-sm border-0",
            El("div", "card-header d-flex justify-content-between align-items-center",
                El("span", "badge rounded-pill text-bg-primary"),
                El("small", "text-body-secondary")),
            El("div", "card-body",
                El("h2", "card-title h4"),
                El("p", "card-text"),
                Attrs(El("a", "btn btn-primary"), ("href", "#"), ("role", "button")),
                Attrs(
                    El("button", "btn btn-outline-secondary"),
                    ("type", "button"),
                    ("disabled", "disabled"))),
            El("div", "card-footer text-body-secondary"));

        var list = El("ul", "list-group shadow-sm",
            Attrs(El("li", "list-group-item active"), ("aria-current", "true")),
            ListItem(),
            ListItem(),
            ListItem());

        var headerCells = new IgniteSupportedDomNode[4];

        for (var i = 0; i < headerCells.Length; i++)
        {
            headerCells[i] = Attrs(El("th"), ("scope", "col"));
        }

        var rows = new IgniteSupportedDomNode[4];

        for (var r = 0; r < rows.Length; r++)
        {
            var cells = new IgniteSupportedDomNode[4];

            for (var c = 0; c < cells.Length; c++)
            {
                cells[c] = El("td");
            }

            rows[r] = El("tr", null, cells);
        }

        var table = El("table", "table table-striped table-hover align-middle",
            El("thead", null, El("tr", null, headerCells)),
            El("tbody", null, rows));

        var emailId = "email-" + suffix;
        var selectId = "select-" + suffix;
        var checkId = "check-" + suffix;

        var form = El("form", "row g-3 mt-4",
            El("div", "col-md-6",
                Attrs(El("label", "form-label"), ("for", emailId)),
                Attrs(
                    El("input", "form-control"),
                    ("id", emailId),
                    ("type", "email"),
                    ("placeholder", "name@example.com"),
                    ("required", "required"))),
            El("div", "col-md-6",
                Attrs(El("label", "form-label"), ("for", selectId)),
                Attrs(
                    El("select", "form-select",
                        El("option"),
                        El("option")),
                    ("id", selectId))),
            El("div", "col-12",
                El("div", "form-check",
                    Attrs(
                        El("input", "form-check-input"),
                        ("id", checkId),
                        ("type", "checkbox")),
                    Attrs(El("label", "form-check-label"), ("for", checkId)))),
            El("div", "col-12",
                Attrs(El("button", "btn btn-success"), ("type", "submit"))));

        return El("section", "py-4",
            El("div", "container",
                El("div", "row g-4 align-items-center",
                    El("div", "col-lg-7", card),
                    El("div", "col-lg-5", list)),
                El("div", "table-responsive mt-4", table),
                form));
    }

    private static IgniteSupportedDomNode ListItem() =>
        El("li", "list-group-item d-flex justify-content-between align-items-center",
            El("span", "badge bg-primary rounded-pill"),
            El("span"));

    private static IgniteSupportedDomNode Footer() =>
        El("footer", "footer mt-auto py-3 bg-body-tertiary",
            El("div", "container",
                El("p", "text-body-secondary text-center mb-0")));

    private static IgniteSupportedDomNode El(
        string tag,
        string? classes = null,
        params IgniteSupportedDomNode[] children)
    {
        var node = new IgniteSupportedDomNode { TagName = tag };

        if (!string.IsNullOrEmpty(classes))
        {
            AddClasses(node, classes);
        }

        if (children.Length != 0)
        {
            node.Children = children;
        }

        return node;
    }

    private static IgniteSupportedDomNode Attrs(
        IgniteSupportedDomNode node,
        params (string Name, string Value)[] attributes)
    {
        foreach (var (name, value) in attributes)
        {
            node.SetAttribute(name, value);
        }

        return node;
    }

    private static IgniteSupportedDomNode WithId(IgniteSupportedDomNode node, string id)
    {
        node.Id = id;
        return node;
    }

    private static void AddClasses(IgniteSupportedDomNode node, string classes)
    {
        var start = 0;

        for (var i = 0; i <= classes.Length; i++)
        {
            if (i < classes.Length && classes[i] != ' ')
            {
                continue;
            }

            if (i > start)
            {
                node.AddClass(classes[start..i]);
            }

            start = i + 1;
        }
    }
}
