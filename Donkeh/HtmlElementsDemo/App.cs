using Donkeh.Hosting;
using Donkeh.UILab;
using static Donkeh.UILab.HtmlHelper;
using static Donkeh.UILab._;

namespace Donkeh.HtmlElementsDemo;

/// <summary>
/// A plain HTML document rendered with the framework's Chrome user-agent
/// defaults. The app adds no author CSS: everything on screen is the same
/// stylesheet the unit tests lock in.
/// </summary>
[AppRoot(Width = 1200, Height = 900, Title = "HTML element defaults")]
public sealed class App : Component
{
    /// <inheritdoc/>
    public override Node Render()
    {
        return Html(new Props(),
            Head(new Props(),
                Meta(new Props()),
                Title(new Props(), Text("HTML element defaults"))),
            Body(new Props(),
                E("h1", "HTML element defaults"),
                P(new Props(),
                    Text("This page has no author CSS. Every element below is laid out with the framework's "),
                    Code(new Props(), Text("HtmlDefaults.Css")),
                    Text(" sheet, which mirrors Chrome's user-agent defaults.")),
                Hr(new Props()),

                Section("Block content",
                    E("div", "div — a generic block container"),
                    E("p", "p — a paragraph with the browser's one-em vertical margins."),
                    E("section", "section — a thematic grouping"),
                    E("article", "article — self-contained content"),
                    E("header", "header — introductory content"),
                    E("footer", "footer — footer content"),
                    E("nav", "nav — navigation content"),
                    E("main", "main — dominant content"),
                    E("aside", "aside — tangential content"),
                    E("address", "address — contact information, italic by default"),
                    E("blockquote", "blockquote — a quotation indented by forty pixels."),
                    E("center", "center — centred text"),
                    Figure(new Props(),
                        Text("figure — self-contained illustration"),
                        Figcaption(new Props(), Text("figcaption — a figure caption"))),
                    Pre(new Props(), Text("pre\n  preserves   whitespace\n  and uses the monospace face.")),
                    Hr(new Props())),

                Section("Headings",
                    E("h1", "h1 — 2em, bold"),
                    E("h2", "h2 — 1.5em, bold"),
                    E("h3", "h3 — 1.17em, bold"),
                    E("h4", "h4 — 1em, bold"),
                    E("h5", "h5 — 0.83em, bold"),
                    E("h6", "h6 — 0.67em, bold")),

                Section("Lists",
                    Ul(new Props(),
                        E("li", "Unordered list item"),
                        E("li", "Another item"),
                        E("li", "A third item")),
                    Ol(new Props(),
                        E("li", "Ordered list item"),
                        E("li", "Another item")),
                    Dl(new Props(),
                        E("dt", "Definition term"),
                        E("dd", "Definition description, indented by forty pixels."),
                        E("dt", "Second term"),
                        E("dd", "Second description."))),

                Section("Inline text", InlineText()),

                Section("Tables",
                    Table(new Props(),
                        Caption(new Props(), Text("table caption")),
                        Thead(new Props(),
                            Tr(new Props(),
                                E("th", "thead th"),
                                E("th", "thead th"))),
                        Tbody(new Props(),
                            Tr(new Props(),
                                E("td", "tbody td"),
                                E("td", "tbody td")),
                            Tr(new Props(),
                                E("td", "tbody td"),
                                E("td", "tbody td"))),
                        Tfoot(new Props(),
                            Tr(new Props(),
                                E("td", "tfoot td"),
                                E("td", "tfoot td"))))),

                Section("Form controls", FormControls()),

                Section("Replaced and embedded content", ReplacedContent()),

                Section("Disclosure and dialogs",
                    Details(new Props(),
                        Summary(new Props(), Text("details > summary (closed)")),
                        P(new Props(), Text("Hidden until the details element is opened."))),
                    Details(new Props { Attributes = Flag("open") },
                        Summary(new Props(), Text("details > summary (open)")),
                        P(new Props(), Text("This content is visible because the details element is open."))),
                    Dialog(new Props { Attributes = Flag("open") },
                        P(new Props(), Text("dialog[open] — positioned like Chrome's non-modal dialog."))),
                    Hr(new Props()),
                    P(new Props(),
                        Text("List markers are not painted yet, so list items appear without bullets or numbers."),
                        Br(new Props()),
                        Text("That is the only intentional visual difference from Chrome on this page.")))));
    }

