namespace Donkeh.LayoutLab.Tests;

public class CssColorTests
{
    [Fact]
    public void PackAndChannelAccessorsRoundTrip()
    {
        var color = CssColor.Pack(0x12, 0x34, 0x56, 0x78);

        Assert.Equal(0x78u, CssColor.Alpha(color));
        Assert.Equal(0x12u, CssColor.Red(color));
        Assert.Equal(0x34u, CssColor.Green(color));
        Assert.Equal(0x56u, CssColor.Blue(color));
        Assert.Equal(color, CssColor.Pack(CssColor.Red(color), CssColor.Green(color), CssColor.Blue(color), CssColor.Alpha(color)));
    }

    [Fact]
    public void ConstantsHaveTheExpectedPackedValues()
    {
        Assert.Equal(0x00000000u, CssColor.Transparent);
        Assert.Equal(0xFF000000u, CssColor.Black);
        Assert.Equal(0xFFFFFFFFu, CssColor.White);
    }

    [Fact]
    public void WithAlphaReplacesOnlyTheAlphaChannel()
    {
        var color = CssColor.WithAlpha(0xFF112233, 0x44);
        Assert.Equal(0x44112233u, color);
    }

    [Theory]
    [InlineData(255, 0.5f, 128)]
    [InlineData(255, 0f, 0)]
    [InlineData(255, 2f, 255)]
    [InlineData(10, 0.5f, 5)]
    public void MultiplyAlphaScalesAndClamps(byte alpha, float factor, byte expected)
    {
        var color = CssColor.WithAlpha(CssColor.White, alpha);
        Assert.Equal(expected, CssColor.Alpha(CssColor.MultiplyAlpha(color, factor)));
    }

    [Fact]
    public void BlendReturnsTheTopWhenFullyOpaque()
    {
        var top = CssColor.Pack(10, 20, 30);
        Assert.Equal(top, CssColor.Blend(top, CssColor.Black));
    }

    [Fact]
    public void BlendReturnsTheBottomWhenFullyTransparent()
    {
        Assert.Equal(CssColor.White, CssColor.Blend(CssColor.Transparent, CssColor.White));
    }

    [Fact]
    public void BlendCompositesTranslucentOverOpaque()
    {
        var top = CssColor.Pack(255, 0, 0, 128);
        var bottom = CssColor.Pack(0, 0, 255);
        var result = CssColor.Blend(top, bottom);

        Assert.Equal(255, CssColor.Alpha(result));
        Assert.Equal(128, CssColor.Red(result));
        Assert.Equal(0, CssColor.Green(result));
        Assert.Equal(127, CssColor.Blue(result));
    }

    [Fact]
    public void BlendOfTranslucentOverTransparentKeepsTheTopChannels()
    {
        var top = CssColor.Pack(10, 20, 30, 100);
        var result = CssColor.Blend(top, CssColor.Transparent);

        Assert.Equal(100, CssColor.Alpha(result));
        Assert.Equal(10, CssColor.Red(result));
        Assert.Equal(20, CssColor.Green(result));
        Assert.Equal(30, CssColor.Blue(result));
    }

