namespace Ignite.LayoutLab;

/// <summary>A float's margin box plus the side it was placed on.</summary>
/// <param name="MarginBox">
/// Margin box in absolute layout coordinates, in CSS pixels. It includes the
/// float's vertical and horizontal margins so flow content that avoids it keeps
/// clear of the whole margin box.
/// </param>
/// <param name="Side">Side of the block formatting context the float was placed against.</param>
/// <remarks>
/// Instances are immutable and stored by <see cref="FloatContext"/> until that
/// context is reset. The rectangle is never adjusted after registration, so
/// moving a float requires clearing and re-registering it.
/// </remarks>
/// <seealso cref="FloatContext"/>
internal readonly record struct FloatPlacement(Rect MarginBox, Float Side);

/// <summary>The horizontal band available at a given vertical position.</summary>
/// <param name="Y">Absolute y coordinate the band applies to, in CSS pixels.</param>
/// <param name="Left">Left edge available for content, in CSS pixels.</param>
/// <param name="Right">Right edge available for content, in CSS pixels.</param>
/// <remarks>
/// A band is one horizontal slice between active floats, as returned by
/// <see cref="FloatContext.BandAt"/>. When floats cover the full width the band
/// is empty; callers should compare <see cref="Width"/> with the content they
/// intend to place rather than assuming a positive width.
/// </remarks>
internal readonly record struct FloatBand(float Y, float Left, float Right)
{
    /// <summary>Gets the usable width of the band in CSS pixels.</summary>
    /// <value><c>Right - Left</c>, clamped to zero when the edges cross.</value>
    public float Width => MathF.Max(0, Right - Left);

    /// <summary>Gets a value indicating whether the band is too narrow to hold content.</summary>
    /// <remarks>
    /// A 0.01 px tolerance treats sub-pixel gaps as empty, which stops float
    /// avoidance from producing degenerate zero-width line boxes.
    /// </remarks>
    public bool IsEmpty => Width <= 0.01f;
}

/// <summary>
/// The floats active in one block formatting context. Floats are stored in
/// absolute layout coordinates, so a block that does not establish a new BFC can
/// share the context with its ancestor and its line boxes will avoid the same
/// floats. Establishing boxes (the root, <c>flow-root</c>, inline-blocks, scroll
/// containers, flex/grid containers, and floats themselves) get a fresh context.
/// </summary>
/// <remarks>
/// <para>
/// A context is owned by the establishing <see cref="LayoutBox"/> and borrowed
/// by its in-flow descendants. The layout engine reuses the owned instance
/// between passes and calls <see cref="Reset"/> before refilling it, so
/// registration is cheap and does not allocate once the backing list has grown.
/// </para>
/// <para>
/// Only margin boxes with positive width and height are stored. Entries are
/// kept in placement order; the queries scan all of them, which is linear in the
/// number of active floats. There is no sorting, merging or removal short of
/// <see cref="Reset"/>, so a page with very many concurrent floats pays that
/// cost on every <see cref="BandAt"/> call.
/// </para>
/// <para>
/// The type is not thread-safe. All coordinates are in CSS pixels and use the
/// same absolute, top-left-origin space as <see cref="Rect"/>.
/// </para>
/// </remarks>
/// <seealso cref="FloatPlacement"/>
/// <seealso cref="FloatBand"/>
internal sealed class FloatContext
{
    private readonly List<FloatPlacement> _floats = [];

    /// <summary>Gets the number of floats currently registered.</summary>
    /// <value>A count that includes floats entirely above the current flow position.</value>
    /// <remarks>
    /// Used mainly for diagnostics; the queries do their own filtering by y
    /// range.
    /// </remarks>
    public int Count => _floats.Count;

    /// <summary>Removes every registered float while keeping the backing capacity.</summary>
    /// <remarks>
    /// Called by the layout engine when an establishing box is laid out again so
    /// placements from the previous pass cannot leak into the new one.
    /// </remarks>
    public void Reset() => _floats.Clear();

    /// <summary>Registers a float that has already been positioned.</summary>
    /// <param name="marginBox">
    /// Absolute margin box of the float, in CSS pixels. Vertical and horizontal
    /// margins are part of the rectangle.
    /// </param>
    /// <param name="side">Side the float was placed on.</param>
    /// <remarks>
    /// Empty boxes (zero or negative width or height) are ignored, because they
    /// cannot affect any band or clearance calculation. Registration does not
    /// validate that the box fits the context or that it does not overlap an
    /// existing float; the caller's placement loop owns that decision.
    /// </remarks>
    public void Add(Rect marginBox, Float side)
    {
        if (marginBox.Width <= 0 || marginBox.Height <= 0)
        {
            return;
        }

        _floats.Add(new FloatPlacement(marginBox, side));
    }

    /// <summary>Gets the lowest bottom edge among the registered floats.</summary>
    /// <returns>
    /// The greatest <see cref="Rect.Bottom"/> value, or
    /// <see cref="float.NegativeInfinity"/> when no float is registered.
    /// </returns>
    /// <remarks>
    /// "Lowest" is in the CSS sense of the largest y coordinate. The sentinel
    /// lets an establishing box enclose its floats with
    /// <see cref="float.IsFinite"/> as the test, without a separate count check.
    /// The scan is linear.
    /// </remarks>
    public float MaxBottom()
    {
        var bottom = float.NegativeInfinity;

        for (var i = 0; i < _floats.Count; i++)
        {
            bottom = MathF.Max(bottom, _floats[i].MarginBox.Bottom);
        }

        return float.IsFinite(bottom) ? bottom : float.NegativeInfinity;
    }