    private static Node InlineText() =>
        P(new Props(),
            Text("Plain text with "),
            E("span", "span"),
            Text(", "),
            E("strong", "strong"),
            Text(", "),
            E("b", "b"),
            Text(", "),
            E("em", "em"),
            Text(", "),
            E("i", "i"),
            Text(", "),
            E("u", "u"),
            Text(", "),
            E("s", "s"),
            Text(", "),
            E("small", "small"),
            Text(", x"),
            E("sub", "sub"),
            Text(", x"),
            E("sup", "sup"),
            Text(", "),
            E("code", "code"),
            Text(", "),
            E("kbd", "kbd"),
            Text(", "),
            E("samp", "samp"),
            Text(", "),
            E("var", "var"),
            Text(", "),
            E("mark", "mark"),
            Text(", "),
            Abbr(new Props { Title = "HyperText Markup Language" }, Text("abbr")),
            Text(", "),
            E("cite", "cite"),
            Text(", "),
            E("q", "q"),
            Text(", "),
            E("time", "time"),
            Text(", "),
            E("del", "del"),
            Text(", "),
            E("ins", "ins"),
            Text(", and an "),
            A(new Props { Href = "#" }, Text("anchor")),
            Text("."));

    private static Node FormControls() =>
        Form(new Props(),
            Fieldset(new Props(),
                Legend(new Props(), Text("fieldset and legend")),
                Field("Text input: ", Input(new Props
                {
                    Type = "text",
                    Value = "Text value",
                    Name = "text"
                })),
                Field("Password: ", Input(new Props
                {
                    Type = "password",
                    Value = "secret",
                    Name = "password"
                })),
                Field("Email: ", Input(new Props
                {
                    Type = "email",
                    Placeholder = "name@example.com",
                    Name = "email"
                })),
                P(new Props(),
                    Label(new Props(), Text("Checkbox: ")),
                    Input(new Props { Type = "checkbox", Checked = true }),
                    Label(new Props(), Text("  Radio: ")),
                    Input(new Props { Type = "radio", Checked = true })),
                Field("Range: ", Input(new Props { Type = "range", Name = "range" })),
                Field("Color: ", Input(new Props
                {
                    Type = "color",
                    Name = "color",
                    Value = "#0b57d0"
                })),
                Field("File: ", Input(new Props { Type = "file", Name = "file" })),
                Field("Select: ", Select(new Props { Name = "select" },
                    Option(new Props { Value = "one" }, Text("One")),
                    Option(new Props { Value = "two" }, Text("Two")),
                    Option(new Props { Value = "three" }, Text("Three")))),
                Field("Textarea: ", Textarea(new Props(), Text("Two rows of\ntext content."))),
                P(new Props(),
                    Button(new Props { Type = "button" }, Text("Button")),
                    Text(" "),
                    Input(new Props { Type = "submit", Value = "Submit" }),
                    Text(" "),
                    Input(new Props { Type = "reset", Value = "Reset" }),
                    Text(" "),
                    Button(new Props { Type = "button", Disabled = true }, Text("Disabled")),
                    Text(" "),
                    Output(new Props(), Text("output")),
                    Text(" "),
                    Progress(new Props { Attributes = Number("value", "60", "max", "100") }),
                    Text(" "),
                    Meter(new Props { Attributes = Number("value", "0.4", "max", "1") }))));

    private static Node ReplacedContent() =>
        P(new Props(),
            Img(new Props
            {
                Alt = "image placeholder",
                Attributes = new Dictionary<string, string>
                {
                    ["width"] = "120",
                    ["height"] = "80"
                }
            }),
            Text(" img, "),
            Canvas(new Props()),
            Text(" canvas, "),
            Video(new Props()),
            Text(" video, "),
            Audio(new Props { Controls = true }),
            Text(" audio, "),
            Iframe(new Props()),
            Text(" iframe, "),
            Object(new Props()),
            Text(" object, "),
            Embed(new Props()),
            Text(" embed and "),
            H("svg", new Props()),
            Text(" svg."));

    private static Node Field(string label, Node control) =>
        P(new Props(),
            Label(new Props(), Text(label)),
            control);

    private static Node Section(string title, params Node[] children) =>
        Div(new Props(),
            [H2(new Props(), Text(title)), .. children]);

    private static Node E(string tag, string text) =>
        H(tag, new Props(), Text(text));

    private static Dictionary<string, string> Flag(string name) =>
        new() { [name] = name };

    private static Dictionary<string, string> Number(
        string firstName,
        string firstValue,
        string secondName,
        string secondValue) =>
        new()
        {
            [firstName] = firstValue,
            [secondName] = secondValue
        };
}
