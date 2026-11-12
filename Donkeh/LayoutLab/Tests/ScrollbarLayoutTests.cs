using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Scrollbar geometry is shared by paint and input handling, so these tests pin
/// down the track/thumb math used for track clicks and thumb dragging.
/// </summary>
public sealed class ScrollbarLayoutTests
{
    private static LayoutBox ScrollContainer(string css, out IgniteSupportedDomNode root)
    {
        root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = TestSupport.Layout(
            TestSupport.Build(css),
            root,
            TestSupport.Options(100, 100));
        return TestSupport.Box(result, root);
    }

    [Fact]
    public void VerticalScrollbarDescribesTrackAndThumb()
    {
        var box = ScrollContainer(
            "#root { overflow-y: auto; width: 100px; height: 100px; }"
            + " #child { height: 300px; }",
            out _);

        Assert.True(ScrollbarLayout.TryGetVertical(box, out var metrics));
        Assert.True(metrics.Vertical);
        Assert.Equal(box.PaddingBox.Right, metrics.Track.Right, 3);
        Assert.Equal(ScrollbarLayout.Thickness, metrics.Track.Width, 3);
        Assert.Equal(100, metrics.TrackLength, 3);
        Assert.Equal(100f / 3, metrics.ThumbLength, 3);
        Assert.Equal(0, metrics.ThumbOffset, 3);
        Assert.Equal(200, box.ScrollHeight - box.ClientHeight, 3);
    }

    [Fact]
    public void ClickingTheTrackMapsThePositionToAScrollOffset()
    {
        var box = ScrollContainer(
            "#root { overflow-y: auto; width: 100px; height: 100px; }"
            + " #child { height: 300px; }",
            out _);

        Assert.True(ScrollbarLayout.TryGetVertical(box, out var metrics));

        Assert.Equal(0, metrics.ScrollOffsetForPoint(metrics.Track.Y, 200), 3);
        Assert.Equal(100, metrics.ScrollOffsetForPoint(metrics.Track.Y + 50, 200), 3);
        Assert.Equal(200, metrics.ScrollOffsetForPoint(metrics.Track.Bottom, 200), 3);
    }

    [Fact]
    public void DraggingTheThumbMovesByTheMappedDistance()
    {
        var box = ScrollContainer(
            "#root { overflow-y: auto; width: 100px; height: 100px; }"
            + " #child { height: 300px; }",
            out _);

        Assert.True(ScrollbarLayout.TryGetVertical(box, out var metrics));

        // Half of the thumb travel equals half of the scrollable range.
        Assert.Equal(200, metrics.ScrollOffsetForDrag(100, metrics.MaxThumbOffset * 0.5f, 200), 3);
    }

    [Fact]
    public void BothScrollbarsReserveTheBottomRightCorner()
    {
        var box = ScrollContainer(
            "#root { overflow: auto; width: 100px; height: 100px; }"
            + " #child { width: 300px; height: 300px; }",
            out _);

        Assert.True(ScrollbarLayout.TryGetVertical(box, out var vertical));
        Assert.True(ScrollbarLayout.TryGetHorizontal(box, out var horizontal));

        const float thickness = ScrollbarLayout.Thickness;
        Assert.Equal(100 - thickness, vertical.TrackLength, 3);
        Assert.Equal(100 - thickness, horizontal.TrackLength, 3);
        Assert.Equal(horizontal.Track.Right, vertical.Track.X, 3);
        Assert.Equal(0, horizontal.Track.Y - vertical.Track.Bottom, 3);
    }

    [Fact]
    public void DraggingWithoutThumbTravelKeepsTheStartOffset()
    {
        var metrics = new ScrollbarMetrics(
            Vertical: true,
            Track: new Rect(0, 0, ScrollbarLayout.Thickness, 100),
            ThumbOffset: 0,
            ThumbLength: 100,
            TrackLength: 100);

        Assert.Equal(50, metrics.ScrollOffsetForDrag(50, 25, 200), 3);
    }

    [Fact]
    public void HorizontalScrollbarUsesTheBottomEdge()
    {
        var box = ScrollContainer(
            "#root { overflow-x: auto; width: 100px; height: 100px; }"
            + " #child { width: 300px; height: 100px; }",
            out _);

        Assert.True(ScrollbarLayout.TryGetHorizontal(box, out var metrics));
        Assert.False(metrics.Vertical);
        Assert.Equal(box.PaddingBox.Bottom, metrics.Track.Bottom, 3);
        Assert.Equal(ScrollbarLayout.Thickness, metrics.Track.Height, 3);
        Assert.Equal(100, metrics.TrackLength, 3);
    }
}
