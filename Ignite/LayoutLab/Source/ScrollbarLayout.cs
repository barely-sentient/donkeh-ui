namespace Ignite.LayoutLab;

/// <summary>
/// The painted geometry of one scrollbar. <see cref="Track"/> uses the
/// scroll container's unscrolled absolute coordinates, like every other
/// <see cref="LayoutBox"/> rect.
/// </summary>
public readonly record struct ScrollbarMetrics(
    bool Vertical,
    Rect Track,
    float ThumbOffset,
    float ThumbLength,
    float TrackLength)
{
    public float MaxThumbOffset => MathF.Max(0, TrackLength - ThumbLength);

    public Rect ThumbRect => Vertical
        ? new Rect(Track.X, Track.Y + ThumbOffset, Track.Width, ThumbLength)
        : new Rect(Track.X + ThumbOffset, Track.Y, ThumbLength, Track.Height);

    /// <summary>
    /// Maps a point along the track to a scroll offset. The thumb is centred on
    /// the point, which is what makes clicking a track position jump there.
    /// </summary>
    public float ScrollOffsetForPoint(float pointer, float maxScroll)
    {
        var travel = MaxThumbOffset;

        if (travel <= 0 || maxScroll <= 0)
        {
            return 0;
        }

        var local = Vertical ? pointer - Track.Y : pointer - Track.X;
        var thumbOffset = Math.Clamp(local - ThumbLength * 0.5f, 0, travel);
        return thumbOffset / travel * maxScroll;
    }

    /// <summary>Maps a pointer delta while dragging the thumb to a scroll offset.</summary>
    public float ScrollOffsetForDrag(float startScroll, float pointerDelta, float maxScroll)
    {
        var travel = MaxThumbOffset;

        if (travel <= 0 || maxScroll <= 0)
        {
            return 0;
        }

        return Math.Clamp(startScroll + pointerDelta * (maxScroll / travel), 0, maxScroll);
    }
}

/// <summary>Computes scrollbar tracks and thumbs for the painter and input router.</summary>
public static class ScrollbarLayout
{
    public const float Thickness = 10;

    private const float OverflowEpsilon = 0.5f;

    public static bool TryGetVertical(LayoutBox box, out ScrollbarMetrics metrics)
    {
        if (box.ScrollHeight <= box.ClientHeight + OverflowEpsilon
            || (!box.Style.ShowScrollbars && box.Style.OverflowY is not (Overflow.Scroll or Overflow.Auto)))
        {
            metrics = default;
            return false;
        }

        var trackLength = MathF.Max(0, box.PaddingBox.Height);
        var client = MathF.Max(1, box.ClientHeight);
        var thumbLength = MathF.Min(
            trackLength,
            MathF.Max(12, trackLength * (client / MathF.Max(client, box.ScrollHeight))));
        var maxScroll = MathF.Max(1, box.ScrollHeight - client);
        var thumbOffset = Math.Clamp(box.ScrollOffsetY / maxScroll, 0, 1) * (trackLength - thumbLength);

        metrics = new ScrollbarMetrics(
            Vertical: true,
            Track: new Rect(
                box.PaddingBox.Right - Thickness,
                box.PaddingBox.Y,
                Thickness,
                trackLength),
            ThumbOffset: thumbOffset,
            ThumbLength: thumbLength,
            TrackLength: trackLength);
        return true;
    }

    public static bool TryGetHorizontal(LayoutBox box, out ScrollbarMetrics metrics)
    {
        if (box.ScrollWidth <= box.ClientWidth + OverflowEpsilon
            || (!box.Style.ShowScrollbars && box.Style.OverflowX is not (Overflow.Scroll or Overflow.Auto)))
        {
            metrics = default;
            return false;
        }

        var trackLength = MathF.Max(0, box.PaddingBox.Width);
        var client = MathF.Max(1, box.ClientWidth);
        var thumbLength = MathF.Min(
            trackLength,
            MathF.Max(12, trackLength * (client / MathF.Max(client, box.ScrollWidth))));
        var maxScroll = MathF.Max(1, box.ScrollWidth - client);
        var thumbOffset = Math.Clamp(box.ScrollOffsetX / maxScroll, 0, 1) * (trackLength - thumbLength);

        metrics = new ScrollbarMetrics(
            Vertical: false,
            Track: new Rect(
                box.PaddingBox.X,
                box.PaddingBox.Bottom - Thickness,
                trackLength,
                Thickness),
            ThumbOffset: thumbOffset,
            ThumbLength: thumbLength,
            TrackLength: trackLength);
        return true;
    }
}
