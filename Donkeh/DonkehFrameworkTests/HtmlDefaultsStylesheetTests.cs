using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Guards the user-agent sheet itself: every declaration it applies has to be
/// understood by the style resolver, and the helper tag tables have to stay in
/// sync with the cascade.
/// </summary>
public sealed class HtmlDefaultsStylesheetTests
{
    private static readonly string[] Tags =
    [
        "html", "body", "div", "p", "h1", "h2", "h3", "h4", "h5", "h6",
        "section", "article", "aside", "header", "footer", "nav", "main",
        "search", "hgroup", "figure", "figcaption", "blockquote", "address",
        "dl", "dt", "dd", "ul", "ol", "menu", "dir", "li", "form", "fieldset",
        "legend", "details", "summary", "center", "hr", "pre", "plaintext",
        "listing", "xmp", "br", "span", "a", "strong", "b", "em", "i", "u", "s",
        "small", "sub", "sup", "code", "kbd", "samp", "var", "mark", "abbr",
        "acronym", "cite", "q", "time", "label", "output", "ruby", "rt", "bdi",
        "bdo", "big", "strike", "del", "dfn", "tt", "nobr", "data", "ins",
        "img", "canvas", "video", "audio", "iframe", "object", "embed", "svg",
        "input", "textarea", "select", "button", "progress", "meter", "marquee",
        "table", "caption", "col", "colgroup", "thead", "tbody", "tfoot",
        "tr", "td", "th", "slot", "head", "title", "meta", "link", "style",
        "script", "template", "noscript", "datalist", "param", "source",
        "track", "area", "rp", "dialog"
    ];

    private static readonly string[] InputTypes =
    [
        "text", "password", "email", "search", "tel", "url", "checkbox",
        "radio", "range", "color", "file", "button", "submit", "reset", "hidden"
    ];

    [Fact]
    public void EveryDefaultDeclarationIsUnderstoodByTheResolver()
    {
        foreach (var tag in Tags)
        {
            var element = RenderedHtml.TextElement(tag);

            if (element.Style is { } style)
            {
                Assert.True(
                    style.UnsupportedProperties is null or { Count: 0 },
                    $"<{tag}> used unsupported properties: {string.Join(", ", style.UnsupportedProperties ?? [])}");
            }
        }

        foreach (var type in InputTypes)
        {
            var input = RenderedHtml.Element("input", new Props { Type = type });

            if (input.Style is { } style)
            {
                Assert.True(
                    style.UnsupportedProperties is null or { Count: 0 },
                    $"<input type=\"{type}\"> used unsupported properties: {string.Join(", ", style.UnsupportedProperties ?? [])}");
            }
        }
    }

    [Fact]
    public void SelectionColoursResolveForTextControls()
    {
        var input = RenderedHtml.Element("input", new Donkeh.UILab.Props { Value = "Sample" });
        var textarea = RenderedHtml.Element("textarea", null, Donkeh.UILab._.Text("Sample"));

        Assert.Equal(0x7F0075FFu, input.Style!.SelectionBackgroundColor);
        Assert.Equal(0x7F0075FFu, textarea.Style!.SelectionBackgroundColor);
    }

    [Fact]
    public void SheetStartsWithChromesDocumentDefaults()
    {
        var page = RenderedHtml.Page();
        var body = page.Find("body").Style!;

        Assert.Equal(16, body.FontSize.Resolve(new Donkeh.LayoutLab.LengthContext(fontSize: 16)), 3);
        Assert.Contains("Times New Roman", body.FontFamily);
        Assert.Equal(0xFF000000u, body.Color);
        Assert.Equal(Donkeh.LayoutLab.CssColor.Transparent, body.BackgroundColor);

        var text = RenderedHtml.TextElement("div");
        Assert.Equal(0xFF000000u, text.Style!.Color);
    }
}
