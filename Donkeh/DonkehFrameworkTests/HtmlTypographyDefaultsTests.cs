using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Typography defaults: document font, heading scale, semantic weight and
/// style, code faces and the text decorations Chrome applies by default.
/// </summary>
public sealed class HtmlTypographyDefaultsTests
{
    private static float FontSize(TestElement element) =>
        element.Style!.ResolveFontSize(new LengthContext(fontSize: 16, rootFontSize: 16));

    [Fact]
    public void DocumentUsesChromesStandardFontSettings()
    {
        var page = RenderedHtml.Page();

        var html = page.Find("html").Style!;
        var body = page.Find("body").Style!;

        Assert.Contains("Times New Roman", html.FontFamily);
        Assert.Equal(16, FontSize(page.Find("html")), 3);
        Assert.Equal(CssColor.Black, html.Color);
        Assert.Equal(CssColor.Transparent, html.BackgroundColor);

        Assert.Contains("Times New Roman", body.FontFamily);
        Assert.Equal(16, FontSize(page.Find("body")), 3);
        Assert.Equal(CssColor.Transparent, body.BackgroundColor);
    }

    [Theory]
    [InlineData("h1", 32f, 0.67f)]
    [InlineData("h2", 24f, 0.83f)]
    [InlineData("h3", 18.72f, 1f)]
    [InlineData("h4", 16f, 1.33f)]
    [InlineData("h5", 13.28f, 1.67f)]
    [InlineData("h6", 10.72f, 2.33f)]
    public void HeadingsUseChromesScaleAndMargins(string tag, float size, float marginEm)
    {
        var heading = RenderedHtml.TextElement(tag);
        var style = heading.Style!;
        var box = heading.Box!;

        Assert.Equal(Display.Block, style.Display);
        Assert.Equal(size, FontSize(heading), 2);
        Assert.Equal(700, style.FontWeight);
        Assert.Equal(size * marginEm, box.Margin.Top, 2);
        Assert.Equal(size * marginEm, box.Margin.Bottom, 2);
        Assert.Equal(0, box.Margin.Left, 3);
        Assert.Equal(0, box.Margin.Right, 3);
    }

    [Fact]
    public void ParagraphsUseOneEmVerticalMargins()
    {
        var paragraph = RenderedHtml.TextElement("p");
        var box = paragraph.Box!;

        Assert.Equal(16, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
        Assert.Equal(0, box.Margin.Left, 3);
        Assert.Equal(0, box.Margin.Right, 3);
        Assert.Equal(16, FontSize(paragraph), 3);
    }

    [Theory]
    [InlineData("strong", 700, false)]
    [InlineData("b", 700, false)]
    [InlineData("em", 400, true)]
    [InlineData("i", 400, true)]
    [InlineData("cite", 400, true)]
    [InlineData("var", 400, true)]
    [InlineData("dfn", 400, true)]
    [InlineData("address", 400, true)]
    public void SemanticWeightAndStyleMatchChrome(string tag, int weight, bool italic)
    {
        var element = RenderedHtml.TextElement(tag);

        Assert.Equal(weight, element.Style!.FontWeight);
        Assert.Equal(italic, element.Style!.Italic);
    }

    [Theory]
    [InlineData("small", 13.33f)]
    [InlineData("sub", 13.33f)]
    [InlineData("sup", 13.33f)]
    public void SmallerTextElementsUseEightyThreePercent(string tag, float size)
    {
        Assert.Equal(size, FontSize(RenderedHtml.TextElement(tag)), 2);
    }

    [Theory]
    [InlineData("code")]
    [InlineData("kbd")]
    [InlineData("samp")]
    [InlineData("tt")]
    [InlineData("pre")]
    public void CodeFacesUseTheThirteenPixelMonospaceDefault(string tag)
    {
        var element = RenderedHtml.TextElement(tag);

        Assert.Equal("monospace", element.Style!.FontFamily);
        Assert.Equal(13, FontSize(element), 3);
    }

    [Fact]
    public void PrePreservesWhitespaceAndUsesChromesThirteenPixelMargins()
    {
        var pre = RenderedHtml.TextElement("pre");

        Assert.Equal(WhiteSpace.Pre, pre.Style!.WhiteSpace);
        Assert.Equal(13, pre.Box!.Margin.Top, 3);
        Assert.Equal(13, pre.Box!.Margin.Bottom, 3);
    }

    [Theory]
    [InlineData("s", TextDecoration.LineThrough)]
    [InlineData("strike", TextDecoration.LineThrough)]
    [InlineData("del", TextDecoration.LineThrough)]
    [InlineData("u", TextDecoration.Underline)]
    [InlineData("ins", TextDecoration.Underline)]
    public void TextDecorationDefaultsMatchChrome(string tag, TextDecoration expected)
    {
        Assert.Equal(expected, RenderedHtml.TextElement(tag).Style!.TextDecoration);
    }

    [Fact]
    public void MarkUsesTheYellowHighlight()
    {
        var mark = RenderedHtml.TextElement("mark").Style!;

        Assert.Equal(0xFFFFFF00u, mark.BackgroundColor);
        Assert.Equal(CssColor.Black, mark.Color);
    }

    [Fact]
    public void AbbreviationWithTitleIsUnderlined()
    {
        var abbr = RenderedHtml.Element("abbr", new Props
        {
            Title = "HyperText Markup Language"
        }, Text("HTML"));

        Assert.Equal(TextDecoration.Underline, abbr.Style!.TextDecoration);
    }

    [Fact]
    public void AnchorsWithHrefUseChromesLinkColourAndDecoration()
    {
        var link = RenderedHtml.Element("a", new Props { Href = "https://example.com" }, Text("Link"));
        var style = link.Style!;

        Assert.Equal(0xFF0000EEu, style.Color);
        Assert.Equal(TextDecoration.Underline, style.TextDecoration);
        Assert.Equal(Cursor.Pointer, style.Cursor);

        var anchorWithoutHref = RenderedHtml.TextElement("a");
        Assert.Equal(CssColor.Black, anchorWithoutHref.Style!.Color);
        Assert.Equal(TextDecoration.None, anchorWithoutHref.Style!.TextDecoration);
    }

    [Fact]
    public void RubyAnnotationIsHalfSizeOnlyInsideRuby()
    {
        var ruby = RenderedHtml.Element("ruby", null,
            Text("漢"),
            H("rt", null, Text("kan")));

        var rt = ruby.Find("rt");

        Assert.Equal(8, FontSize(rt), 2);
    }

    [Theory]
    [InlineData("center")]
    [InlineData("th")]
    [InlineData("caption")]
    public void CentredElementsUseChromesCentreAlignment(string tag)
    {
        Assert.Equal(TextAlign.Center, RenderedHtml.TextElement(tag).Style!.TextAlign);
    }
}
