namespace Ignite.LayoutLab.Tests;

public class GeometryTests
{
    [Fact]
    public void PointTranslatesByTheDelta()
    {
        Assert.Equal(new Point(3, 4), Point.Zero.Translate(3, 4));
        Assert.Equal(new Point(1, -1), new Point(3, 4).Translate(-2, -5));
        Assert.Equal(new Point(0, 0), Point.Zero);
    }

    [Fact]
    public void SizeReportsEmptyAndClamps()
    {
        Assert.True(Size.Zero.IsEmpty);
        Assert.True(new Size(0, 10).IsEmpty);
        Assert.True(new Size(10, 0).IsEmpty);
        Assert.False(new Size(1, 1).IsEmpty);

        var clamped = new Size(50, 200).Clamp(10, 20, 100, 150);
        Assert.Equal(new Size(50, 150), clamped);

        var small = new Size(1, 2).Clamp(10, 20, 100, 150);
        Assert.Equal(new Size(10, 20), small);
    }

    [Fact]
    public void UnboundedSizeIsInfinite()
    {
        Assert.True(float.IsPositiveInfinity(Size.Unbounded.Width));
        Assert.True(float.IsPositiveInfinity(Size.Unbounded.Height));
        Assert.False(Size.Unbounded.IsEmpty);
    }

    [Fact]
    public void RectExposesEdgesOriginAndSize()
    {
        var rect = new Rect(10, 20, 30, 40);
        Assert.Equal(40, rect.Right);
        Assert.Equal(60, rect.Bottom);
        Assert.Equal(new Point(10, 20), rect.Origin);
        Assert.Equal(new Size(30, 40), rect.Size);
        Assert.False(rect.IsEmpty);
        Assert.True(Rect.Empty.IsEmpty);
        Assert.True(new Rect(0, 0, -1, 5).IsEmpty);
    }

    [Theory]
    [InlineData(0, 0, false)]
    [InlineData(10, 20, true)]
    [InlineData(40, 60, true)]
    [InlineData(41, 60, false)]
    [InlineData(40, 61, false)]
    [InlineData(-1, 0, false)]
    public void ContainsIncludesTheEdges(float x, float y, bool expected)
    {
        var rect = new Rect(10, 20, 30, 40);
        Assert.Equal(expected, rect.Contains(x, y));
    }

    [Fact]
    public void IntersectsRequiresAPositiveOverlap()
    {
        var rect = new Rect(0, 0, 10, 10);
        Assert.True(rect.Intersects(new Rect(5, 5, 10, 10)));
        Assert.True(rect.Intersects(new Rect(-5, -5, 10, 10)));
        Assert.False(rect.Intersects(new Rect(10, 0, 10, 10)));
        Assert.False(rect.Intersects(new Rect(0, 10, 10, 10)));
        Assert.False(rect.Intersects(Rect.Empty));
    }

    [Fact]
    public void IntersectReturnsTheOverlapOrEmpty()
    {
        var rect = new Rect(0, 0, 10, 10);
        Assert.Equal(new Rect(5, 5, 5, 5), rect.Intersect(new Rect(5, 5, 10, 10)));
        Assert.Equal(new Rect(0, 0, 10, 10), rect.Intersect(new Rect(-5, -5, 30, 30)));
        Assert.Equal(Rect.Empty, rect.Intersect(new Rect(20, 20, 5, 5)));
        Assert.Equal(Rect.Empty, rect.Intersect(new Rect(10, 0, 5, 5)));
    }

    [Fact]
    public void UnionCombinesBothRectsAndTreatsEmptyAsIdentity()
    {
        var rect = new Rect(0, 0, 10, 10);
        Assert.Equal(new Rect(0, 0, 20, 20), rect.Union(new Rect(10, 10, 10, 10)));
        Assert.Equal(rect, rect.Union(Rect.Empty));
        Assert.Equal(rect, Rect.Empty.Union(rect));
    }

    [Fact]
    public void TranslateAndWithSizePreserveTheExpectedFields()
    {
        var rect = new Rect(1, 2, 3, 4);
        Assert.Equal(new Rect(11, 22, 3, 4), rect.Translate(10, 20));
        Assert.Equal(new Rect(1, 2, 8, 9), rect.WithSize(8, 9));
    }

    [Fact]
    public void InflateExpandsAllSides()
    {
        var rect = new Rect(10, 10, 20, 30);
        Assert.Equal(new Rect(8, 8, 24, 34), rect.Inflate(2));
        Assert.Equal(new Rect(5, 7, 30, 39), rect.Inflate(top: 3, right: 5, bottom: 6, left: 5));
    }

    [Fact]
    public void DeflateContractsAllSides()
    {
        var rect = new Rect(10, 10, 40, 50);
        Assert.Equal(new Rect(15, 13, 30, 41), rect.Deflate(top: 3, right: 5, bottom: 6, left: 5));

        var thickness = new Thickness(1, 2, 3, 4);
        Assert.Equal(new Rect(14, 11, 34, 46), rect.Deflate(thickness));
    }

    [Fact]
    public void ThicknessComputesAxesAndFactories()
    {
        var thickness = new Thickness(1, 2, 3, 4);
        Assert.Equal(6, thickness.Horizontal);
        Assert.Equal(4, thickness.Vertical);

        Assert.Equal(new Thickness(5, 5, 5, 5), Thickness.All(5));
        Assert.Equal(new Thickness(2, 8, 2, 8), Thickness.Symmetric(2, 8));
        Assert.Equal(new Thickness(0, 0, 0, 0), Thickness.Zero);
    }

    [Fact]
    public void CornerRadiusTracksZeroAndBuildsUniformRadii()
    {
        Assert.True(CornerRadius.Zero.IsZero);
        Assert.False(CornerRadius.All(1).IsZero);
        Assert.False(new CornerRadius(0, 0, 0, 1).IsZero);
        Assert.Equal(new CornerRadius(4, 4, 4, 4), CornerRadius.All(4));
    }
}
