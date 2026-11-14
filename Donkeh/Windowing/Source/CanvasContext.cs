using System.Numerics;
using Donkeh.LayoutLab;

namespace Donkeh.Windowing;

/// <summary>
/// Records canvas drawing into the window's indirect render batch instead of
/// issuing immediate-mode OpenGL calls.
/// </summary>
/// <remarks>
/// <para>
/// A <see cref="CanvasContext"/> is supplied to
/// <see cref="CanvasProps.OnDraw"/>. Every primitive it receives is appended to
/// the same GPU instance buffer as the rest of the UI, so the canvas does not
/// force a viewport/scissor switch, a buffer upload or a separate draw call per
/// shape. The host runs the batched canvas pass before the frame flush and the
/// raw <see cref="CanvasProps.OnContext"/> pass afterwards.
/// </para>
/// <para>
/// Coordinates are canvas-local CSS pixels with the origin at the top-left of
/// the element's border box. Push/pop pairs are balanced by the host even when
/// a handler throws, but callers should still pop what they push so sibling
/// drawing inherits the expected state.
/// </para>
/// <para>
/// The context is reused between frames and is only valid inside the callback.
/// Storing it and drawing later throws <see cref="InvalidOperationException"/>.
/// </para>
/// </remarks>
public sealed class CanvasContext
{
    /// <summary>Saved canvas-local transforms for nested pushes.</summary>
    private readonly Stack<Matrix3x2> _transforms = new();

    /// <summary>Saved canvas-local opacities for nested pushes.</summary>
    private readonly Stack<float> _opacities = new();

    /// <summary>The renderer currently receiving geometry, or <see langword="null"/>.</summary>
    private GLRenderer? _renderer;

    /// <summary>The canvas border box in absolute CSS pixels.</summary>
    private Rect _bounds;

    /// <summary>Translation that maps canvas-local coordinates to absolute pixels.</summary>
    private Matrix3x2 _translation = Matrix3x2.Identity;

    /// <summary>Accumulated canvas-local transform.</summary>
    private Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>The renderer transform to restore when recording ends.</summary>
    private Matrix3x2 _savedTransform = Matrix3x2.Identity;

    /// <summary>Number of user clip pushes not yet popped.</summary>
    private int _clipDepth;

    /// <summary>Number of user transform pushes not yet popped.</summary>
    private int _transformDepth;

    /// <summary>Number of user opacity pushes not yet popped.</summary>
    private int _opacityDepth;

    /// <summary>Starts recording for one canvas element.</summary>
    /// <param name="renderer">The renderer that will submit the frame.</param>
    /// <param name="bounds">The canvas border box in absolute CSS pixels.</param>
    internal void Begin(GLRenderer renderer, in Rect bounds)
    {
        _renderer = renderer;
        _bounds = bounds;
        _translation = Matrix3x2.CreateTranslation(bounds.X, bounds.Y);
        _transform = Matrix3x2.Identity;
        _transforms.Clear();
        _opacities.Clear();
        _clipDepth = 0;
        _transformDepth = 0;
        _opacityDepth = 0;

        _savedTransform = renderer.SetCanvasTransform(_translation);
        renderer.PushCanvasClip(bounds);
    }

    /// <summary>Stops recording and balances any pushes the caller left open.</summary>
    internal void End()
    {
        if (_renderer is not { } renderer)
        {
            return;
        }

        for (; _opacityDepth > 0; _opacityDepth--)
        {
            renderer.PopCanvasOpacity();
        }

        for (; _transformDepth > 0; _transformDepth--)
        {
            _transform = _transforms.Count > 0 ? _transforms.Pop() : Matrix3x2.Identity;
            renderer.SetCanvasTransform(Matrix3x2.Multiply(_transform, _translation));
        }

        for (; _clipDepth > 0; _clipDepth--)
        {
            renderer.PopCanvasClip();
        }

        renderer.PopCanvasClip();
        renderer.SetCanvasTransform(_savedTransform);
        _renderer = null;
    }

    /// <summary>Fills the whole canvas with one colour.</summary>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    public void Clear(uint color) =>
        FillRect(new Rect(0, 0, _bounds.Width, _bounds.Height), color);

    /// <summary>Fills an axis-aligned rectangle.</summary>
    /// <param name="rect">Rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    public void FillRect(in Rect rect, uint color) => Active.AddCanvasRect(rect, color);

    /// <summary>Fills an axis-aligned rectangle.</summary>
    /// <param name="x">Left edge in canvas-local CSS pixels.</param>
    /// <param name="y">Top edge in canvas-local CSS pixels.</param>
    /// <param name="width">Rectangle width.</param>
    /// <param name="height">Rectangle height.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    public void FillRect(float x, float y, float width, float height, uint color) =>
        FillRect(new Rect(x, y, width, height), color);

    /// <summary>Fills a rectangle with per-corner rounding.</summary>
    /// <param name="rect">Rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <param name="radius">Corner radii in CSS pixels.</param>
    public void FillRoundedRect(in Rect rect, uint color, CornerRadius radius) =>
        Active.AddCanvasRoundedRect(rect, color, radius);

    /// <summary>Fills an ellipse inscribed in a rectangle.</summary>
    /// <param name="rect">Bounding rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    public void FillEllipse(in Rect rect, uint color) => Active.AddCanvasEllipse(rect, color);

