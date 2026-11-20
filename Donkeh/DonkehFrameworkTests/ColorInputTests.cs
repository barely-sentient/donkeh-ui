using Donkeh.LayoutLab;
using Donkeh.UILab;
using Donkeh.Windowing;
using static Donkeh.UILab._;

namespace Donkeh.FrameworkTests;

/// <summary>
/// Regression coverage for colour inputs. The native chooser itself is modal and
/// platform-owned, so these tests lock the value conversion it depends on and the
/// DOM contract around the control.
/// </summary>
public sealed class ColorInputTests
{
    [Theory]
    [InlineData("#0b57d0", 0x00D0570B)]
    [InlineData("#ffffff", 0x00FFFFFF)]
    [InlineData("#000000", 0x00000000)]
    [InlineData("red", 0x000000FF)]
    [InlineData("rgb(11, 87, 208)", 0x00D0570B)]
    [InlineData("not-a-colour", 0x00000000)]
    public void CssColoursConvertToWin32ColorRef(string value, uint expected)
    {
        Assert.Equal(expected, ColorDialog.ToColorRef(value));
    }

    [Theory]
    [InlineData(0x00D0570B, "#0b57d0")]
    [InlineData(0x00FFFFFF, "#ffffff")]
    [InlineData(0x00000000, "#000000")]
    [InlineData(0x000000FF, "#ff0000")]
    public void ColorRefFormatsAsLowercaseCssHex(uint colorRef, string expected)
    {
        Assert.Equal(expected, ColorDialog.ToCssHex(colorRef));
    }

    [Theory]
    [InlineData("#0b57d0")]
    [InlineData("#ffffff")]
    [InlineData("#123456")]
    [InlineData("#abcdef")]
    public void ColorRefRoundTripsThroughCss(string css)
    {
        Assert.Equal(css, ColorDialog.ToCssHex(ColorDialog.ToColorRef(css)));
    }

    [Fact]
    public void AcceptingAColourWritesTheNormalisedValueAttribute()
    {
        var element = H("input", new Props { Type = "color", Value = "#000000" });

        var hex = ColorDialog.Apply(element, 0x00D0570B);

        Assert.Equal("#0b57d0", hex);
        Assert.True(element.TryGetAttribute("value", out var written));
        Assert.Equal("#0b57d0", written);
    }

    [Fact]
    public void ColorInputPaintsASwatchButContributesNoText()
    {
        var input = RenderedHtml.Element("input", new Props
        {
            Type = "color",
            Value = "#0b57d0"
        });

        Assert.Equal(Display.InlineBlock, input.Style!.Display);
        Assert.Null(input.Box!.Text);
        Assert.Equal("#0b57d0", input.Value);
    }
}
