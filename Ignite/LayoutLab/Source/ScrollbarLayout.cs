namespace Ignite.LayoutLab;

/// <summary>
/// The painted geometry of one scrollbar. <see cref="Track"/> uses the
/// scroll container's unscrolled absolute coordinates, like every other
/// <see cref="LayoutBox"/> rect.
/// </summary>
/// <param name="Vertical">Whether this is a vertical scrollbar; selects the pointer axis and thumb orientation.</param>
/// <param name="Track">The absolute track rectangle in CSS pixels.</param>
/// <param name="ThumbOffset">The thumb's offset from the track start in CSS pixels, at least zero.</param>
/// <param name="ThumbLength">The thumb length along the track in CSS pixels.</param>
/// <param name="TrackLength">The track length along the scroll axis in CSS pixels.</param>
/// <remarks>
/// All lengths are along the scroll axis: height for a vertical scrollbar,
/// width for a horizontal one. The struct is immutable and has no reference to
/// the box it was derived from, so it is safe to hand to the input router.
/// </remarks>
public readonly record struct ScrollbarMetrics(
    bool Vertical,
    Rect Track,
    float ThumbOffset,
    float ThumbLength,
    float TrackLength)
{
    /// <summary>Gets the maximum thumb travel, which is the track length minus the thumb length.</summary>
    /// <value>Never negative; zero when the thumb fills the track.</value>
    public float MaxThumbOffset => MathF.Max(0, TrackLength - ThumbLength);

    /// <summary>Gets the absolute thumb rectangle in CSS pixels.</summary>
    /// <remarks>
    /// The thumb spans the full track thickness and is placed
    /// <see cref="ThumbOffset"/> along the scroll axis.
    /// </remarks>
    public Rect ThumbRect => Vertical
        ? new Rect(Track.X, Track.Y + ThumbOffset, Track.Width, ThumbLength)
        : new Rect(Track.X + ThumbOffset, Track.Y, ThumbLength, Track.Height);

    /// <summary>
    /// Maps a point along the track to a scroll offset. The thumb is centred on
    /// the point, which is what makes clicking a track position jump there.
    /// </summary>
    /// <param name="pointer">The pointer coordinate along the scroll axis, in absolute CSS pixels.</param>
    /// <param name="maxScroll">The maximum scroll offset in CSS pixels.</param>
    /// <returns>A scroll offset in the range 0 to <paramref name="maxScroll"/>.</returns>
    /// <remarks>
    /// Returns zero when the thumb cannot travel or there is nothing to scroll.
    /// The pointer is converted to track-relative coordinates before centring.
    /// </remarks>
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
    /// <param name="startScroll">The scroll offset when the drag began, in CSS pixels.</param>
    /// <param name="pointerDelta">The pointer movement along the scroll axis since the drag began, in CSS pixels.</param>
    /// <param name="maxScroll">The maximum scroll offset in CSS pixels.</param>
    /// <returns>The new scroll offset, clamped to the range 0 to <paramref name="maxScroll"/>.</returns>
    /// <remarks>
    /// The delta is scaled by <c>maxScroll / travel</c> so the thumb tracks the
    /// pointer. Returns zero rather than <paramref name="startScroll"/> when
    /// there is no travel or nothing to scroll.
    /// </remarks>
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
/// <remarks>
/// <para>
/// Scrollbars are overlay-style: they are drawn inside the padding box and do
/// not consume layout space. <see cref="Thickness"/> is used for both the track
/// thickness and the painter's expected width.
/// </para>
/// <para>
/// A scrollbar is reported only when the scrollable extent exceeds the client
/// size by more than <c>0.5</c> px (to tolerate fractional layout) and either
/// <see cref="ComputedStyle.ShowScrollbars"/> is set or the corresponding
/// overflow value is <see cref="Overflow.Scroll"/> or <see cref="Overflow.Auto"/>.
/// <see cref="Overflow.Hidden"/> alone does not show a scrollbar.
/// </para>
/// </remarks>
public static class ScrollbarLayout
{
    /// <summary>The scrollbar thickness in CSS pixels.</summary>
    public const float Thickness = 10;

    /// <summary>Sub-pixel overflow tolerated before a scrollbar is reported, in CSS pixels.</summary>
    private const float OverflowEpsilon = 0.5f;

    /// <summary>Computes the vertical scrollbar geometry for a box.</summary>
    /// <param name="box">The scroll container; its padding box and vertical scroll metrics are read.</param>
    /// <param name="metrics">Receives the geometry on success, otherwise the default value.</param>
    /// <returns><see langword="true"/> when a vertical scrollbar should be painted.</returns>
    /// <remarks>
    /// The track spans the full padding-box height at the right edge. The thumb
    /// is proportional to <c>clientHeight / scrollHeight</c> with a 12 px
    /// minimum, and its offset maps the current scroll position onto the
    /// available travel. The track does not reserve space for a horizontal
    /// scrollbar.
    /// </remarks>
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

    /// <summary>Computes the horizontal scrollbar geometry for a box.</summary>
    /// <param name="box">The scroll container; its padding box and horizontal scroll metrics are read.</param>
    /// <param name="metrics">Receives the geometry on success, otherwise the default value.</param>
    /// <returns><see langword="true"/> when a horizontal scrollbar should be painted.</returns>
    /// <remarks>
    /// The track spans the full padding-box width at the bottom edge. The thumb
    /// uses the same 12 px minimum and proportional sizing as
    /// <see cref="TryGetVertical"/>. The track is not shortened for a vertical
    /// scrollbar, so the two tracks can overlap in the bottom-right corner.
    /// </remarks>
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
