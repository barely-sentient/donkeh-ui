namespace Donkeh.LayoutLab.Tests;

public class CssLengthTests
{
    private static readonly LengthContext Context = new(
        fontSize: 20,
        rootFontSize: 10,
        viewportWidth: 1000,
        viewportHeight: 500,
        percentBasis: 200);

    [Theory]
    [InlineData("12px", 12f)]
    [InlineData("3", 3f)]
    [InlineData("25%", 50f)]
    [InlineData("2em", 40f)]
    [InlineData("2rem", 20f)]
    [InlineData("2ex", 20f)]
    [InlineData("3ch", 30f)]
    [InlineData("10vw", 100f)]
    [InlineData("10vh", 50f)]
    [InlineData("10vmin", 50f)]
    [InlineData("10vmax", 100f)]
    [InlineData("2pc", 32f)]
    public void ResolveConvertsEveryRelativeUnit(string css, float expected)
    {
        var length = CssValueParser.ParseLength(css);
        Assert.Equal(expected, length.Resolve(Context), 4);
    }

    [Fact]
    public void ResolveConvertsAbsoluteUnitsAtNinetySixDpi()
    {
        Assert.Equal(96f, CssValueParser.ParseLength("1in").Resolve(Context), 3);
        Assert.Equal(96f / 2.54f, CssValueParser.ParseLength("1cm").Resolve(Context), 3);
        Assert.Equal(96f / 25.4f, CssValueParser.ParseLength("1mm").Resolve(Context), 3);
        Assert.Equal(96f / 72f, CssValueParser.ParseLength("1pt").Resolve(Context), 3);
        Assert.Equal(16f, CssValueParser.ParseLength("1pc").Resolve(Context), 3);
    }

    [Fact]
    public void PercentageWithNoFiniteBasisResolvesToZero()
    {
        var context = new LengthContext(percentBasis: float.NaN);
        Assert.Equal(0, CssLength.Percent(50).Resolve(context));
        Assert.Equal(25, CssLength.Percent(50).Resolve(context, 50), 3);
    }

    [Fact]
    public void ResolveOrFallsBackForNonDefiniteValues()
    {
        Assert.Equal(7, CssLength.Auto.ResolveOr(Context, 200, 7));
        Assert.Equal(7, CssLength.MinContent.ResolveOr(Context, 200, 7));
        Assert.Equal(7, CssLength.MaxContent.ResolveOr(Context, 200, 7));
        Assert.Equal(7, CssLength.Stretch.ResolveOr(Context, 200, 7));
        Assert.Equal(7, CssLength.Fr(1).ResolveOr(Context, 200, 7));
        Assert.Equal(7, CssLength.Percent(10).ResolveOr(new LengthContext(), float.NaN, 7));
        Assert.Equal(20, CssLength.Percent(10).ResolveOr(Context, 200, 7), 3);
        Assert.Equal(
            7,
            CssValueParser.ParseLength("calc(100% - 10px)").ResolveOr(Context, float.NaN, 7));
    }

    [Theory]
    [InlineData("1px", true)]
    [InlineData("1", true)]
    [InlineData("1em", true)]
    [InlineData("10%", true)]
    [InlineData("auto", false)]
    [InlineData("1fr", false)]
    [InlineData("min-content", false)]
    [InlineData("fit-content", false)]
    public void IsDefiniteRequiresAValueTheAxisCanResolve(string css, bool expected)
    {
        Assert.Equal(expected, CssValueParser.ParseLength(css).IsDefinite(100));
    }

    [Fact]
    public void PercentIsOnlyDefiniteWhenABasisExists()
    {
        var length = CssLength.Percent(10);
        Assert.True(length.IsDefinite(100));
        Assert.False(length.IsDefinite(float.NaN));

        var expression = CssValueParser.ParseLength("calc(100% - 10px)");
        Assert.True(expression.IsDefinite(100));
        Assert.False(expression.IsDefinite(float.NaN));
    }

    [Fact]
    public void UnitPredicatesDescribeTheParsedValue()
    {
        Assert.True(CssLength.Auto.IsAuto);
        Assert.True(CssLength.Percent(1).IsPercent);
        Assert.True(CssLength.Fr(2).IsFr);
        Assert.True(CssLength.Number(2).IsNumber);
        Assert.True(CssValueParser.ParseLength("calc(1px + 1px)").IsExpression);
        Assert.False(CssLength.Px(1).IsExpression);

        Assert.True(CssLength.MinContent.IsIntrinsic);
        Assert.True(CssLength.MaxContent.IsIntrinsic);
        Assert.True(CssValueParser.ParseLength("fit-content").IsIntrinsic);
        Assert.True(CssLength.Stretch.IsIntrinsic);
        Assert.False(CssLength.Px(1).IsIntrinsic);
    }

    [Fact]
    public void StaticFactoriesProduceTheExpectedUnit()
    {
        Assert.Equal(LengthUnit.Auto, CssLength.Auto.Unit);
        Assert.Equal(LengthUnit.Px, CssLength.Zero.Unit);
        Assert.Equal(LengthUnit.Px, CssLength.Px(5).Unit);
        Assert.Equal(LengthUnit.Percent, CssLength.Percent(5).Unit);
        Assert.Equal(LengthUnit.Em, CssLength.Em(5).Unit);
        Assert.Equal(LengthUnit.Rem, CssLength.Rem(5).Unit);
        Assert.Equal(LengthUnit.Number, CssLength.Number(5).Unit);
        Assert.Equal(LengthUnit.Fr, CssLength.Fr(5).Unit);
    }

