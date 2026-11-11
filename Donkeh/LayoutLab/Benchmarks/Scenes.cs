using Donkeh.Data;
using Donkeh.Data.Dom;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Benchmarks;

/// <summary>Kinds of DOM tree used by the benchmarks.</summary>
public enum SceneKind
{
    Block,
    Flex,
    Grid,
    Paint,
    Mixed
}

/// <summary>
/// Deterministic benchmark scenes. A single stylesheet covers every scene and is
/// compiled once; each scene builds a DOM tree whose node count is close to the
/// requested value. Text is varied per node so the text-layout cache is not
/// unrealistically effective.
/// </summary>
public static class Scenes
{
    private static readonly Lazy<DataTable> SharedTable = new(
        static () => new Engine(Condition.AlwaysTrueCompiler).Build(Css),
        LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>The compiled stylesheet shared by every benchmark.</summary>
    public static DataTable Table => SharedTable.Value;

    /// <summary>Builds a fresh machine-readable DOM tree for a scene.</summary>
    public static IgniteSupportedDomNode Build(SceneKind scene, int targetNodes)
    {
        targetNodes = Math.Max(8, targetNodes);

        return scene switch
        {
            SceneKind.Block => BuildBlock(targetNodes),
            SceneKind.Flex => BuildFlex(targetNodes),
            SceneKind.Grid => BuildGrid(targetNodes),
            SceneKind.Paint => BuildPaint(targetNodes),
            SceneKind.Mixed => BuildMixed(targetNodes),
            _ => throw new ArgumentOutOfRangeException(nameof(scene), scene, "Unknown scene.")
        };
    }

    /// <summary>Creates one element with an optional id, text and classes.</summary>
    public static IgniteSupportedDomNode Element(
        string tag,
        string? id = null,
        string? text = null,
        params string[] classes)
    {
        var node = new IgniteSupportedDomNode { TagName = tag };

        if (id is not null)
        {
            node.Id = id;
        }

        if (text is not null)
        {
            node.TextContent = text;
        }

        foreach (var className in classes)
        {
            node.AddClass(className);
        }

        return node;
    }

    /// <summary>Counts every element in a tree, including the root.</summary>
    public static int Count(IgniteSupportedDomNode node)
    {
        var count = 1;

        foreach (var child in node.Children)
        {
            count += Count(child);
        }

        return count;
    }

    /// <summary>Options used by the benchmark frames.</summary>
    public static LayoutOptions Options(
        bool optimise = true,
        bool compact = false,
        bool caching = true,
        bool cull = true,
        bool merge = true,
        bool clip = true)
    {
        return new LayoutOptions
        {
            ViewportWidth = 1280,
            ViewportHeight = 800,
            Optimise = optimise,
            CompactConstants = compact,
            EnableCaching = caching,
            CullOffscreen = cull,
            MergeDraws = merge,
            ClipToViewport = clip
        };
    }

    private static IgniteSupportedDomNode BuildBlock(int target)
    {
        var root = Element("div", "root", null, "block-root");
        var sections = new List<IgniteSupportedDomNode>(Math.Max(1, target / 6));
        var sectionCount = Math.Max(1, (target - 1) / 6);

        for (var i = 0; i < sectionCount; i++)
        {
            var section = Element("section", null, null, "block-section");
            var title = Element("h2", null, $"Section {i}", "block-title");
            var body = Element("p", null, Paragraph(i), "block-text");
            var cards = new List<IgniteSupportedDomNode>(3);

            for (var card = 0; card < 3; card++)
            {
                cards.Add(Element(
                    "div",
                    null,
                    $"Card {i}.{card}: {Paragraph(i + card)}",
                    "block-card"));
            }

            section.Children = [title, body, .. cards];
            sections.Add(section);
        }

        root.Children = [.. sections];
        return root;
    }

    private static IgniteSupportedDomNode BuildFlex(int target)
    {
        var root = Element("div", "root", null, "flex-root");
        var header = Element("header", null, null, "flex-header");
        var brand = Element("div", null, "LayoutLab", "flex-brand");
        var nav = Element("nav", null, null, "flex-nav");
        var links = new List<IgniteSupportedDomNode>(5);

        for (var i = 0; i < 5; i++)
        {
            links.Add(Element("span", null, $"Link {i}", "flex-link"));
        }

        nav.Children = [.. links];
        header.Children = [brand, nav];

        var main = Element("main", null, null, "flex-main");
        var cardCount = Math.Max(1, (target - 10) / 3);
        var cards = new List<IgniteSupportedDomNode>(cardCount);

        for (var i = 0; i < cardCount; i++)
        {
            var classes = i % 11 == 0
                ? new[] { "flex-card", "grow" }
                : ["flex-card"];

            var card = Element("div", null, null, classes);
            var title = Element("div", null, $"Card {i}", "flex-title");
            var body = Element("p", null, Paragraph(i), "flex-body");
            card.Children = [title, body];
            cards.Add(card);
        }

        main.Children = [.. cards];

        var footer = Element("footer", null, "LayoutLab benchmark footer", "flex-footer");
        root.Children = [header, main, footer];
        return root;
    }

    private static IgniteSupportedDomNode BuildGrid(int target)
    {
        var root = Element("div", "root", null, "grid-root");
        var items = new List<IgniteSupportedDomNode>(Math.Max(1, target - 1));

        for (var i = 0; i < target - 1; i++)
        {
            var classes = i == 0
                ? new[] { "grid-item", "hero" }
                : i % 11 == 0
                    ? ["grid-item", "wide"]
                    : i % 13 == 0
                        ? ["grid-item", "tall"]
                        : ["grid-item"];

            items.Add(Element("div", null, $"Cell {i}", classes));
        }

        root.Children = [.. items];
        return root;
    }

    private static IgniteSupportedDomNode BuildPaint(int target)
    {
        var root = Element("div", "root", null, "paint-root");
        var cardCount = Math.Max(1, (target - 1) / 3);
        var cards = new List<IgniteSupportedDomNode>(cardCount);

        for (var i = 0; i < cardCount; i++)
        {
            var classes = new List<string>(4) { "paint-card" };

            if (i % 3 == 0)
            {
                classes.Add("shadow");
            }

            if (i % 4 == 0)
            {
                classes.Add("gradient");
            }

            if (i % 5 == 0)
            {
                classes.Add("fade");
            }

            if (i % 7 == 0)
            {
                classes.Add("shift");
            }

            if (i % 11 == 0)
            {
                classes.Add("blur");
            }

            if (i % 13 == 0)
            {
                classes.Add("clipped");
            }

            if (i % 17 == 0)
            {
                classes.Add("outline");
            }

            var card = Element("div", null, null, [.. classes]);
            var title = Element("div", null, $"Card {i}", "paint-title");
            var body = Element("p", null, Paragraph(i), "paint-body");
            card.Children = [title, body];
            cards.Add(card);
        }

        root.Children = [.. cards];
        return root;
    }

    private static IgniteSupportedDomNode BuildMixed(int target)
    {
        var root = Element("div", "root", null, "mixed-root");
        var header = Element("header", null, "LayoutLab", "mixed-header");
        var shell = Element("div", null, null, "mixed-shell");
        var sidebar = Element("aside", null, null, "mixed-sidebar");
        var main = Element("main", null, null, "mixed-main");
        var footer = Element("footer", null, "LayoutLab benchmark footer", "mixed-footer");

        var sidebarCount = Math.Max(1, (target / 6));
        var sidebarItems = new List<IgniteSupportedDomNode>(sidebarCount);

        for (var i = 0; i < sidebarCount; i++)
        {
            sidebarItems.Add(Element("div", null, $"Section {i}", "sidebar-item"));
        }

        sidebar.Children = [.. sidebarItems];

        var cardCount = Math.Max(1, (target - 6 - sidebarCount) / 3);
        var cards = new List<IgniteSupportedDomNode>(cardCount);

        for (var i = 0; i < cardCount; i++)
        {
            var card = Element("article", null, null, "mixed-card");
            var title = Element("h2", null, $"Article {i}", "mixed-title");
            var body = Element("p", null, Paragraph(i), "mixed-text");
            card.Children = [title, body];
            cards.Add(card);
        }

        main.Children = [.. cards];
        shell.Children = [sidebar, main];
        root.Children = [header, shell, footer];
        return root;
    }

    private static string Paragraph(int index)
    {
        return $"Item {index} wraps a sentence across several lines so text layout, "
            + "measurement and line breaking are part of the measured frame.";
    }

    private const string Css = """
        #root {
            font-size: 16px;
            color: #1f2937;
            background-color: #ffffff;
            width: 100%;
        }

        /* ---------------------------------------------------------- block */
        .block-section {
            margin: 0 0 12px 0;
            padding: 8px 12px;
            border: 1px solid #d1d5db;
            background-color: #f9fafb;
        }

        .block-title {
            margin: 0 0 6px 0;
            font-size: 20px;
            font-weight: 700;
            color: #111827;
        }

        .block-text {
            margin: 0 0 6px 0;
            font-size: 14px;
            line-height: 20px;
            color: #4b5563;
        }

        .block-card {
            margin: 4px 0;
            padding: 6px 8px;
            border: 1px solid #e5e7eb;
            border-radius: 4px;
            background-color: #ffffff;
            font-size: 13px;
            line-height: 18px;
        }

        /* ----------------------------------------------------------- flex */
        .flex-root {
            display: flex;
            flex-direction: column;
        }

        .flex-header {
            display: flex;
            flex-direction: row;
            align-items: center;
            justify-content: space-between;
            padding: 12px 16px;
            background-color: #111827;
            color: #ffffff;
        }

        .flex-brand {
            font-size: 20px;
            font-weight: 700;
        }

        .flex-nav {
            display: flex;
            flex-direction: row;
        }

        .flex-link {
            margin-left: 12px;
            color: #d1d5db;
        }

        .flex-main {
            display: flex;
            flex-direction: row;
            flex-wrap: wrap;
            align-items: flex-start;
            padding: 12px;
        }

        .flex-card {
            display: flex;
            flex-direction: column;
            width: 180px;
            min-height: 90px;
            margin: 6px;
            padding: 10px;
            border: 1px solid #d1d5db;
            border-radius: 6px;
            background-color: #f3f4f6;
        }

        .flex-card.grow {
            flex-grow: 1;
        }

        .flex-title {
            margin-bottom: 4px;
            font-weight: 600;
        }

        .flex-body {
            margin: 0;
            font-size: 13px;
            line-height: 18px;
            color: #6b7280;
        }

        .flex-footer {
            padding: 10px 16px;
            background-color: #e5e7eb;
            color: #374151;
        }

        /* ----------------------------------------------------------- grid */
        .grid-root {
            display: grid;
            grid-template-columns: repeat(8, 1fr);
            grid-auto-rows: 60px;
            gap: 6px;
            padding: 8px;
        }

        .grid-item {
            padding: 4px;
            border: 1px solid #cbd5e1;
            border-radius: 4px;
            background-color: #e2e8f0;
            font-size: 12px;
            line-height: 16px;
        }

        .grid-item.wide {
            grid-column: span 2;
        }

        .grid-item.tall {
            grid-row: span 2;
        }

        .grid-item.hero {
            grid-column: span 4;
            grid-row: span 2;
            background-color: #bfdbfe;
        }

        /* ---------------------------------------------------------- paint */
        .paint-root {
            display: flex;
            flex-direction: row;
            flex-wrap: wrap;
            padding: 8px;
            background-color: #0f172a;
        }

        .paint-card {
            width: 160px;
            height: 110px;
            margin: 5px;
            padding: 8px;
            border: 2px solid #1d4ed8;
            border-radius: 8px;
            background-color: #2563eb;
            color: #eff6ff;
            font-size: 13px;
            line-height: 18px;
        }

        .paint-card.shadow {
            box-shadow: 0 3px 8px rgba(0, 0, 0, 0.45);
        }

        .paint-card.gradient {
            background-image: linear-gradient(135deg, #0ea5e9, #6366f1);
        }

        .paint-card.fade {
            opacity: 0.85;
        }

        .paint-card.shift {
            transform: translate(2px, 3px);
        }

        .paint-card.blur {
            filter: blur(1px);
        }

        .paint-card.clipped {
            overflow: hidden;
        }

        .paint-card.outline {
            outline: 2px solid #f59e0b;
        }

        .paint-title {
            font-weight: 700;
        }

        .paint-body {
            margin: 0;
            font-size: 12px;
            line-height: 17px;
        }

        /* ---------------------------------------------------------- mixed */
        .mixed-root {
            display: block;
            background-color: #f8fafc;
        }

        .mixed-header {
            position: fixed;
            top: 0;
            left: 0;
            right: 0;
            height: 56px;
            padding: 0 16px;
            background-color: #0f172a;
            color: #ffffff;
            font-size: 20px;
            font-weight: 700;
        }

        .mixed-shell {
            display: flex;
            flex-direction: row;
            padding-top: 56px;
        }

        .mixed-sidebar {
            width: 200px;
            padding: 12px;
            border-right: 1px solid #e2e8f0;
            background-color: #ffffff;
        }

        .sidebar-item {
            margin-bottom: 8px;
            padding: 4px 6px;
            border-radius: 4px;
            background-color: #f1f5f9;
            font-size: 13px;
        }

        .mixed-main {
            flex-grow: 1;
            height: 640px;
            padding: 16px;
            overflow: auto;
        }

        .mixed-card {
            margin-bottom: 10px;
            padding: 10px;
            border: 1px solid #e2e8f0;
            border-radius: 6px;
            background-color: #ffffff;
        }

        .mixed-title {
            margin: 0 0 4px 0;
            font-size: 18px;
            font-weight: 600;
        }

        .mixed-text {
            margin: 0;
            font-size: 14px;
            line-height: 20px;
            color: #475569;
        }

        .mixed-footer {
            padding: 12px 16px;
            background-color: #e2e8f0;
            color: #475569;
        }
        """;
}
