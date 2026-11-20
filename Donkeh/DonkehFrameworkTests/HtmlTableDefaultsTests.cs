using Donkeh.LayoutLab;
using Donkeh.Testing;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Table defaults: Chrome's separate-border model, one-pixel untabled cell
/// padding (presentational attributes add the familiar 1 px cell padding in a
/// real table), bold centred headers and centred captions.
/// </summary>
public sealed class HtmlTableDefaultsTests
{
    [Fact]
    public void TableUsesSeparateBordersAndTwoPixelSpacing()
    {
        var table = RenderedHtml.TextElement("table");
        var style = table.Style!;

        Assert.Equal(Display.Table, style.Display);
        Assert.Equal(BoxSizing.BorderBox, style.BoxSizing);
        Assert.Equal(BorderCollapse.Separate, style.BorderCollapse);
        Assert.Equal(2, style.BorderSpacing.Resolve(new LengthContext(fontSize: 16)), 3);
        Assert.Equal(0, table.Box!.Margin.Top, 3);
    }

    [Fact]
    public void CellsUseOnePixelPaddingAndHeadersAreBoldAndCentred()
    {
        var page = RenderedHtml.Page(
            H("table", null,
                H("caption", null, Text("Caption")),
                H("thead", null,
                    H("tr", null,
                        H("th", null, Text("Heading")))),
                H("tbody", null,
                    H("tr", null,
                        H("td", null, Text("Cell"))))));

        var th = page.Find("th");
        var td = page.Find("td");
        var caption = page.Find("caption");

        Assert.Equal(1, th.Box!.Padding.Top, 3);
        Assert.Equal(1, th.Box!.Padding.Left, 3);
        Assert.Equal(700, th.Style!.FontWeight);
        Assert.Equal(TextAlign.Center, th.Style!.TextAlign);

        Assert.Equal(1, td.Box!.Padding.Top, 3);
        Assert.Equal(400, td.Style!.FontWeight);
        Assert.Equal(TextAlign.Start, td.Style!.TextAlign);
        Assert.True(td.Bounds.Width > 0);

        Assert.Equal(Display.TableCaption, caption.Style!.Display);
        Assert.Equal(TextAlign.Center, caption.Style!.TextAlign);
    }

    [Fact]
    public void TableSectionsAndRowsUseTheirChromeDisplays()
    {
        var page = RenderedHtml.Page(
            H("table", null,
                H("colgroup", null, H("col", null)),
                H("thead", null, H("tr", null, H("th", null, Text("H")))),
                H("tbody", null, H("tr", null, H("td", null, Text("B")))),
                H("tfoot", null, H("tr", null, H("td", null, Text("F"))))));

        Assert.Equal(Display.TableColumnGroup, page.Find("colgroup").Style!.Display);
        Assert.Equal(Display.TableColumn, page.Find("col").Style!.Display);
        Assert.Equal(Display.TableHeaderGroup, page.Find("thead").Style!.Display);
        Assert.Equal(Display.TableRowGroup, page.Find("tbody").Style!.Display);
        Assert.Equal(Display.TableFooterGroup, page.Find("tfoot").Style!.Display);
        Assert.Equal(Display.TableRow, page.Find("tr").Style!.Display);
    }
}
