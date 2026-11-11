namespace Ignite.LayoutLab;

/// <summary>A point in layout space, measured in CSS pixels from the viewport origin.</summary>
/// <param name="X">Horizontal offset in CSS pixels, increasing to the right.</param>
/// <param name="Y">Vertical offset in CSS pixels, increasing downwards.</param>
/// <remarks>
/// Positions produced by the layout engine are absolute rather than parent
/// relative, so a child point is already resolved to viewport space. This is a
/// small value type and is safe to copy between layout and paint stages.
/// </remarks>
public readonly record struct Point(float X, float Y)
{
    /// <summary>The origin, <c>(0, 0)</c>.</summary>
    public static readonly Point Zero = new(0, 0);

    /// <summary>Returns this point moved by the given offsets.</summary>
    /// <param name="dx">Horizontal movement in CSS pixels.</param>
    /// <param name="dy">Vertical movement in CSS pixels.</param>
    /// <returns>A new <see cref="Point"/> at <c>(X + dx, Y + dy)</c>.</returns>
    public Point Translate(float dx, float dy) => new(X + dx, Y + dy);
}

/// <summary>A width/height pair. Never expected to be negative.</summary>
/// <param name="Width">Width in CSS pixels.</param>
/// <param name="Height">Height in CSS pixels.</param>
/// <remarks>
/// <para>
/// The type does not validate or normalise its arguments, so callers are
/// responsible for keeping both dimensions non-negative. Use
/// <see cref="Clamp(float, float, float, float)"/> to enforce bounds.
/// </para>
/// <para>
/// <see cref="Unbounded"/> uses <see cref="float.PositiveInfinity"/> on both
/// axes to represent an unconstrained available space during measurement.
/// </para>
/// </remarks>
public readonly record struct Size(float Width, float Height)
{
    /// <summary>A zero size, <c>(0, 0)</c>.</summary>
    public static readonly Size Zero = new(0, 0);

    /// <summary>
    /// An unbounded size, with both dimensions set to
    /// <see cref="float.PositiveInfinity"/>.
    /// </summary>
    public static readonly Size Unbounded = new(float.PositiveInfinity, float.PositiveInfinity);

    /// <summary>Gets a value indicating whether either dimension is zero or negative.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Returns this size clamped to the supplied per-axis bounds.</summary>
    /// <param name="minWidth">Inclusive lower bound for <see cref="Width"/>, in CSS pixels.</param>
    /// <param name="minHeight">Inclusive lower bound for <see cref="Height"/>, in CSS pixels.</param>
    /// <param name="maxWidth">Inclusive upper bound for <see cref="Width"/>, in CSS pixels.</param>
    /// <param name="maxHeight">Inclusive upper bound for <see cref="Height"/>, in CSS pixels.</param>
    /// <returns>
    /// A new <see cref="Size"/> with each axis clamped with
    /// <see cref="Math.Clamp(float, float, float)"/>.
    /// </returns>
    public Size Clamp(float minWidth, float minHeight, float maxWidth, float maxHeight)
    {
        return new Size(
            Math.Clamp(Width, minWidth, maxWidth),
            Math.Clamp(Height, minHeight, maxHeight));
    }
}

