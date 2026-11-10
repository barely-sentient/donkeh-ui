namespace Ignite.LayoutLab.Tests;

/// <summary>
/// Tests for the parsed-value cache. The dictionaries are internal, so the tests
/// exercise the public surface and verify that a real layout pass populates the
/// cache and that clearing it returns the observable count to zero.
/// </summary>
public class StyleValueCacheTests
{
    [Fact]
    public void ANewCacheIsEmpty()
    {
        var cache = new StyleValueCache();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ClearOnAnEmptyCacheIsIdempotent()
    {
        var cache = new StyleValueCache();
        cache.Clear();
        cache.Clear();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void ARealLayoutPassPopulatesTheCacheAndClearEmptiesIt()
    {
        var cache = new StyleValueCache();
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 100px; height: 40px; margin: 4px 8px; color: red;"
            + " border: 2px solid #336699; padding: 3px; }"), cache);

        var root = TestSupport.Node("div", "root");
        engine.Layout(root, TestSupport.Options());

        Assert.True(cache.Count > 0, "A layout pass should intern parsed values.");

        engine.ClearCaches();
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void TheEngineExposesTheCacheItWasConstructedWith()
    {
        var cache = new StyleValueCache();
        var engine = new LayoutEngine(TestSupport.Build("#root { width: 1px; }"), cache);

        Assert.Same(cache, engine.ValueCache);
    }

    [Fact]
    public void ALayingOutTheSameTreeTwiceDoesNotGrowTheCache()
    {
        var cache = new StyleValueCache();
        var table = TestSupport.Build(
            "#root { width: 100px; padding: 1px 2px 3px 4px; color: #123456; }");
        var engine = new LayoutEngine(table, cache);
        var root = TestSupport.Node("div", "root");

        engine.Layout(root, TestSupport.Options());
        var count = cache.Count;

        engine.Layout(root, TestSupport.Options());

        Assert.Equal(count, cache.Count);
    }
}
