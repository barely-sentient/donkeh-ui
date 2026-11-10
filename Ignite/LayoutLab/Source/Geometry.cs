namespace Ignite.LayoutLab;

/// <summary>A point in layout space, measured in CSS pixels from the viewport origin.</summary>
public readonly record struct Point(float X, float Y)
{
    public static readonly Point Zero = new(0, 0);

    public Point Translate(float dx, float dy) => new(X + dx, Y + dy);
}

/// <summary>A width/height pair. Never expected to be negative.</summary>
public readonly record struct Size(float Width, float Height)
{
    public static readonly Size Zero = new(0, 0);
    public static readonly Size Unbounded = new(float.PositiveInfinity, float.PositiveInfinity);

    public bool IsEmpty => Width <= 0 || Height <= 0;

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
public readonly record struct Rect(float X, float Y, float Width, float Height)
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public float Right => X + Width;
    public float Bottom => Y + Height;
    public Point Origin => new(X, Y);
    public Size Size => new(Width, Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public bool Contains(float x, float y)
    {
        return x >= X && x <= Right && y >= Y && y <= Bottom;
    }

    public bool Intersects(in Rect other)
    {
        return X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
    }

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

    public Rect Translate(float dx, float dy) => new(X + dx, Y + dy, Width, Height);

    public Rect Inflate(float amount) => new(X - amount, Y - amount, Width + amount * 2, Height + amount * 2);

    public Rect Inflate(float top, float right, float bottom, float left)
        => new(X - left, Y - top, Width + left + right, Height + top + bottom);

    public Rect Deflate(float top, float right, float bottom, float left)
        => new(X + left, Y + top, Width - left - right, Height - top - bottom);

    public Rect Deflate(in Thickness thickness)
        => Deflate(thickness.Top, thickness.Right, thickness.Bottom, thickness.Left);

    public Rect WithSize(float width, float height) => new(X, Y, width, height);
}

/// <summary>Top/right/bottom/left values, for margins, padding and borders.</summary>
public readonly record struct Thickness(float Top, float Right, float Bottom, float Left)
{
    public static readonly Thickness Zero = new(0, 0, 0, 0);

    public float Horizontal => Left + Right;
    public float Vertical => Top + Bottom;

    public static Thickness All(float value) => new(value, value, value, value);

    public static Thickness Symmetric(float vertical, float horizontal)
        => new(vertical, horizontal, vertical, horizontal);
}

/// <summary>Four corner radii, in CSS order.</summary>
public readonly record struct CornerRadius(float TopLeft, float TopRight, float BottomRight, float BottomLeft)
{
    public static readonly CornerRadius Zero = new(0, 0, 0, 0);

    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    public static CornerRadius All(float value) => new(value, value, value, value);
}