    [Theory]
    [InlineData("#f00", 0xFFFF0000u)]
    [InlineData("#0f0", 0xFF00FF00u)]
    [InlineData("#00f", 0xFF0000FFu)]
    [InlineData("#abc", 0xFFAABBCCu)]
    [InlineData("#f00a", 0xAAFF0000u)]
    [InlineData("#FF8000", 0xFFFF8000u)]
    [InlineData("#11223344", 0x44112233u)]
    [InlineData("#FFFFFFFF", 0xFFFFFFFFu)]
    public void HexColorsParseAllSupportedLengths(string input, uint expected)
    {
        Assert.True(CssColor.TryParse(input, CssColor.Black, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData("#ff")]
    [InlineData("#fffff")]
    [InlineData("#ggg")]
    [InlineData("#12345g")]
    [InlineData("#")]
    public void InvalidHexColorsAreRejected(string input)
    {
        Assert.False(CssColor.TryParse(input, CssColor.Black, out _));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", 0xFFFF0000u)]
    [InlineData("rgb( 255 0 0 )", 0xFFFF0000u)]
    [InlineData("RGB(0,255,0)", 0xFF00FF00u)]
    [InlineData("rgb(100%, 0%, 0%)", 0xFFFF0000u)]
    [InlineData("rgb(300, -5, 0)", 0xFFFF0000u)]
    [InlineData("rgba(0, 0, 255, 0.5)", 0x7F0000FFu)]
    [InlineData("rgb(0 0 255 / 50%)", 0x7F0000FFu)]
    public void RgbColorsParseCommasSpacesAndPercentages(string input, uint expected)
    {
        Assert.True(CssColor.TryParse(input, CssColor.Black, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb()")]
    [InlineData("rgb(nope, 0, 0)")]
    [InlineData("rgb(10px, 0, 0)")]
    [InlineData("rgb(0, 0, 0, nope)")]
    [InlineData("hsl(nope, 100%, 50%)")]
    [InlineData("hsl(0, 100%, 50%, nope)")]
    public void MalformedColorComponentsAreRejected(string input)
    {
        Assert.False(CssColor.TryParse(input, CssColor.Black, out _));
    }

    [Theory]
    [InlineData("hsl(0, 100%, 50%)", 0xFFFF0000u)]
    [InlineData("hsl(120 100% 50%)", 0xFF00FF00u)]
    [InlineData("hsl(240, 100%, 50%)", 0xFF0000FFu)]
    [InlineData("hsl(360deg 100% 50%)", 0xFFFF0000u)]
    [InlineData("hsl(0.5turn 100% 50%)", 0xFF00FFFFu)]
    [InlineData("hsl(200grad 100% 50%)", 0xFF00FFFFu)]
    [InlineData("hsl(-120, 100%, 50%)", 0xFF0000FFu)]
    [InlineData("hsla(240, 100%, 50%, 0.5)", 0x7F0000FFu)]
    [InlineData("hsl(0 0% 50%)", 0xFF808080u)]
    [InlineData("hsl(0 100% 100%)", 0xFFFFFFFFu)]
    [InlineData("hsl(0 100% 0%)", 0xFF000000u)]
    public void HslColorsParseHueUnitsAndAlpha(string input, uint expected)
    {
        Assert.True(CssColor.TryParse(input, CssColor.Black, out var color));
        Assert.Equal(expected, color);
    }

    [Theory]
    [InlineData("red", 0xFFFF0000u)]
    [InlineData("RED", 0xFFFF0000u)]
    [InlineData("ReD", 0xFFFF0000u)]
    [InlineData("blue", 0xFF0000FFu)]
    [InlineData("rebeccapurple", 0xFF663399u)]
    [InlineData("aliceblue", 0xFFF0F8FFu)]
    [InlineData("yellowgreen", 0xFF9ACD32u)]
    [InlineData("grey", 0xFF808080u)]
    [InlineData("gray", 0xFF808080u)]
    [InlineData("white", 0xFFFFFFFFu)]
    [InlineData("black", 0xFF000000u)]
    public void NamedColorsAreCaseInsensitive(string input, uint expected)
    {
        Assert.True(CssColor.TryParse(input, CssColor.Transparent, out var color));
        Assert.Equal(expected, color);
    }

    [Fact]
    public void TransparentParsesToZero()
    {
        Assert.True(CssColor.TryParse("transparent", 0xFFFF0000u, out var color));
        Assert.Equal(CssColor.Transparent, color);
    }

    [Theory]
    [InlineData("currentColor")]
    [InlineData("currentcolor")]
    [InlineData("CURRENT-COLOR")]
    public void CurrentColorReturnsTheSuppliedColor(string input)
    {
        Assert.True(CssColor.TryParse(input, 0xFF123456, out var color));
        Assert.Equal(0xFF123456u, color);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-color")]
    [InlineData("rgbx(1,2,3)")]
    public void UnknownColorsAreRejected(string input)
    {
        Assert.False(CssColor.TryParse(input, CssColor.Black, out _));
    }

    [Fact]
    public void ParseOrFallsBackWhenParsingFails()
    {
        Assert.Equal(0xFF00FF00u, CssColor.ParseOr("nope", 0xFF00FF00));
        Assert.Equal(0xFFFF0000u, CssColor.ParseOr("red", 0xFF00FF00));
    }

    [Fact]
    public void ParseOrUsesTheCurrentColorForCurrentColor()
    {
        Assert.Equal(0xFF0A0B0C, CssColor.ParseOr("currentColor", CssColor.White, 0xFF0A0B0C));
    }
}