    [Theory]
    [InlineData("auto", "auto")]
    [InlineData("50%", "50%")]
    [InlineData("12.5px", "12.5px")]
    [InlineData("1.5em", "1.5em")]
    [InlineData("2rem", "2rem")]
    [InlineData("3fr", "3fr")]
    [InlineData("4", "4")]
    [InlineData("min-content", "min-content")]
    [InlineData("max-content", "max-content")]
    [InlineData("fit-content", "fit-content")]
    [InlineData("calc(1px + 2px)", "calc(...)")]
    public void ToStringUsesCssSyntax(string input, string expected)
    {
        Assert.Equal(expected, CssValueParser.ParseLength(input).ToString());
    }

    [Fact]
    public void LengthContextHelpersReplaceTheRequestedFields()
    {
        var updated = Context.WithFontSize(33).WithPercentBasis(44);
        Assert.Equal(33, updated.FontSize);
        Assert.Equal(44, updated.PercentBasis);
        Assert.Equal(Context.RootFontSize, updated.RootFontSize);
        Assert.Equal(Context.ViewportWidth, updated.ViewportWidth);
    }

    [Fact]
    public void CssLength4ShorthandExpandsOneToFourValues()
    {
        var one = CssLengths.ParseShorthand("1px");
        Assert.Equal(1, one.Top.Value);
        Assert.Equal(1, one.Right.Value);
        Assert.Equal(1, one.Bottom.Value);
        Assert.Equal(1, one.Left.Value);

        var two = CssLengths.ParseShorthand("1px 2px");
        Assert.Equal(1, two.Top.Value);
        Assert.Equal(2, two.Right.Value);
        Assert.Equal(1, two.Bottom.Value);
        Assert.Equal(2, two.Left.Value);

        var three = CssLengths.ParseShorthand("1px 2px 3px");
        Assert.Equal(1, three.Top.Value);
        Assert.Equal(2, three.Right.Value);
        Assert.Equal(3, three.Bottom.Value);
        Assert.Equal(2, three.Left.Value);

        var four = CssLengths.ParseShorthand("1px 2px 3px 4px");
        Assert.Equal(1, four.Top.Value);
        Assert.Equal(2, four.Right.Value);
        Assert.Equal(3, four.Bottom.Value);
        Assert.Equal(4, four.Left.Value);
    }

    [Fact]
    public void EmptyShorthandIsZero()
    {
        var lengths = CssLengths.ParseShorthand("");
        Assert.Equal(CssLength4.Zero, lengths);
    }

    [Fact]
    public void CssLength4IndexerAndWithersAddressAllSides()
    {
        var lengths = CssLengths.All(CssLength.Px(5));
        Assert.Equal(5, lengths[0].Value);
        Assert.Equal(5, lengths[1].Value);
        Assert.Equal(5, lengths[2].Value);
        Assert.Equal(5, lengths[3].Value);
        Assert.Throws<ArgumentOutOfRangeException>(() => lengths[4]);

        var changed = lengths
            .WithTop(CssLength.Px(1))
            .WithRight(CssLength.Px(2))
            .WithBottom(CssLength.Px(3))
            .WithLeft(CssLength.Px(4));

        Assert.Equal(1, changed.Top.Value);
        Assert.Equal(2, changed.Right.Value);
        Assert.Equal(3, changed.Bottom.Value);
        Assert.Equal(4, changed.Left.Value);
    }

    [Fact]
    public void CssLength4ResolveBuildsAThickness()
    {
        var lengths = CssLengths.ParseShorthand("1px 2px 3px 4px");
        var thickness = lengths.Resolve(Context, 100);

        Assert.Equal(1, thickness.Top);
        Assert.Equal(2, thickness.Right);
        Assert.Equal(3, thickness.Bottom);
        Assert.Equal(4, thickness.Left);
    }

    [Fact]
    public void AutoSidesResolveToZeroInAShorthand()
    {
        var thickness = CssLengths.ParseShorthand("auto").Resolve(Context, 100);
        Assert.Equal(0, thickness.Top);
        Assert.Equal(0, thickness.Right);
        Assert.Equal(0, thickness.Bottom);
        Assert.Equal(0, thickness.Left);
    }

    [Fact]
    public void CornerRadiusPercentagesResolveAgainstTheSmallerBoxSide()
    {
        var radii = CssLengths.ParseShorthand("10%");
        var resolved = new CssCornerRadius4(radii.Top, radii.Right, radii.Bottom, radii.Left)
            .Resolve(Context, 200, 80);

        Assert.Equal(8, resolved.TopLeft, 3);
        Assert.Equal(8, resolved.TopRight, 3);
        Assert.Equal(8, resolved.BottomRight, 3);
        Assert.Equal(8, resolved.BottomLeft, 3);
    }

    [Fact]
    public void CornerRadiusKeepsTheCssCornerOrder()
    {
        var radii = new CssCornerRadius4(
            CssLength.Px(1),
            CssLength.Px(2),
            CssLength.Px(3),
            CssLength.Px(4));

        var resolved = radii.Resolve(Context, 100, 100);
        Assert.Equal(1, resolved.TopLeft);
        Assert.Equal(2, resolved.TopRight);
        Assert.Equal(3, resolved.BottomRight);
        Assert.Equal(4, resolved.BottomLeft);
    }

    [Fact]
    public void CssCornerRadius4ZeroResolvesToZero()
    {
        var resolved = CssCornerRadius4.Zero.Resolve(Context, 100, 100);
        Assert.True(resolved.IsZero);
    }
}
