using Donkeh.Data;
using Donkeh.Data.Dom;
using Engine = Donkeh.DataEngine.DataEngine;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Shared helpers for the LayoutLab specification tests. Stylesheets are always
/// compiled from real CSS text so the tests exercise the parser, the matcher,
/// the cascade and the layout engine together rather than hand-built internals.
/// </summary>
internal static class TestSupport
{
    public static DataTable Build(string css)
    {
        return new Engine(Condition.AlwaysTrueCompiler).Build(css);
    }

    public static IgniteSupportedDomNode Node(
        string tag = "div",
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

    public static LayoutOptions Options(
        float width = 1000,
        float height = 800,
        bool optimise = false)
    {
        return new LayoutOptions
        {
            ViewportWidth = width,
            ViewportHeight = height,
            Optimise = optimise
        };
    }

    public static LayoutResult Layout(
        DataTable table,
        IgniteSupportedDomNode root,
        float width = 1000,
        float height = 800,
        bool optimise = false)
    {
        return new LayoutEngine(table).Layout(root, Options(width, height, optimise));
    }

    public static LayoutResult Layout(string css, IgniteSupportedDomNode root,
        float width = 1000, float height = 800, bool optimise = false)
    {
        return Layout(Build(css), root, width, height, optimise);
    }

    public static LayoutResult Layout(DataTable table, IgniteSupportedDomNode root, LayoutOptions options)
    {
        return new LayoutEngine(table).Layout(root, options);
    }

    /// <summary>Deterministic measurer: every character is 10px wide.</summary>
    internal sealed class FixedMeasurer : ITextMeasurer
    {
        public static readonly FixedMeasurer Instance = new();

        public TextMetrics Measure(ReadOnlySpan<char> text, in TextStyle style)
        {
            return new TextMetrics(text.Length * 10, style.FontSize * 0.8f, style.FontSize * 0.2f);
        }

        public TextMetrics Measure(string text, in TextStyle style)
        {
            return Measure(text.AsSpan(), in style);
        }
    }

    public static LayoutBox Box(LayoutResult result, IgniteSupportedDomNode node)
    {
        Assert.True(result.TryGetBox(node, out var box), "The layout result did not contain the node.");
        return box;
    }

    public static ComputedStyle Style(string declarations)
    {
        return StyleFor(declarations, out _);
    }

    public static ComputedStyle StyleFor(string declarations, out LayoutResult result)
    {
        var table = Build("#root { " + declarations + " }");
        var root = Node("div", "root");
        result = Layout(table, root);
        return Box(result, root).Style;
    }

    /// <summary>
    /// Resolves a node's style without laying it out. Useful for nodes that do
    /// not produce a box at all, such as <c>display: none</c> elements.
    /// </summary>
    public static ComputedStyle ResolveStyle(string css, IgniteSupportedDomNode node,
        ComputedStyle? parent = null)
    {
        var table = Build(css);
        var resolver = new StyleResolver(table, new StyleValueCache(), Options());
        var context = new RendererContext();
        var matches = new MatchedRule[16];
        var count = table.MatchRules(node, context, matches);

        if (count > matches.Length)
        {
            Array.Resize(ref matches, count);
            count = table.MatchRules(node, context, matches);
        }

        return resolver.Resolve(node, parent, matches.AsSpan(0, count));
    }

    public static ComputedStyle StyleOf(string css, string id = "root")
    {
        var table = Build(css);
        var root = Node("div", id);
        var result = Layout(table, root);
        return Box(result, root).Style;
    }

    public static List<Instruction> Instructions(LayoutResult result, InstructionOpCode opCode)
    {
        var matches = new List<Instruction>();

        foreach (var instruction in result.Opcodes)
        {
            if (instruction.OpCode == opCode)
            {
                matches.Add(instruction);
            }
        }

        return matches;
    }

    public static Instruction Single(LayoutResult result, InstructionOpCode opCode)
    {
        var matches = Instructions(result, opCode);
        Assert.Single(matches);
        return matches[0];
    }

    public static void AssertRect(Rect rect, float x, float y, float width, float height, int precision = 3)
    {
        Assert.Equal(x, rect.X, precision);
        Assert.Equal(y, rect.Y, precision);
        Assert.Equal(width, rect.Width, precision);
        Assert.Equal(height, rect.Height, precision);
    }
}
