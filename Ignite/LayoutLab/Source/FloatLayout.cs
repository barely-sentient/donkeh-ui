namespace Ignite.LayoutLab;

/// <summary>A float's margin box plus the side it was placed on.</summary>
internal readonly record struct FloatPlacement(Rect MarginBox, Float Side);

/// <summary>The horizontal band available at a given vertical position.</summary>
internal readonly record struct FloatBand(float Y, float Left, float Right)
{
    public float Width => MathF.Max(0, Right - Left);

    public bool IsEmpty => Width <= 0.01f;
}

/// <summary>
/// The floats active in one block formatting context. Floats are stored in
/// absolute layout coordinates, so a block that does not establish a new BFC can
/// share the context with its ancestor and its line boxes will avoid the same
/// floats. Establishing boxes (the root, <c>flow-root</c>, inline-blocks, scroll
/// containers, flex/grid containers, and floats themselves) get a fresh context.
/// </summary>
internal sealed class FloatContext
{
    private readonly List<FloatPlacement> _floats = [];

    public int Count => _floats.Count;

    public void Add(Rect marginBox, Float side)
    {
        if (marginBox.Width <= 0 || marginBox.Height <= 0)
        {
            return;
        }

        _floats.Add(new FloatPlacement(marginBox, side));
    }

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
    /// band is wide enough to non-empty. When floats cover the full width the
    /// scan advances to the next float bottom.
    /// </summary>
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
