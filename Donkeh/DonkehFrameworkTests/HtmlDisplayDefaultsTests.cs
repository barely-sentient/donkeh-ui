using Donkeh.LayoutLab;
using Donkeh.Testing;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Locks in the <c>display</c> value Chrome gives every HTML element. Unknown
/// tags are inline, just as in a browser, and the metadata carriers generate no
/// box at all.
/// </summary>
public sealed class HtmlDisplayDefaultsTests
{
    public static TheoryData<string, Display> VisibleElements => new()
    {
        { "html", Display.Block },
        { "body", Display.Block },
        { "div", Display.Block },
        { "p", Display.Block },
        { "section", Display.Block },
        { "article", Display.Block },
        { "aside", Display.Block },
        { "header", Display.Block },
        { "footer", Display.Block },
        { "nav", Display.Block },
        { "main", Display.Block },
        { "search", Display.Block },
        { "hgroup", Display.Block },
        { "figure", Display.Block },
        { "figcaption", Display.Block },
        { "blockquote", Display.Block },
        { "address", Display.Block },
        { "dl", Display.Block },
        { "dt", Display.Block },
        { "dd", Display.Block },
        { "ul", Display.Block },
        { "ol", Display.Block },
        { "menu", Display.Block },
        { "dir", Display.Block },
        { "li", Display.ListItem },
        { "form", Display.Block },
        { "fieldset", Display.Block },
        { "legend", Display.Block },
        { "details", Display.Block },
        { "summary", Display.Block },
        { "center", Display.Block },
        { "hr", Display.Block },
        { "pre", Display.Block },
        { "plaintext", Display.Block },
        { "listing", Display.Block },
        { "xmp", Display.Block },
        { "br", Display.Block },
        { "h1", Display.Block },
        { "h2", Display.Block },
        { "h3", Display.Block },
        { "h4", Display.Block },
        { "h5", Display.Block },
        { "h6", Display.Block },

        { "span", Display.Inline },
        { "a", Display.Inline },
        { "strong", Display.Inline },
        { "b", Display.Inline },
        { "em", Display.Inline },
        { "i", Display.Inline },
        { "u", Display.Inline },
        { "s", Display.Inline },
        { "small", Display.Inline },
        { "sub", Display.Inline },
        { "sup", Display.Inline },
        { "code", Display.Inline },
        { "kbd", Display.Inline },
        { "samp", Display.Inline },
        { "var", Display.Inline },
        { "mark", Display.Inline },
        { "abbr", Display.Inline },
        { "acronym", Display.Inline },
        { "cite", Display.Inline },
        { "q", Display.Inline },
        { "time", Display.Inline },
        { "label", Display.Inline },
        { "output", Display.Inline },
        { "ruby", Display.Inline },
        { "rt", Display.Inline },
        { "bdi", Display.Inline },
        { "bdo", Display.Inline },
        { "big", Display.Inline },
        { "strike", Display.Inline },
        { "del", Display.Inline },
        { "dfn", Display.Inline },
        { "tt", Display.Inline },
        { "nobr", Display.Inline },
        { "data", Display.Inline },
        { "ins", Display.Inline },
        { "img", Display.Inline },
        { "canvas", Display.Inline },
        { "video", Display.Inline },
        { "iframe", Display.Inline },
        { "object", Display.Inline },
        { "embed", Display.Inline },
        { "svg", Display.Inline },

        { "input", Display.InlineBlock },
        { "textarea", Display.InlineBlock },
        { "select", Display.InlineBlock },
        { "button", Display.InlineBlock },
        { "progress", Display.InlineBlock },
        { "meter", Display.InlineBlock },
        { "marquee", Display.InlineBlock },

        { "table", Display.Table },
        { "caption", Display.TableCaption },
        { "col", Display.TableColumn },
        { "colgroup", Display.TableColumnGroup },
        { "thead", Display.TableHeaderGroup },
        { "tbody", Display.TableRowGroup },
        { "tfoot", Display.TableFooterGroup },
        { "tr", Display.TableRow },
        { "td", Display.TableCell },
        { "th", Display.TableCell },

        { "slot", Display.Contents }
    };

    public static TheoryData<string> HiddenElements => new()
    {
        "head", "title", "meta", "link", "style", "script", "template",
        "noscript", "datalist", "param", "source", "track", "area", "rp",
        "audio", "dialog"
    };

    [Theory]
    [MemberData(nameof(VisibleElements))]
    public void ElementUsesTheChromeDisplayValue(string tag, Display expected)
    {
        var element = RenderedHtml.TextElement(tag);

        Assert.NotNull(element.Style);
        Assert.Equal(expected, element.Style!.Display);
    }

    [Theory]
    [MemberData(nameof(HiddenElements))]
    public void NonRenderedElementGeneratesNoBox(string tag)
    {
        var element = RenderedHtml.TextElement(tag);

        Assert.Null(element.Style);
        Assert.False(element.IsVisible);
    }

    [Fact]
    public void UnknownCustomElementIsInline()
    {
        var element = RenderedHtml.TextElement("my-widget");

        Assert.Equal(Display.Inline, element.Style!.Display);
    }

    [Fact]
    public void HiddenInputGeneratesNoBox()
    {
        var element = RenderedHtml.Element("input", new Donkeh.UILab.Props { Type = "hidden" });

        Assert.Null(element.Style);
    }

    [Fact]
    public void DialogWithoutOpenAttributeGeneratesNoBox()
    {
        Assert.Null(RenderedHtml.Element("dialog").Style);

        var open = RenderedHtml.Element("dialog", new Donkeh.UILab.Props
        {
            Attributes = new Dictionary<string, string> { ["open"] = "open" }
        });

        Assert.Equal(Display.Block, open.Style!.Display);
    }

    [Fact]
    public void AudioWithoutControlsGeneratesNoBoxButWithControlsIsInline()
    {
        Assert.Null(RenderedHtml.Element("audio").Style);

        var audio = RenderedHtml.Element("audio", new Donkeh.UILab.Props
        {
            Controls = true
        });

        Assert.Equal(Display.Inline, audio.Style!.Display);
        Assert.Equal(300, audio.Bounds.Width, 3);
        Assert.Equal(54, audio.Bounds.Height, 3);
    }

    [Fact]
    public void HiddenAttributeOverridesDisplay()
    {
        var element = RenderedHtml.Element("div", new Donkeh.UILab.Props
        {
            Attributes = new Dictionary<string, string> { ["hidden"] = string.Empty }
        }, Donkeh.UILab._.Text("Hidden"));

        Assert.Null(element.Style);
        Assert.False(element.IsVisible);
    }
}