    /// <summary>Strokes a rectangular outline as four filled edges.</summary>
    /// <param name="rect">Rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Stroke colour as packed <c>0xAARRGGBB</c>.</param>
    /// <param name="width">Stroke width in CSS pixels.</param>
    public void StrokeRect(in Rect rect, uint color, float width)
    {
        if (rect.IsEmpty || width <= 0 || (color >> 24) == 0)
        {
            return;
        }

        var w = MathF.Min(width, MathF.Min(rect.Width, rect.Height) * 0.5f);
        var inner = new Rect(rect.X + w, rect.Y + w, rect.Width - w * 2, rect.Height - w * 2);
        FillRect(new Rect(rect.X, rect.Y, rect.Width, w), color);
        FillRect(new Rect(rect.X, rect.Bottom - w, rect.Width, w), color);
        FillRect(new Rect(rect.X, inner.Y, w, inner.Height), color);
        FillRect(new Rect(rect.Right - w, inner.Y, w, inner.Height), color);
    }

    /// <summary>Draws a single run of text.</summary>
    /// <param name="rect">Text box in canvas-local CSS pixels.</param>
    /// <param name="text">The string to draw.</param>
    /// <param name="style">The resolved text style.</param>
    public void DrawText(in Rect rect, string text, in TextStyle style)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        Active.AddCanvasText(rect, new TextCommand(text, style));
    }

    /// <summary>Draws a cached image with an object-fit rule.</summary>
    /// <param name="rect">Destination box in canvas-local CSS pixels.</param>
    /// <param name="source">Image source path or packed asset name.</param>
    /// <param name="fit">How the image is fitted into the box.</param>
    /// <param name="tint">Straight tint as packed <c>0xAARRGGBB</c>; white leaves it unchanged.</param>
    /// <param name="opacity">Image opacity multiplier.</param>
    public void DrawImage(
        in Rect rect,
        string source,
        ObjectFit fit = ObjectFit.Fill,
        uint tint = 0xFFFFFFFF,
        float opacity = 1) =>
        Active.AddCanvasImage(new ImageSpec(source, fit, opacity, tint), rect);

    /// <summary>Pushes a canvas-local rectangular clip.</summary>
    /// <param name="rect">Clip rectangle in canvas-local CSS pixels.</param>
    /// <remarks>
    /// The rectangle is transformed by the current canvas transform and reduced
    /// to its axis-aligned bounds, matching the renderer's rectangular clip.
    /// </remarks>
    public void PushClip(in Rect rect)
    {
        var renderer = Active;
        var matrix = Matrix3x2.Multiply(_transform, _translation);

        var topLeft = Vector2.Transform(new Vector2(rect.X, rect.Y), matrix);
        var topRight = Vector2.Transform(new Vector2(rect.Right, rect.Y), matrix);
        var bottomLeft = Vector2.Transform(new Vector2(rect.X, rect.Bottom), matrix);
        var bottomRight = Vector2.Transform(new Vector2(rect.Right, rect.Bottom), matrix);
        var minX = MathF.Min(MathF.Min(topLeft.X, topRight.X), MathF.Min(bottomLeft.X, bottomRight.X));
        var minY = MathF.Min(MathF.Min(topLeft.Y, topRight.Y), MathF.Min(bottomLeft.Y, bottomRight.Y));
        var maxX = MathF.Max(MathF.Max(topLeft.X, topRight.X), MathF.Max(bottomLeft.X, bottomRight.X));
        var maxY = MathF.Max(MathF.Max(topLeft.Y, topRight.Y), MathF.Max(bottomLeft.Y, bottomRight.Y));

        renderer.PushCanvasClip(new Rect(minX, minY, maxX - minX, maxY - minY));
        _clipDepth++;
    }

    /// <summary>Pops the most recent <see cref="PushClip"/>.</summary>
    public void PopClip()
    {
        if (_clipDepth <= 0)
        {
            return;
        }

        Active.PopCanvasClip();
        _clipDepth--;
    }

    /// <summary>Pushes an additional canvas-local transform.</summary>
    /// <param name="transform">Transform applied after the current canvas transform.</param>
    public void PushTransform(Matrix3x2 transform)
    {
        _transforms.Push(_transform);
        _transform = Matrix3x2.Multiply(_transform, transform);
        Active.SetCanvasTransform(Matrix3x2.Multiply(_transform, _translation));
        _transformDepth++;
    }

    /// <summary>Pops the most recent <see cref="PushTransform"/>.</summary>
    public void PopTransform()
    {
        if (_transformDepth <= 0)
        {
            return;
        }

        _transform = _transforms.Count > 0 ? _transforms.Pop() : Matrix3x2.Identity;
        Active.SetCanvasTransform(Matrix3x2.Multiply(_transform, _translation));
        _transformDepth--;
    }

    /// <summary>Pushes an opacity multiplier for subsequent canvas primitives.</summary>
    /// <param name="opacity">Multiplier clamped to the 0..1 range.</param>
    public void PushOpacity(float opacity)
    {
        _opacities.Push(opacity);
        Active.PushCanvasOpacity(opacity);
        _opacityDepth++;
    }

    /// <summary>Pops the most recent <see cref="PushOpacity"/>.</summary>
    public void PopOpacity()
    {
        if (_opacityDepth <= 0)
        {
            return;
        }

        _opacities.Pop();
        Active.PopCanvasOpacity();
        _opacityDepth--;
    }

    /// <summary>Gets the active renderer or throws when recording has ended.</summary>
    private GLRenderer Active => _renderer
        ?? throw new InvalidOperationException(
            "CanvasContext can only be used inside the OnDraw callback.");
}
