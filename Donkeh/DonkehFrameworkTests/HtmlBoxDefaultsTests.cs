using Donkeh.LayoutLab;
using Donkeh.Testing;
using Donkeh.UILab;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// The resolved margins, padding and borders Chrome gives flow content without
/// an author stylesheet. Values are asserted on the laid-out box, so percentage
/// and em declarations are checked in their resolved pixel form.
/// </summary>
public sealed class HtmlBoxDefaultsTests
{
    [Fact]
    public void BodyHasTheEightPixelBrowserMargin()
    {
        var body = RenderedHtml.Page().Find("body");
        var box = body.Box!;

        Assert.Equal(8, box.Margin.Top, 3);
        Assert.Equal(8, box.Margin.Right, 3);
        Assert.Equal(8, box.Margin.Bottom, 3);
        Assert.Equal(8, box.Margin.Left, 3);
        Assert.Equal(8, box.BorderBox.X, 3);
        Assert.Equal(8, box.BorderBox.Y, 3);
    }

    [Theory]
    [InlineData("div")]
    [InlineData("section")]
    [InlineData("article")]
    [InlineData("aside")]
    [InlineData("header")]
    [InlineData("footer")]
    [InlineData("nav")]
    [InlineData("main")]
    [InlineData("search")]
    [InlineData("hgroup")]
    [InlineData("figcaption")]
    [InlineData("address")]
    [InlineData("dt")]
    [InlineData("li")]
    [InlineData("legend")]
    [InlineData("details")]
    [InlineData("summary")]
    public void PlainFlowBlocksHaveNoDefaultSpacing(string tag)
    {
        var box = RenderedHtml.TextElement(tag).Box!;

        Assert.Equal(0, box.Margin.Top, 3);
        Assert.Equal(0, box.Margin.Right, 3);
        Assert.Equal(0, box.Margin.Bottom, 3);
        Assert.Equal(0, box.Margin.Left, 3);
        Assert.Equal(0, box.Padding.Top, 3);
        Assert.Equal(0, box.Border.Top, 3);
    }

    [Fact]
    public void BlockquoteUsesFortyPixelInlineMargins()
    {
        var box = RenderedHtml.TextElement("blockquote").Box!;

        Assert.Equal(16, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
        Assert.Equal(40, box.Margin.Left, 3);
        Assert.Equal(40, box.Margin.Right, 3);
    }

    [Fact]
    public void FigureUsesFortyPixelInlineMargins()
    {
        var box = RenderedHtml.TextElement("figure").Box!;

        Assert.Equal(16, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
        Assert.Equal(40, box.Margin.Left, 3);
        Assert.Equal(40, box.Margin.Right, 3);
    }

    [Theory]
    [InlineData("ul")]
    [InlineData("ol")]
    [InlineData("menu")]
    [InlineData("dir")]
    public void ListsUseOneEmMarginsAndFortyPixelIndent(string tag)
    {
        var box = RenderedHtml.TextElement(tag).Box!;

        Assert.Equal(16, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
        Assert.Equal(40, box.Padding.Left, 3);
        Assert.Equal(0, box.Padding.Right, 3);
    }

    [Fact]
    public void DefinitionListUsesOneEmMarginsAndIndentedDescription()
    {
        var dl = RenderedHtml.Element("dl", null,
            H("dt", null, Text("Term")),
            H("dd", null, Text("Description")));

        var box = dl.Box!;
        var dd = dl.Find("dd").Box!;

        Assert.Equal(16, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
        Assert.Equal(40, dd.Margin.Left, 3);
    }

    [Fact]
    public void FieldsetUsesChromesGrooveBorderAndPadding()
    {
        var fieldset = RenderedHtml.Element("fieldset", null,
            H("legend", null, Text("Legend")),
            Text("Content"));

        var box = fieldset.Box!;
        var style = fieldset.Style!;

        Assert.Equal(0, box.Margin.Top, 3);
        Assert.Equal(2, box.Margin.Left, 3);
        Assert.Equal(2, box.Margin.Right, 3);
        Assert.Equal(2, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Groove, style.BorderStyleTop);
        Assert.Equal(0xFFF0F0F0u, style.BorderColorTop);
        Assert.Equal(5.6f, box.Padding.Top, 2);
        Assert.Equal(12f, box.Padding.Left, 2);
        Assert.Equal(10f, box.Padding.Bottom, 2);
    }

    [Fact]
    public void LegendHasTwoPixelInlinePadding()
    {
        var legend = RenderedHtml.TextElement("legend").Box!;

        Assert.Equal(2, legend.Padding.Left, 3);
        Assert.Equal(2, legend.Padding.Right, 3);
        Assert.Equal(0, legend.Padding.Top, 3);
    }

    [Fact]
    public void FormKeepsChromesBottomMargin()
    {
        var box = RenderedHtml.TextElement("form").Box!;

        Assert.Equal(0, box.Margin.Top, 3);
        Assert.Equal(16, box.Margin.Bottom, 3);
    }

    [Fact]
    public void RuleUsesInsetBorderAndHalfEmMargins()
    {
        var hr = RenderedHtml.Element("hr");
        var box = hr.Box!;
        var style = hr.Style!;

        Assert.Equal(8, box.Margin.Top, 3);
        Assert.Equal(8, box.Margin.Bottom, 3);
        Assert.Equal(1, box.Border.Top, 3);
        Assert.Equal(BorderStyle.Inset, style.BorderStyleTop);
        Assert.Equal(0xFF808080u, style.BorderColorTop);
        Assert.Equal(2, hr.Bounds.Height, 2);
    }

    [Fact]
    public void InlineElementsHaveNoSpacingOrBorder()
    {
        foreach (var tag in new[] { "span", "strong", "em", "small", "mark", "code", "a", "q", "time" })
        {
            var box = RenderedHtml.TextElement(tag).Box!;

            Assert.Equal(0, box.Margin.Top, 3);
            Assert.Equal(0, box.Margin.Left, 3);
            Assert.Equal(0, box.Padding.Top, 3);
            Assert.Equal(0, box.Border.Top, 3);
        }
    }
}
