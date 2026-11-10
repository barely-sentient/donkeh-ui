namespace Ignite.LayoutLab.Tests;

public class CssValueParserTests
{
    [Fact]
    public void EmptyLengthValuesBecomeAuto()
    {
        Assert.Equal(LengthUnit.Auto, CssValueParser.ParseLength(null).Unit);
        Assert.Equal(LengthUnit.Auto, CssValueParser.ParseLength("").Unit);
        Assert.Equal(LengthUnit.Auto, CssValueParser.ParseLength("   ").Unit);
    }

    [Theory]
    [InlineData("auto", LengthUnit.Auto, 0)]
    [InlineData("AUTO", LengthUnit.Auto, 0)]
    [InlineData("none", LengthUnit.Auto, 0)]
    [InlineData("min-content", LengthUnit.MinContent, 0)]
    [InlineData("max-content", LengthUnit.MaxContent, 0)]
    [InlineData("fit-content", LengthUnit.FitContent, 0)]
    [InlineData("stretch", LengthUnit.Stretch, 0)]
    [InlineData("-webkit-fill-available", LengthUnit.Auto, 0)]
    [InlineData("12px", LengthUnit.Px, 12)]
    [InlineData("1.5em", LengthUnit.Em, 1.5f)]
    [InlineData("2rem", LengthUnit.Rem, 2)]
    [InlineData("3ex", LengthUnit.Ex, 3)]
    [InlineData("4ch", LengthUnit.Ch, 4)]
    [InlineData("5vw", LengthUnit.Vw, 5)]
    [InlineData("6vh", LengthUnit.Vh, 6)]
    [InlineData("7vmin", LengthUnit.Vmin, 7)]
    [InlineData("8vmax", LengthUnit.Vmax, 8)]
    [InlineData("9cm", LengthUnit.Cm, 9)]
    [InlineData("10mm", LengthUnit.Mm, 10)]
    [InlineData("11in", LengthUnit.In, 11)]
    [InlineData("12pt", LengthUnit.Pt, 12)]
    [InlineData("13pc", LengthUnit.Pc, 13)]
    [InlineData("14fr", LengthUnit.Fr, 14)]
    [InlineData("15%", LengthUnit.Percent, 15)]
    [InlineData("16", LengthUnit.Number, 16)]
    [InlineData("17zz", LengthUnit.Number, 17)]
    [InlineData("unknown", LengthUnit.Auto, 0)]
    public void ParseLengthHandlesKeywordsUnitsAndBareNumbers(
        string input,
        LengthUnit unit,
        float value)
    {
        var length = CssValueParser.ParseLength(input);
        Assert.Equal(unit, length.Unit);
        Assert.Equal(value, length.Value, 4);
    }

    [Fact]
    public void ParseLengthOrAutoOnlyTreatsAutoAndBlankAsAuto()
    {
        Assert.Equal(LengthUnit.Auto, CssValueParser.ParseLengthOrAuto(null).Unit);
        Assert.Equal(LengthUnit.Auto, CssValueParser.ParseLengthOrAuto(" auto ").Unit);
        Assert.Equal(LengthUnit.Px, CssValueParser.ParseLengthOrAuto("4px").Unit);
    }

    [Theory]
    [InlineData("10px", 10)]
    [InlineData("12.5", 12.5f)]
    [InlineData("50%", 50)]
    [InlineData("2em", 2)]
    [InlineData("3fr", 3)]
    public void ParseNumberReadsTheFirstNumericToken(string input, float expected)
    {
        Assert.Equal(expected, CssValueParser.ParseNumber(input), 4);
    }

    [Fact]
    public void ParseNumberUsesFallbackForNullOrNonNumericInput()
    {
        Assert.Equal(7, CssValueParser.ParseNumber(null, 7));
        Assert.Equal(7, CssValueParser.ParseNumber("auto", 7));
        Assert.Equal(7, CssValueParser.ParseNumber("", 7));
    }

    [Fact]
    public void ParseNumberResolvesFunctions()
    {
        Assert.Equal(8, CssValueParser.ParseNumber("calc(2 * 4)"), 4);
        Assert.Equal(4, CssValueParser.ParseNumber("min(4, 8)"), 4);
        Assert.Equal(8, CssValueParser.ParseNumber("max(4, 8)"), 4);
        Assert.Equal(4, CssValueParser.ParseNumber("clamp(1, 6, 4)"), 4);
    }

