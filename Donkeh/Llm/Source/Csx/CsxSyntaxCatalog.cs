namespace Donkeh.Llm.Csx;

/// <summary>One discoverable CSX language topic.</summary>
public sealed class CsxSyntaxTopic
{
    /// <summary>Stable topic key, for example <c>components</c>.</summary>
    public required string Category { get; init; }

    /// <summary>Short title.</summary>
    public required string Title { get; init; }

    /// <summary>One-sentence summary.</summary>
    public required string Summary { get; init; }

    /// <summary>Syntax forms that are actually accepted by the CSX transformer.</summary>
    public IReadOnlyList<string> Syntax { get; init; } = [];

    /// <summary>Minimal examples.</summary>
    public IReadOnlyList<string> Examples { get; init; } = [];

    /// <summary>Behaviour notes and limitations.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>CSX syntax knowledge derived from the shipped CSX parser and transformer.</summary>
public static class CsxSyntaxCatalog
{
    private static readonly List<CsxSyntaxTopic> Topics =
    [
        new()
        {
            Category = "overview",
            Title = "CSX files",
            Summary = "A .cx file is C# plus JSX-style elements. The transformer rewrites each element into ordinary method calls, then the normal C# compiler builds it.",
            Syntax = ["<tag attribute=\"value\">children</tag>", "<Component property={expression} />", "<>fragment children</>"],
            Examples =
            [
                "using Donkeh.UILab;\npublic sealed class App : Component\n{\n    public override Node Render() =>\n        <div className=\"app\">Hello</div>;\n}"
            ],
            Notes =
            [
                "The file extension is .cx. The task description also uses .csx as a filename; both are accepted by the CLI file commands.",
                "csx.json in the project root configures the generated method names and output directory.",
                "Generated .generated.cs files are ordinary C# and are compiled by the normal SDK glob."
            ]
        },
        new()
        {
            Category = "components",
            Title = "Component declarations",
            Summary = "Declare components as ordinary C# classes deriving from Component or Component<TProps>; render with a CSX expression.",
            Syntax =
            [
                "public sealed class Name : Component { public override Node Render() => <div />; }",
                "public sealed class Name : Component<NameProps> { public override Node Render() => ...; }",
                "public sealed class NameProps : Props { public string Title { get; init; } = \"\"; }"
            ],
            Examples =
            [
                "public sealed class CardProps : Props\n{\n    public string Title { get; init; } = \"\";\n}\n\npublic sealed class Card : Component<CardProps>\n{\n    public override Node Render() =>\n        <section className=\"card\"><h2>{Props.Title}</h2></section>;\n}"
            ],
            Notes =
            [
                "Components need a public parameterless constructor.",
                "The renderer creates and owns component instances; do not instantiate them yourself.",
                "Component classes are discovered through the normal C# type system, so they can live in any referenced package."
            ]
        },
        new()
        {
            Category = "elements",
            Title = "HTML elements",
            Summary = "Lowercase tags become Donkeh.UILab HTML helper calls. Uppercase tags become component calls.",
            Syntax = ["<div className=\"app\" />", "<button disabled>Save</button>", "<Header />"],
            Examples =
            [
                "<main className=\"page\">\n    <h1>Title</h1>\n    <p>Body</p>\n</main>"
            ],
            Notes =
            [
                "Tag names are case-sensitive in meaning: lowercase is an HTML element, uppercase is a component.",
                "Always write HTML element tags in lowercase (div, span, button); component tags use PascalCase.",
                "Component search can list every built-in element and component."
            ]
        },
        new()
        {
            Category = "attributes",
            Title = "Attributes",
            Summary = "Attributes map to properties on Props or the component's props class. String literals use quotes; C# expressions use braces.",
            Syntax =
            [
                "attribute=\"literal text\"",
                "attribute={expression}",
                "booleanAttribute",
                "className=\"one two\""
            ],
            Examples =
            [
                "<button className=\"primary\" disabled onClick={Save}>Save</button>\n<Card title={Props.Title} count={items.Count} />"
            ],
            Notes =
            [
                "A valueless attribute emits true, matching JSX and HTML boolean attributes.",
                "Props defines camelCase aliases for its common HTML attributes, so className, id, href and disabled compile directly.",
                "Unknown attributes on a custom component props class are compile errors; inspect the component's properties before using them."
            ]
        },
        new()
        {
            Category = "expressions",
            Title = "Expressions",
            Summary = "Brace expressions contain ordinary C# expressions and are emitted into the generated C# unchanged.",
            Syntax = ["{expression}", "{Props.Value}", "{items.Select(item => <Row item={item} />)}"],
            Examples =
            [
                "<span>{Props.Count}</span>\n<div className={Props.Active ? \"active\" : \"idle\"} />"
            ],
            Notes =
            [
                "Braces also delimit attribute expressions; nested braces are balanced by the CSX parser.",
                "Only valid C# expressions work; statement blocks belong in methods or local functions, not directly in markup."
            ]
        },
        new()
        {
            Category = "children",
            Title = "Child content",
            Summary = "Nested elements and text become child node arguments. Components receive children through PropsWithChildren or a Children property.",
            Syntax = ["<div><span>Text</span></div>", "<Card><p>Body</p></Card>"],
            Examples =
            [
                "<section className=\"card\">\n    <h2>{Props.Title}</h2>\n    <div className=\"body\">{Props.Children ?? []}</div>\n</section>"
            ],
            Notes =
            [
                "A component that accepts children should derive its props from PropsWithChildren or expose Node?[] Children.",
                "Fragments flatten their children into the parent's child list."
            ]
        },
        new()
        {
            Category = "fragments",
            Title = "Fragments",
            Summary = "Empty tags group children without adding an element to the tree.",
            Syntax = ["<>child one<child two></>"],
            Examples = ["<> <Header /> <main>{Props.Children ?? []}</main> </>"],
            Notes = ["Fragments are emitted through the configured fragment method, F by default."]
        },
        new()
        {
            Category = "generics",
            Title = "Component generics",
            Summary = "Component tags may carry C# generic arguments when the component type is generic.",
            Syntax = ["<Component<T> />", "<Component<T, U> property={value} />"],
            Examples = ["<List<Row> items={rows} />"],
            Notes = ["The transformer copies the generic argument list into the generated C<...> call."]
        },
        new()
        {
            Category = "events",
            Title = "Event handlers",
            Summary = "Event attributes are ordinary props whose type is Action, Action<TEvent> or another delegate. Use a method group or lambda in braces.",
            Syntax = ["onClick={Save}", "onClick={() => Save()}", "onChange={value => Search(value)}"],
            Examples =
            [
                "<input value={Props.Query} onChange={value => SetQuery(value)} />\n<button onClick={Props.OnSave}>Save</button>"
            ],
            Notes =
            [
                "Props exposes both simple handlers such as OnClick and full-payload handlers such as OnClickEvent.",
                "The full-payload handler wins when both are set on the same element.",
                "Event names are case-sensitive C# property names on custom props classes; className-style aliases exist only on Props."
            ]
        },
        new()
        {
            Category = "conditional",
            Title = "Conditional rendering",
            Summary = "There is no special @if directive. Use C# conditional expressions, switch expressions or ordinary if statements.",
            Syntax = ["{condition ? <A /> : <B />}", "{condition ? <A /> : null}", "if (condition) { return <A />; }"],
            Examples =
            [
                "public override Node Render() =>\n    Props.Loading\n        ? <p>Loading</p>\n        : <Results items={Props.Items} />;"
            ],
            Notes =
            [
                "C# `&&` with Node operands is not valid; prefer a conditional expression or an if statement.",
                "Null children are removed by the renderer, so a conditional expression may return null."
            ]
        },
        new()
        {
            Category = "iteration",
            Title = "Iteration",
            Summary = "There is no special loop directive. Use LINQ or the UILab Each helper inside an expression or C# statement.",
            Syntax = ["{items.Select(item => <Row item={item} />)}", "{Each(items, item => <Row item={item} />)}"],
            Examples =
            [
                "<ul>{Props.Items.Select(item => <li key={item.Id}>{item.Name}</li>)}</ul>"
            ],
            Notes =
            [
                "Supply key when a list can reorder so component state and layout are reused.",
                "LINQ produces IEnumerable<Node>; the renderer normalises child sequences."
            ]
        },
        new()
        {
            Category = "directives",
            Title = "Directives and directives-like syntax",
            Summary = "CSX does not add directives. There is no @if, @foreach, v-if, v-for, :bind or special data-binding syntax.",
            Syntax = [],
            Examples = [],
            Notes =
            [
                "Data binding is ordinary C#: read Props and hook state in expressions, and call state setters from event handlers.",
                "Conditional rendering and iteration use normal C# as described in the conditional and iteration topics.",
                "csx.json is configuration, not an in-file directive."
            ]
        },
        new()
        {
            Category = "csharp",
            Title = "Supported C# expressions",
            Summary = "CSX wraps C# rather than replacing it. Any expression the C# compiler accepts in a method body is available inside braces.",
            Syntax =
            [
                "{Math.Max(Props.Min, Props.Value)}",
                "{Props.Items.Where(item => item.Visible).Count()}",
                "{nameof(Props.Title)}",
                "{new[] { \"a\", \"b\" }}"
            ],
            Examples = ["<span>{string.Join(\", \", Props.Tags)}</span>"],
            Notes =
            [
                "The transformer preserves C# tokens outside JSX elements.",
                "Generic method calls and relational operators are distinguished from tag starts by the CSX tokenizer.",
                "The generated file is normal C#, so IDE and compiler diagnostics apply to it after transformation."
            ]
        },
        new()
        {
            Category = "configuration",
            Title = "csx.json",
            Summary = "Project-level transformer configuration.",
            Syntax =
            [
                "{\n  \"FrameworkHtmlClass\": \"_\",\n  \"FrameworkHtmlClassMethod\": \"H\",\n  \"FrameworkComponentMethod\": \"C\",\n  \"FrameworkFragmentMethod\": \"F\",\n  \"GeneratedFileOutput\": \"src/generated\"\n}"
            ],
            Examples = ["donkeh new project MyApp\ndonkeh run"],
            Notes =
            [
                "The defaults match Donkeh's UILab helper class.",
                "GeneratedFileOutput controls where transformed files are written.",
                "The CLI reads this file for project inspection and CSX compile output."
            ]
        }
    ];

    /// <summary>Returns all topics, or one category when requested.</summary>
    public static List<CsxSyntaxTopic> Get(string? category = null)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Topics;
        }

        var value = category.Trim().ToLowerInvariant();
        return Topics
            .Where(topic => topic.Category.Equals(value, StringComparison.OrdinalIgnoreCase)
                || topic.Title.Contains(value, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}