/// <summary>
/// An axis-aligned rectangle. <see cref="Width"/> and <see cref="Height"/> are
/// expected to be non-negative; the constructor does not normalise them so the
/// zero-allocation hot paths stay branch-light.
/// </summary>
/// <param name="X">Left edge, in CSS pixels from the viewport origin.</param>
/// <param name="Y">Top edge, in CSS pixels from the viewport origin.</param>
/// <param name="Width">Width in CSS pixels; expected to be non-negative.</param>
/// <param name="Height">Height in CSS pixels; expected to be non-negative.</param>
/// <remarks>
/// <para>
/// Rectangles use top-left origin and y-down coordinates, matching CSS layout.
/// Equality and deconstruction come from the generated record struct members.
/// </para>
/// <para>
/// An empty rectangle (see <see cref="IsEmpty"/>) has zero or negative
/// extent. Operations such as <see cref="Union(in Rect)"/> treat empty
/// rectangles as absent, which makes them safe to use as an accumulator
/// seed.
/// </para>
/// </remarks>
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    /// <summary>An empty rectangle at the origin, <c>(0, 0, 0, 0)</c>.</summary>
    public static readonly Rect Empty = new(0, 0, 0, 0);

    /// <summary>Gets the x coordinate of the right edge (<see cref="X"/> + <see cref="Width"/>).</summary>
    public float Right => X + Width;

    /// <summary>Gets the y coordinate of the bottom edge (<see cref="Y"/> + <see cref="Height"/>).</summary>
    public float Bottom => Y + Height;

    /// <summary>Gets the top-left corner as a <see cref="Point"/>.</summary>
    public Point Origin => new(X, Y);

    /// <summary>Gets the dimensions as a <see cref="Size"/>.</summary>
    public Size Size => new(Width, Height);

    /// <summary>Gets a value indicating whether either extent is zero or negative.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>
    /// Gets a value indicating whether a point lies inside or on the boundary of
    /// this rectangle.
    /// </summary>
    /// <param name="x">Horizontal coordinate to test, in CSS pixels.</param>
    /// <param name="y">Vertical coordinate to test, in CSS pixels.</param>
    /// <returns><see langword="true"/> when the point is within the closed bounds.</returns>
    /// <remarks>Both edges are inclusive, so points exactly on the border count as inside.</remarks>
    public bool Contains(float x, float y)
    {
        return x >= X && x <= Right && y >= Y && y <= Bottom;
    }

    /// <summary>Gets a value indicating whether this rectangle overlaps another.</summary>
    /// <param name="other">The rectangle to test, passed by read-only reference.</param>
    /// <returns><see langword="true"/> when the interiors overlap.</returns>
    /// <remarks>
    /// The comparison is strict, so rectangles that merely share an edge or
    /// corner do not intersect. This differs deliberately from
    /// <see cref="Contains(float, float)"/>, which includes its boundary.
    /// </remarks>
    public bool Intersects(in Rect other)
    {
        return X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    }

    /// <summary>Computes the overlap of this rectangle with another.</summary>
    /// <param name="other">The rectangle to intersect with, passed by read-only reference.</param>
    /// <returns>
    /// The overlapping region, or <see cref="Empty"/> when the rectangles are
    /// disjoint or only touch.
    /// </returns>
    public Rect Intersect(in Rect other)
    {
        var x = MathF.Max(X, other.X);
        var y = MathF.Max(Y, other.Y);
        var right = MathF.Min(Right, other.Right);
        var bottom = MathF.Min(Bottom, other.Bottom);

        return right <= x || bottom <= y
            ? Empty
            : new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>Computes the smallest rectangle containing both operands.</summary>
    /// <param name="other">The rectangle to include, passed by read-only reference.</param>
    /// <returns>
    /// The bounding union. If either operand is empty, the other is returned
    /// unchanged; if both are empty the result is empty.
    /// </returns>
    /// <remarks>
    /// Empty operands are ignored rather than treated as a point at the origin,
    /// which is what makes this suitable for accumulating paint bounds.
    /// </remarks>
    public Rect Union(in Rect other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        var x = MathF.Min(X, other.X);
        var y = MathF.Min(Y, other.Y);
        var right = MathF.Max(Right, other.Right);
        var bottom = MathF.Max(Bottom, other.Bottom);

        return new Rect(x, y, right - x, bottom - y);
    }

    /// <summary>Returns this rectangle moved by the given offsets.</summary>
    /// <param name="dx">Horizontal movement in CSS pixels.</param>
    /// <param name="dy">Vertical movement in CSS pixels.</param>
    /// <returns>A new rectangle with the same size at the translated position.</returns>
    public Rect Translate(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>Returns this rectangle grown by the same amount on every side.</summary>
    /// <param name="amount">Amount to add to each edge, in CSS pixels; may be negative to shrink.</param>
    /// <returns>A new rectangle expanded by <c>amount</c> horizontally and vertically.</returns>
    public Rect Inflate(float amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    /// <summary>Returns this rectangle grown by per-side amounts.</summary>
    /// <param name="top">Amount to extend the top edge by, in CSS pixels.</param>
    /// <param name="right">Amount to extend the right edge by, in CSS pixels.</param>
    /// <param name="bottom">Amount to extend the bottom edge by, in CSS pixels.</param>
    /// <param name="left">Amount to extend the left edge by, in CSS pixels.</param>
    /// <returns>A new rectangle with each edge moved outwards by its amount.</returns>
    /// <remarks>
    /// The parameter order matches the CSS <c>margin</c>/<c>padding</c>
    /// shorthand (top, right, bottom, left), not clockwise from the top-left
    /// corner.
    /// </remarks>
    public Rect Inflate(float top, float right, float bottom, float left)
        => new(X - left, Y - top, Width + left + right, Height + top + bottom);

    /// <summary>Returns this rectangle shrunk by per-side amounts.</summary>
    /// <param name="top">Amount to inset the top edge by, in CSS pixels.</param>
    /// <param name="right">Amount to inset the right edge by, in CSS pixels.</param>
    /// <param name="bottom">Amount to inset the bottom edge by, in CSS pixels.</param>
    /// <param name="left">Amount to inset the left edge by, in CSS pixels.</param>
    /// <returns>A new rectangle with each edge moved inwards by its amount.</returns>
    /// <remarks>
    /// This is the exact inverse of
    /// <see cref="Inflate(float, float, float, float)"/> and is typically used
    /// to convert a border box into a padding or content box. Resulting
    /// negative extents are not clamped.
    /// </remarks>
    public Rect Deflate(float top, float right, float bottom, float left)
        => new(X + left, Y + top, Width - left - right, Height - top - bottom);

    /// <summary>Returns this rectangle shrunk by the per-side values of a thickness.</summary>
    /// <param name="thickness">Insets to apply, in CSS pixels.</param>
    /// <returns>A new rectangle inset on each side.</returns>
    public Rect Deflate(in Thickness thickness)
        => Deflate(thickness.Top, thickness.Right, thickness.Bottom, thickness.Left);

    /// <summary>Returns this rectangle's position with a new size.</summary>
    /// <param name="width">New width in CSS pixels.</param>
    /// <param name="height">New height in CSS pixels.</param>
    /// <returns>A new rectangle at the same origin with the supplied dimensions.</returns>
    public Rect WithSize(float width, float height) => new(X, Y, width, height);
}

/// <summary>Top/right/bottom/left values, for margins, padding and borders.</summary>
/// <param name="Top">Top edge value, in CSS pixels.</param>
/// <param name="Right">Right edge value, in CSS pixels.</param>
/// <param name="Bottom">Bottom edge value, in CSS pixels.</param>
/// <param name="Left">Left edge value, in CSS pixels.</param>
/// <remarks>
/// Values are stored unsigned in the sense that they are expected to be
/// non-negative after resolution; negatives are not validated here because
/// <c>margin</c> can legitimately resolve negative. The component order is the
/// CSS shorthand order (top, right, bottom, left).
/// </remarks>
public readonly record struct Thickness(float Top, float Right, float Bottom, float Left)
{
    /// <summary>A zero thickness on all four sides.</summary>
    public static readonly Thickness Zero = new(0, 0, 0, 0);

    /// <summary>Gets the combined left and right value.</summary>
    public float Horizontal => Left + Right;

    /// <summary>Gets the combined top and bottom value.</summary>
    public float Vertical => Top + Bottom;

    /// <summary>Creates a thickness with the same value on all four sides.</summary>
    /// <param name="value">Value to apply to each side, in CSS pixels.</param>
    /// <returns>A uniform <see cref="Thickness"/>.</returns>
    public static Thickness All(float value) => new(value, value, value, value);

    /// <summary>Creates a thickness from symmetric axis values.</summary>
    /// <param name="vertical">Value for the top and bottom edges, in CSS pixels.</param>
    /// <param name="horizontal">Value for the left and right edges, in CSS pixels.</param>
    /// <returns>A <see cref="Thickness"/> mirroring the two-axis CSS shorthand.</returns>
    public static Thickness Symmetric(float vertical, float horizontal)
        => new(vertical, horizontal, vertical, horizontal);
}

/// <summary>Four corner radii, in CSS order.</summary>
/// <param name="TopLeft">Radius of the top-left corner, in CSS pixels.</param>
/// <param name="TopRight">Radius of the top-right corner, in CSS pixels.</param>
/// <param name="BottomRight">Radius of the bottom-right corner, in CSS pixels.</param>
/// <param name="BottomLeft">Radius of the bottom-left corner, in CSS pixels.</param>
/// <remarks>
/// The order starts at the top-left and proceeds clockwise, matching the
/// <c>border-radius</c> shorthand. The paint stage clamps overlapping radii
/// when it builds the rounded-rectangle outline.
/// </remarks>
public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    /// <summary>Zero radius on every corner, producing a square box.</summary>
    public static readonly CornerRadius Zero = new(0, 0, 0, 0);

    /// <summary>Gets a value indicating whether all four radii are zero.</summary>
    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    /// <summary>Creates a corner radius with the same value on all four corners.</summary>
    /// <param name="value">Radius to apply to each corner, in CSS pixels.</param>
    /// <returns>A uniform <see cref="CornerRadius"/>.</returns>
    public static CornerRadius All(float value) => new(value, value, value, value);
}