    /// <summary>
    /// Returns the first vertical position at or below <paramref name="y"/> whose
    /// band is wide enough to be non-empty. When floats cover the full width the
    /// scan advances to the next float bottom.
    /// </summary>
    /// <param name="y">Absolute y position to start the search from, in CSS pixels.</param>
    /// <param name="left">Left edge of the containing block's content area, in CSS pixels.</param>
    /// <param name="right">
    /// Right edge of the containing block's content area, in CSS pixels.
    /// </param>
    /// <returns>
    /// A band at the resolved y position. Its edges are narrowed by overlapping
    /// left and right floats; it may be empty when no gap exists.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A float is active for the query when <paramref name="y"/> lies in the
    /// half-open interval from its top edge up to but not including its bottom
    /// edge, so adjacent floats that only share a horizontal edge do not overlap
    /// vertically. A left float raises the band's left edge to its right side; a
    /// right float lowers the band's right edge to its left side.
    /// </para>
    /// <para>
    /// When the resulting gap is narrower than 0.01 px the scan jumps to the
    /// smallest bottom among the floats that made it too narrow, which is the
    /// next position where the obstruction could change. If no finite next
    /// bottom is found, or it would not advance <paramref name="y"/>, the
    /// current (possibly empty) band is returned. A guard of 64 jumps bounds
    /// pathological input; when the loop is exhausted the method returns the
    /// original full-width band at the last y, which may overlap floats.
    /// </para>
    /// <para>
    /// The method does not allocate and scans every registered float on each
    /// iteration, so the cost is O(floats) per candidate position.
    /// </para>
    /// </remarks>
    public FloatBand BandAt(float y, float left, float right)
    {
        if (_floats.Count == 0 || right <= left)
        {
            return new FloatBand(y, left, right);
        }

        for (var guard = 0; guard < 64; guard++)
        {
            var bandLeft = left;
            var bandRight = right;
            var nextBottom = float.PositiveInfinity;

            for (var i = 0; i < _floats.Count; i++)
            {
                var placement = _floats[i];
                var box = placement.MarginBox;

                if (y < box.Y || y >= box.Bottom)
                {
                    continue;
                }

                if (placement.Side == Float.Left)
                {
                    bandLeft = MathF.Max(bandLeft, box.Right);
                }
                else
                {
                    bandRight = MathF.Min(bandRight, box.X);
                }

                if (bandLeft >= bandRight - 0.01f)
                {
                    nextBottom = MathF.Min(nextBottom, box.Bottom);
                }
            }

            if (bandLeft < bandRight - 0.01f)
            {
                return new FloatBand(y, bandLeft, bandRight);
            }

            if (!float.IsFinite(nextBottom) || nextBottom <= y + 0.001f)
            {
                return new FloatBand(y, bandLeft, bandRight);
            }

            y = nextBottom;
        }

        return new FloatBand(y, left, right);
    }

    /// <summary>
    /// Advances <paramref name="y"/> past every float on the cleared side that
    /// horizontally overlaps the content range.
    /// </summary>
    /// <param name="clear">Sides to clear, read as a <see cref="Clear"/> bit mask.</param>
    /// <param name="y">Candidate absolute y position, in CSS pixels.</param>
    /// <param name="left">Left edge of the content range, in CSS pixels.</param>
    /// <param name="right">Right edge of the content range, in CSS pixels.</param>
    /// <returns>
    /// The largest of <paramref name="y"/> and the bottom edges of all matching
    /// floats, or <paramref name="y"/> unchanged when nothing matches.
    /// </returns>
    /// <remarks>
    /// <para>
    /// A float matches when its side is selected by <paramref name="clear"/> and
    /// its margin box horizontally overlaps the open range. Floats that merely
    /// touch an edge (<c>box.Right == left</c> or <c>box.X == right</c>) are not
    /// considered overlapping.
    /// </para>
    /// <para>
    /// The result never moves backwards: floats whose bottoms are already above
    /// the candidate y leave it unchanged. <see cref="Clear.None"/> returns the
    /// input immediately. The scan is linear and allocation-free.
    /// </para>
    /// </remarks>
    public float ClearanceY(Clear clear, float y, float left, float right)
    {
        if (clear == Clear.None || _floats.Count == 0)
        {
            return y;
        }

        for (var i = 0; i < _floats.Count; i++)
        {
            var placement = _floats[i];
            var box = placement.MarginBox;

            if (box.Right <= left || box.X >= right)
            {
                continue;
            }

            var matches = placement.Side switch
            {
                Float.Left => (clear & Clear.Left) != 0,
                Float.Right => (clear & Clear.Right) != 0,
                _ => false
            };

            if (matches)
            {
                y = MathF.Max(y, box.Bottom);
            }
        }

        return y;
    }
}