    [Fact]
    public void ParseIntRoundsTheNumber()
    {
        Assert.Equal(13, CssValueParser.ParseInt("12.6"));
        Assert.Equal(-3, CssValueParser.ParseInt("-2.6"));
        Assert.Equal(9, CssValueParser.ParseInt(null, 9));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("yes", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("no", false)]
    [InlineData("anything", false)]
    public void ParseBoolAcceptsCssBooleans(string input, bool expected)
    {
        Assert.Equal(expected, CssValueParser.ParseBool(input));
    }

    [Fact]
    public void ParseBoolUsesFallbackWhenUnknownOrBlank()
    {
        Assert.True(CssValueParser.ParseBool(null, true));
        Assert.True(CssValueParser.ParseBool("", true));
        Assert.True(CssValueParser.ParseBool("what", true));
        Assert.False(CssValueParser.ParseBool("what", false));
    }

    [Fact]
    public void IsKeywordIsTrimmedAndCaseInsensitive()
    {
        Assert.True(CssValueParser.IsKeyword("  Auto ", "auto"));
        Assert.True(CssValueParser.IsKeyword("BORDER-BOX", "border-box"));
        Assert.False(CssValueParser.IsKeyword("auto ", "none"));
        Assert.False(CssValueParser.IsKeyword(null, "auto"));
    }

    [Fact]
    public void FirstKeywordReturnsOnlyAWord()
    {
        Assert.Equal("auto", CssValueParser.FirstKeyword("auto 10px"));
        Assert.Equal("solid", CssValueParser.FirstKeyword(" solid red "));
        Assert.Null(CssValueParser.FirstKeyword("10px auto"));
        Assert.Null(CssValueParser.FirstKeyword(""));
        Assert.Null(CssValueParser.FirstKeyword(null));
    }

    [Fact]
    public void ParseLengthKeepsCalcAsAnExpression()
    {
        var length = CssValueParser.ParseLength("calc(10px + 5px)");
        Assert.True(length.IsExpression);
        Assert.Equal(LengthUnit.Number, length.Unit);
        Assert.Equal(15, length.Resolve(new LengthContext()), 4);
    }

    [Theory]
    [InlineData("calc(10px + 5px)", 15)]
    [InlineData("calc(10px - 5px)", 5)]
    [InlineData("calc(2 * 3px)", 6)]
    [InlineData("calc(10px / 2)", 5)]
    [InlineData("calc(10px / 0)", 0)]
    [InlineData("calc(-5px + 10px)", 5)]
    [InlineData("calc(10px + 2 * 3px)", 16)]
    [InlineData("calc((10px + 5px) * 2)", 30)]
    [InlineData("min(10px, 20px)", 10)]
    [InlineData("max(10px, 20px)", 20)]
    [InlineData("clamp(5px, 10px, 8px)", 8)]
    [InlineData("clamp(5px, 2px, 8px)", 5)]
    [InlineData("clamp(5px, 12px, 8px)", 8)]
    [InlineData("calc(10px + min(20px, 5px))", 15)]
    [InlineData("calc(2 * max(3px, 4px))", 8)]
    [InlineData("foo(10px)", 0)]
    public void CalcMinMaxAndClampExpressionsResolve(string input, float expected)
    {
        var length = CssValueParser.ParseLength(input);
        Assert.Equal(expected, length.Resolve(new LengthContext()), 4);
    }

    [Fact]
    public void CalcCanMixPercentagesAndLengths()
    {
        var length = CssValueParser.ParseLength("calc(100% - 20px)");
        Assert.Equal(180, length.Resolve(new LengthContext(percentBasis: 200), 200), 4);
        Assert.Equal(-20, length.Resolve(new LengthContext(percentBasis: float.NaN), float.NaN), 4);
    }

    [Fact]
    public void ClampWithTooFewArgumentsUsesTheFirst()
    {
        Assert.Equal(5, CssValueParser.ParseNumber("clamp(5px)"), 4);
    }

    [Fact]
    public void SplitTopLevelBreaksOnCommasAndRespectsParentheses()
    {
        var parts = CssValueParser.SplitTopLevel("1px, calc(2px + 3px), 4px", ',');
        Assert.Equal(3, parts.Count);
        Assert.Equal("1px", parts[0]);
        Assert.Equal("calc(2px + 3px)", parts[1]);
        Assert.Equal("4px", parts[2]);
    }

    [Fact]
    public void SplitTopLevelCollapsesRunsOfWhitespace()
    {
        var parts = CssValueParser.SplitTopLevel("  1px   2px\t3px  ", ' ');
        Assert.Equal(3, parts.Count);
        Assert.Equal("1px", parts[0]);
        Assert.Equal("2px", parts[1]);
        Assert.Equal("3px", parts[2]);
    }

    [Fact]
    public void SplitTopLevelIgnoresSeparatorsInsideQuotes()
    {
        var parts = CssValueParser.SplitTopLevel("'a,b', \"c,d\"", ',');
        Assert.Equal(2, parts.Count);
        Assert.Equal("'a,b'", parts[0]);
        Assert.Equal("\"c,d\"", parts[1]);
    }

    [Fact]
    public void SplitTopLevelIgnoresSeparatorsInsideUrl()
    {
        var parts = CssValueParser.SplitTopLevel("url(a,b) 2px", ' ');
        Assert.Equal(2, parts.Count);
        Assert.Equal("url(a,b)", parts[0]);
        Assert.Equal("2px", parts[1]);
    }

    [Fact]
    public void SplitTopLevelSkipsTrailingAndEmptySeparators()
    {
        Assert.Empty(CssValueParser.SplitTopLevel("", ','));
        Assert.Single(CssValueParser.SplitTopLevel("1px,", ','));
        Assert.Empty(CssValueParser.SplitTopLevel(",,,", ','));
    }

    [Fact]
    public void SplitTopLevelSupportsSlashSeparators()
    {
        var parts = CssValueParser.SplitTopLevel("1 / 2 / 3", '/');
        Assert.Equal(3, parts.Count);
        Assert.Equal("1", parts[0]);
        Assert.Equal("2", parts[1]);
        Assert.Equal("3", parts[2]);
    }

    [Theory]
    [InlineData("\"abc\"", "abc")]
    [InlineData("'abc'", "abc")]
    [InlineData("\"\"", "")]
    [InlineData("abc", "abc")]
    [InlineData("\"abc'", "\"abc'")]
    [InlineData("a", "a")]
    public void UnquoteStripsOnlyMatchingQuotes(string input, string expected)
    {
        Assert.Equal(expected, CssValueParser.Unquote(input));
    }
}
