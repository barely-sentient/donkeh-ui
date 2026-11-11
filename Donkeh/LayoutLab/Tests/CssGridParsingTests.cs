namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Tests for the grid grammar: track lists (including <c>repeat()</c> and
/// <c>minmax()</c>), named template areas and the grid-line placement parser.
/// </summary>
public class CssGridParsingTests
{
    // ------------------------------------------------------------ track lists

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("subgrid")]
    public void EmptyTrackListsCollapseToTheSharedEmptyValue(string? value)
    {
        var list = GridTrackList.Parse(value);

        Assert.Same(GridTrackList.Empty, list);
        Assert.Equal(0, list.Count);
        Assert.Empty(list.Tracks);
    }

    [Fact]
    public void FixedPercentAndFlexibleTracksAreParsedByKind()
    {
        var list = GridTrackList.Parse("100px 25% 2fr");

        Assert.Equal(3, list.Count);
        Assert.Equal(GridTrackKind.Fixed, list.Tracks[0].Kind);
        Assert.Equal(100, list.Tracks[0].Minimum.Resolve(new LengthContext()), 3);
        Assert.Equal(GridTrackKind.Percent, list.Tracks[1].Kind);
        Assert.Equal(25, list.Tracks[1].Minimum.Value, 3);
        Assert.Equal(GridTrackKind.Fr, list.Tracks[2].Kind);
        Assert.Equal(2, list.Tracks[2].Fr, 3);
        Assert.True(list.Tracks[2].IsFlexible);
    }

    [Fact]
    public void KeywordTracksMapToTheIntrinsicKinds()
    {
        var list = GridTrackList.Parse("auto min-content max-content");

        Assert.Equal(3, list.Count);
        Assert.Equal(GridTrackKind.Auto, list.Tracks[0].Kind);
        Assert.Equal(GridTrackKind.MinContent, list.Tracks[1].Kind);
        Assert.Equal(GridTrackKind.MaxContent, list.Tracks[2].Kind);
    }

    [Fact]
    public void MinMaxKeepsTheMinimumAndTheFlexibleMaximum()
    {
        var list = GridTrackList.Parse("minmax(100px, 1fr)");

        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.Fr, list.Tracks[0].Kind);
        Assert.Equal(1, list.Tracks[0].Fr, 3);
        Assert.Equal(100, list.Tracks[0].Minimum.Resolve(new LengthContext()), 3);
    }

    [Fact]
    public void MinMaxWithAPercentMaximumBecomesAPercentTrack()
    {
        var list = GridTrackList.Parse("minmax(10px, 50%)");

        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.Percent, list.Tracks[0].Kind);
        Assert.Equal(50, list.Tracks[0].Maximum.Value, 3);
    }

    [Fact]
    public void MinMaxWithAFixedMaximumStaysFixed()
    {
        var list = GridTrackList.Parse("minmax(10px, 90px)");

        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.Fixed, list.Tracks[0].Kind);
        Assert.Equal(90, list.Tracks[0].Maximum.Resolve(new LengthContext()), 3);
    }

    [Fact]
    public void MinMaxWithoutACommaFallsBackToAuto()
    {
        var list = GridTrackList.Parse("minmax(10px)");
        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.Auto, list.Tracks[0].Kind);
    }

    [Fact]
    public void FitContentCarriesItsLimit()
    {
        var list = GridTrackList.Parse("fit-content(40px)");

        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.FitContent, list.Tracks[0].Kind);
        Assert.Equal(40, list.Tracks[0].FitContentLimit, 3);
    }

    [Fact]
    public void LineNamesAreAttachedToTheLineBeforeEachTrack()
    {
        var list = GridTrackList.Parse("[a] 100px [b c] 1fr [d]");

        Assert.Equal(2, list.Count);
        Assert.Equal(3, list.LineNames.Length);
        Assert.Equal(new[] { "a" }, list.LineNames[0]);
        Assert.Equal(new[] { "b", "c" }, list.LineNames[1]);
        Assert.Equal(new[] { "d" }, list.LineNames[2]);
    }

    [Fact]
    public void AnUnclosedBracketConsumesTheRestOfTheListAsNames()
    {
        // The parser requires a closing bracket before a track; without one the
        // remainder is treated as line names and the list stays empty.
        var list = GridTrackList.Parse("[left 100px 1fr");
        Assert.Same(GridTrackList.Empty, list);
    }

    [Fact]
    public void RepeatExpandsThePatternTheRequestedNumberOfTimes()
    {
        var list = GridTrackList.Parse("repeat(3, 10px)");

        Assert.Equal(3, list.Count);
        Assert.All(list.Tracks, track => Assert.Equal(GridTrackKind.Fixed, track.Kind));
        Assert.All(list.Tracks, track => Assert.Equal(10, track.Maximum.Resolve(new LengthContext()), 3));
    }

    [Fact]
    public void RepeatExpandsMixedPatternsAndNames()
    {
        var list = GridTrackList.Parse("repeat(2, [x] 10px 1fr)");

        Assert.Equal(4, list.Count);
        Assert.Equal(GridTrackKind.Fixed, list.Tracks[0].Kind);
        Assert.Equal(GridTrackKind.Fr, list.Tracks[1].Kind);
        Assert.Equal(GridTrackKind.Fixed, list.Tracks[2].Kind);
        Assert.Equal(GridTrackKind.Fr, list.Tracks[3].Kind);
        Assert.Contains("x", list.LineNames[0]);
        Assert.Contains("x", list.LineNames[2]);
    }

    [Fact]
    public void RepeatWithANonPositiveCountIsNotExpanded()
    {
        var list = GridTrackList.Parse("repeat(0, 10px)");

        // The malformed repeat is treated as an ordinary (unknown) track, so the
        // parse never produces the requested zero tracks.
        Assert.True(list.Count <= 1);
    }

    [Theory]
    [InlineData("auto-fill", false)]
    [InlineData("auto-fit", true)]
    public void AutoRepeatKeepsThePatternAsAPlaceholder(string count, bool fit)
    {
        var list = GridTrackList.Parse($"repeat({count}, 100px)");

        Assert.Single(list.Tracks);
        Assert.Equal(GridTrackKind.Auto, list.Tracks[0].Kind);
        Assert.Equal(0, list.AutoRepeatIndex);
        Assert.Single(list.AutoRepeatTracks);
        Assert.Equal(GridTrackKind.Fixed, list.AutoRepeatTracks[0].Kind);
        Assert.Equal(fit, list.AutoRepeatFit);
        Assert.Empty(list.AutoRepeatLineNames[0]);
    }

    [Fact]
    public void AutoRepeatKeepsInnerLineNames()
    {
        var list = GridTrackList.Parse("repeat(auto-fill, [col] minmax(100px, 1fr))");

        Assert.Equal(0, list.AutoRepeatIndex);
        Assert.Single(list.AutoRepeatTracks);
        Assert.Equal(GridTrackKind.Fr, list.AutoRepeatTracks[0].Kind);
        Assert.Equal(new[] { "col" }, list.AutoRepeatLineNames[0]);
    }

    [Fact]
    public void AutoRepeatWithAnEmptyPatternFallsBackToAuto()
    {
        var list = GridTrackList.Parse("repeat(auto-fill, )");

        Assert.Single(list.AutoRepeatTracks);
        Assert.Equal(GridTrackKind.Auto, list.AutoRepeatTracks[0].Kind);
    }

    [Fact]
    public void ASecondAutoRepeatIsIgnored()
    {
        var list = GridTrackList.Parse(
            "repeat(auto-fill, 10px) repeat(auto-fit, 20px)");

        Assert.Equal(0, list.AutoRepeatIndex);
        // The first auto-repeat owns the expansion; the second becomes a plain
        // (unknown) track and never overwrites the pattern.
        Assert.Single(list.AutoRepeatTracks);
        Assert.False(list.AutoRepeatFit);
    }

    [Fact]
    public void TrackFactoriesProduceTheExpectedKinds()
    {
        Assert.Equal(GridTrackKind.Auto, GridTrack.Auto.Kind);
        Assert.Equal(GridTrackKind.Fixed, GridTrack.Fixed(CssLength.Px(5)).Kind);
        Assert.Equal(5, GridTrack.Fixed(CssLength.Px(5)).Minimum.Resolve(new LengthContext()), 3);

        var fraction = GridTrack.Fraction(2);
        Assert.Equal(GridTrackKind.Fr, fraction.Kind);
        Assert.Equal(2, fraction.Fr, 3);
        Assert.True(fraction.IsFlexible);
    }

    // ---------------------------------------------------------- named areas

    [Fact]
    public void NamedAreasAreMeasuredAsBoundingRectangles()
    {
        var areas = GridTemplateAreas.Parse("\"a a\" \"b c\"");

        Assert.NotNull(areas);
        Assert.Equal(2, areas!.RowCount);
        Assert.Equal(2, areas.ColumnCount);
        Assert.Equal(3, areas.Areas.Count);

        Assert.Equal(new GridAreaRect(0, 1, 0, 2), areas.Areas["a"]);
        Assert.Equal(new GridAreaRect(1, 2, 0, 1), areas.Areas["b"]);
        Assert.Equal(new GridAreaRect(1, 2, 1, 2), areas.Areas["c"]);
    }

    [Fact]
    public void DotAndNoneCellsAreSkipped()
    {
        var areas = GridTemplateAreas.Parse("\". a\" \"none a\"");

        Assert.NotNull(areas);
        Assert.Single(areas!.Areas);
        Assert.Equal(new GridAreaRect(0, 2, 1, 2), areas.Areas["a"]);
    }

    [Fact]
    public void SingleQuotesAreAccepted()
    {
        var areas = GridTemplateAreas.Parse("'x' 'y'");

        Assert.NotNull(areas);
        Assert.Equal(2, areas!.RowCount);
        Assert.Equal(1, areas.ColumnCount);
        Assert.True(areas.Areas.ContainsKey("x"));
        Assert.True(areas.Areas.ContainsKey("y"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("none")]
    public void MissingOrKeywordAreasParseToNull(string? value)
    {
        Assert.Null(GridTemplateAreas.Parse(value));
    }

    [Fact]
    public void TransposeSwapsRowsAndColumnsAndAreaBounds()
    {
        var areas = GridTemplateAreas.Parse("\"a a\" \"b c\"")!;
        var transposed = areas.Transpose();

        Assert.Equal(areas.ColumnCount, transposed.RowCount);
        Assert.Equal(areas.RowCount, transposed.ColumnCount);

        var a = transposed.Areas["a"];
        Assert.Equal(new GridAreaRect(0, 2, 0, 1), a);
        Assert.Equal(areas.Areas["a"].RowSpan, a.ColumnSpan);
        Assert.Equal(areas.Areas["a"].ColumnSpan, a.RowSpan);
    }

    [Fact]
    public void AreaRectSpansAreAtLeastOne()
    {
        Assert.Equal(1, new GridAreaRect(3, 3, 4, 4).RowSpan);
        Assert.Equal(1, new GridAreaRect(3, 3, 4, 4).ColumnSpan);
        Assert.Equal(2, new GridAreaRect(1, 3, 5, 8).RowSpan);
        Assert.Equal(3, new GridAreaRect(1, 3, 5, 8).ColumnSpan);
    }

    // ------------------------------------------------------------ placements

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public void EmptyOrAutoPlacementIsTheSharedAutoValue(string? value)
    {
        Assert.Equal(GridPlacement.Auto, GridPlacementParser.Parse(value));
    }

    [Fact]
    public void ASingleNumberAnchorsTheStartLine()
    {
        var placement = GridPlacementParser.Parse("2");

        Assert.Equal(2, placement.Start);
        Assert.Equal(0, placement.End);
        Assert.False(placement.StartAuto);
        Assert.True(placement.EndAuto);
        Assert.False(placement.HasNames);
    }

    [Fact]
    public void ALoneSpanLeavesBothSidesAuto()
    {
        var placement = GridPlacementParser.Parse("span 3");

        Assert.True(placement.StartAuto);
        Assert.True(placement.EndAuto);
        Assert.Equal(3, placement.SpanCount);
        Assert.Equal(3, placement.Span);
    }

    [Fact]
    public void ALoneSpanClampsToAtLeastOne()
    {
        var placement = GridPlacementParser.Parse("span 0");
        Assert.Equal(1, placement.SpanCount);
    }

    [Fact]
    public void TwoNumbersGiveDefiniteStartAndEnd()
    {
        var placement = GridPlacementParser.Parse("1 / 4");

        Assert.Equal(1, placement.Start);
        Assert.Equal(4, placement.End);
        Assert.False(placement.StartAuto);
        Assert.False(placement.EndAuto);
        Assert.Equal(3, placement.Span);
    }

    [Fact]
    public void AStartNumberWithAnEndSpanAnchorsToTheEndSide()
    {
        var placement = GridPlacementParser.Parse("1 / span 2");

        Assert.Equal(1, placement.Start);
        Assert.True(placement.EndAuto);
        Assert.True(placement.SpanOnEnd);
        Assert.Equal(2, placement.SpanCount);
        Assert.Equal(2, placement.Span);
    }

    [Fact]
    public void AStartSpanWithAnEndNumberRunsBackwards()
    {
        var placement = GridPlacementParser.Parse("span 2 / 5");

        Assert.True(placement.StartAuto);
        Assert.Equal(5, placement.End);
        Assert.Equal(2, placement.SpanCount);
        Assert.Equal(2, placement.Span);
    }

    [Fact]
    public void NamesAreRecordedOnTheirSide()
    {
        var placement = GridPlacementParser.Parse("header / footer");

        Assert.Equal("header", placement.StartName);
        Assert.Equal("footer", placement.EndName);
        Assert.True(placement.HasNames);
        Assert.False(placement.StartAuto);
        Assert.False(placement.EndAuto);
    }

    [Fact]
    public void ASingleNameIsAStartNamedLine()
    {
        var placement = GridPlacementParser.Parse("content");

        Assert.Equal("content", placement.StartName);
        Assert.Null(placement.EndName);
        Assert.True(placement.EndAuto);
        Assert.False(placement.StartAuto);
    }

    [Fact]
    public void SpanCanCarryAName()
    {
        var placement = GridPlacementParser.Parse("span content");

        Assert.True(placement.StartAuto);
        Assert.True(placement.EndAuto);
        Assert.Equal(1, placement.SpanCount);
        Assert.Equal("content", placement.EndName);
    }

    [Fact]
    public void ASpanAgainstANamedEndKeepsTheName()
    {
        var placement = GridPlacementParser.Parse("span 2 / content");

        Assert.True(placement.StartAuto);
        Assert.False(placement.EndAuto);
        Assert.Equal(2, placement.SpanCount);
        Assert.Equal("content", placement.EndName);
    }

    [Theory]
    [InlineData("3", true, 3, false, null)]
    [InlineData("span 2", true, 2, true, null)]
    [InlineData("auto", false, 0, false, null)]
    [InlineData("", false, 0, false, null)]
    [InlineData("sidebar", true, 0, false, "sidebar")]
    public void TryParseLineExposesTheLineGrammar(
        string value, bool expected, int line, bool isSpan, string? name)
    {
        var success = GridPlacementParser.TryParseLine(value, out var parsedLine, out var parsedSpan, out var parsedName);

        Assert.Equal(expected, success);
        Assert.Equal(line, parsedLine);
        Assert.Equal(isSpan, parsedSpan);
        Assert.Equal(name, parsedName);
    }

    [Fact]
    public void AnEmptySpanDefaultsToASpanOfOne()
    {
        var placement = GridPlacementParser.Parse("span 1 / 3");

        Assert.True(placement.StartAuto);
        Assert.Equal(3, placement.End);
        Assert.Equal(1, placement.SpanCount);
    }
}
