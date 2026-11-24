using Donkeh.Data.Dom;
using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Unit coverage for the tag-name helpers that back DOM construction and
/// intrinsic sizing. The display table is checked against the rendered cascade
/// so the helper sets cannot drift from <see cref="HtmlDefaults.Css"/>.
/// </summary>
public sealed class HtmlDefaultsHelperTests
{
    public static TheoryData<string> Tags => new()
    {
        "html", "body", "div", "p", "h1", "h2", "h3", "h4", "h5", "h6",
        "section", "article", "aside", "header", "footer", "nav", "main",
        "search", "hgroup", "figure", "figcaption", "blockquote", "address",
        "dl", "dt", "dd", "ul", "ol", "menu", "dir", "li", "form", "fieldset",
        "legend", "details", "summary", "center", "hr", "pre", "plaintext",
        "listing", "xmp", "br",
        "span", "a", "strong", "b", "em", "i", "u", "s", "small", "sub",
        "sup", "code", "kbd", "samp", "var", "mark", "abbr", "acronym",
        "cite", "q", "time", "label", "output", "ruby", "rt", "bdi", "bdo",
        "big", "strike", "del", "dfn", "tt", "nobr", "data", "ins",
        "img", "canvas", "video", "iframe", "object", "embed", "svg", "webview",
        "input", "textarea", "select", "button", "progress", "meter", "marquee",
        "table", "caption", "col", "colgroup", "thead", "tbody", "tfoot",
        "tr", "td", "th", "slot", "my-widget"
    };

    [Theory]
    [MemberData(nameof(Tags))]
    public void DisplayForMatchesTheRenderedDefault(string tag)
    {
        var element = RenderedHtml.TextElement(tag);
        var expected = HtmlDefaults.DisplayFor(tag);

        Assert.NotNull(element.Style);
        Assert.Equal(expected, Describe(element.Style!.Display));
    }

    [Fact]
    public void HiddenTagsAreReportedAndSuppressed()
    {
        string[] hidden =
        [
            "area", "base", "basefont", "datalist", "head", "link", "meta",
            "noembed", "noframes", "noscript", "param", "rp", "script", "source",
            "style", "template", "title", "track"
        ];

        foreach (var tag in hidden)
        {
            Assert.True(HtmlDefaults.IsHidden(tag));
            Assert.True(HtmlDefaults.IsHidden(tag.ToUpperInvariant()));
            Assert.Null(RenderedHtml.TextElement(tag).Style);
        }

        Assert.False(HtmlDefaults.IsHidden(null));
        Assert.False(HtmlDefaults.IsHidden("div"));
    }

    [Fact]
    public void OnlyReplacedTagsReportAnIntrinsicSize()
    {
        string[] replaced = ["img", "canvas", "video", "audio", "iframe", "object", "embed", "webview"];

        foreach (var tag in replaced)
        {
            Assert.True(HtmlDefaults.IsReplaced(tag));
        }

        foreach (var tag in new[] { "input", "select", "textarea", "div", "span", null })
        {
            Assert.False(HtmlDefaults.IsReplaced(tag));
        }
    }

    [Fact]
    public void AttributeLengthReadsTheNumericPrefix()
    {
        var image = RenderedHtml.Element("img", new Props
        {
            Attributes = new Dictionary<string, string>
            {
                ["width"] = "320px",
                ["height"] = "-12.5"
            }
        });

        Assert.Equal(320, HtmlDefaults.AttributeLength(image.Element, "width", 0), 3);
        Assert.Equal(-12.5f, HtmlDefaults.AttributeLength(image.Element, "height", 0), 3);
        Assert.Equal(42, HtmlDefaults.AttributeLength(image.Element, "missing", 42), 3);
    }

    private static string Describe(Display display) => display switch
    {
        Display.None => "none",
        Display.Block => "block",
        Display.Inline => "inline",
        Display.InlineBlock => "inline-block",
        Display.ListItem => "list-item",
        Display.Table => "table",
        Display.InlineTable => "inline-table",
        Display.TableCaption => "table-caption",
        Display.TableColumn => "table-column",
        Display.TableColumnGroup => "table-column-group",
        Display.TableHeaderGroup => "table-header-group",
        Display.TableRowGroup => "table-row-group",
        Display.TableFooterGroup => "table-footer-group",
        Display.TableRow => "table-row",
        Display.TableCell => "table-cell",
        Display.Contents => "contents",
        Display.Flex => "flex",
        Display.InlineFlex => "inline-flex",
        Display.Grid => "grid",
        Display.InlineGrid => "inline-grid",
        Display.FlowRoot => "flow-root",
        _ => display.ToString().ToLowerInvariant()
    };
}
