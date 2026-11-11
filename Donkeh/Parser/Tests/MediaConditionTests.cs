using Donkeh.Data;
using Donkeh.DataEngine;
using Donkeh.Parser;

namespace Donkeh.Parser.Tests;

public class MediaConditionTests
{
    private static DataTable Build(string css)
    {
        return new DataEngine.DataEngine(MediaConditionCompiler.Instance).Build(css);
    }

    private static bool Matches(DataTable table, RendererContext context)
    {
        return table.MatchesCondition(table.GetRule(0).ConditionId, context);
    }

    [Fact]
    public void MinWidthUsesTheViewportWidth()
    {
        var table = Build("@media (min-width: 768px) { .a { color: red; } }");

        Assert.False(Matches(table, new RendererContext { ViewportWidth = 767, ViewportHeight = 800 }));
        Assert.True(Matches(table, new RendererContext { ViewportWidth = 768, ViewportHeight = 800 }));
    }

    [Fact]
    public void MaxWidthAcceptsFractionalBootstrapBreakpoints()
    {
        var table = Build("@media (max-width: 767.98px) { .a { color: red; } }");

        Assert.True(Matches(table, new RendererContext { ViewportWidth = 767, ViewportHeight = 800 }));
        Assert.False(Matches(table, new RendererContext { ViewportWidth = 768, ViewportHeight = 800 }));
    }

    [Fact]
    public void CombinedFeaturesMustAllMatch()
    {
        var table = Build(
            "@media (min-width: 576px) and (max-width: 991.98px) { .a { color: red; } }");

        Assert.True(Matches(table, new RendererContext { ViewportWidth = 768, ViewportHeight = 800 }));
        Assert.False(Matches(table, new RendererContext { ViewportWidth = 500, ViewportHeight = 800 }));
        Assert.False(Matches(table, new RendererContext { ViewportWidth = 1000, ViewportHeight = 800 }));
    }

    [Fact]
    public void MediaTypesAndOrientationAreEvaluated()
    {
        var print = Build("@media print { .a { color: red; } }");
        var portrait = Build("@media (orientation: portrait) { .a { color: red; } }");

        Assert.False(Matches(print, new RendererContext { ViewportWidth = 400, ViewportHeight = 800 }));
        Assert.True(Matches(portrait, new RendererContext { ViewportWidth = 400, ViewportHeight = 800 }));
        Assert.False(Matches(portrait, new RendererContext { ViewportWidth = 800, ViewportHeight = 400 }));
    }

    [Fact]
    public void CommaSeparatedQueriesAreAnOrList()
    {
        var table = Build(
            "@media (max-width: 575.98px), (min-width: 1200px) { .a { color: red; } }");

        Assert.True(Matches(table, new RendererContext { ViewportWidth = 400, ViewportHeight = 800 }));
        Assert.False(Matches(table, new RendererContext { ViewportWidth = 800, ViewportHeight = 800 }));
        Assert.True(Matches(table, new RendererContext { ViewportWidth = 1280, ViewportHeight = 800 }));
    }

    [Fact]
    public void UnsupportedFeaturesFailClosed()
    {
        var table = Build("@media (hover: hover) { .a { color: red; } }");

        Assert.False(Matches(table, new RendererContext { ViewportWidth = 1280, ViewportHeight = 800 }));
    }
}
