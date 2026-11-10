using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>Text style packed into the constant pool so glyph runs can be batched.</summary>
public readonly record struct TextStyle(
    string FontFamily,
    float FontSize,
    int FontWeight,
    bool Italic,
    uint Color,
    float LetterSpacing,
    float LineHeight,
    TextAlign Align,
    TextDecoration Decoration,
    WhiteSpace WhiteSpace,
    Direction Direction)
{
    public static readonly TextStyle Default = new(
        "sans-serif",
        16,
        400,
        false,
        0xFF000000,
        0,
        1.2f,
        TextAlign.Start,
        TextDecoration.None,
        WhiteSpace.Normal,
        Direction.LeftToRight);
}

public readonly record struct TextCommand(string Text, TextStyle Style);

public readonly record struct BorderSide(float Width, BorderStyle Style, uint Color)
{
    public static readonly BorderSide None = new(0, BorderStyle.None, 0);
}

public readonly record struct BorderSpec(
    BorderSide Top,
    BorderSide Right,
    BorderSide Bottom,
    BorderSide Left,
    CornerRadius Radius)
{
    public bool IsEmpty =>
        (Top.Width <= 0 || Top.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Right.Width <= 0 || Right.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Bottom.Width <= 0 || Bottom.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Left.Width <= 0 || Left.Style is BorderStyle.None or BorderStyle.Hidden);
}

public readonly record struct ShadowSpec(
    float OffsetX,
    float OffsetY,
    float Blur,
    float Spread,
    uint Color,
    bool Inset);

/// <summary>
/// One function in a <c>filter</c>/<c>backdrop-filter</c> list. <see cref="Amount"/>
/// is the resolved numeric argument (pixels for blur, a multiplier for the
/// color adjustments, degrees for hue rotation). <see cref="Shadow"/> is only
/// meaningful for <see cref="FilterFunctionKind.DropShadow"/>.
/// </summary>
public readonly record struct FilterFunction(
    FilterFunctionKind Kind,
    float Amount,
    ShadowSpec Shadow = default,
    string? Url = null);

/// <summary>A parsed filter chain. The renderer applies the functions in order.</summary>
public sealed class FilterSpec
{
    public static readonly FilterSpec None = new([]);

    public FilterSpec(FilterFunction[] functions)
    {
        Functions = functions;
    }

    public FilterFunction[] Functions { get; }

    public bool IsNone => Functions.Length == 0;
}

public readonly record struct ClipPathPoint(CssLength X, CssLength Y);

/// <summary>
/// A parsed <c>clip-path</c>. Percentages and relative lengths stay unresolved
/// so the renderer can resolve them against the element's border box, which is
/// carried on the <c>PushClipPath</c> instruction.
/// </summary>
public sealed class ClipPathSpec
{
    public static readonly ClipPathSpec None = new() { Kind = ClipPathKind.None };

    public ClipPathKind Kind { get; init; }
    public CssLength InsetTop { get; init; } = CssLength.Zero;
    public CssLength InsetRight { get; init; } = CssLength.Zero;
    public CssLength InsetBottom { get; init; } = CssLength.Zero;
    public CssLength InsetLeft { get; init; } = CssLength.Zero;
    public CssLength RadiusX { get; init; } = CssLength.Percent(50);
    public CssLength RadiusY { get; init; } = CssLength.Percent(50);
    public CssLength CenterX { get; init; } = CssLength.Percent(50);
    public CssLength CenterY { get; init; } = CssLength.Percent(50);
    public ClipPathPoint[]? Points { get; init; }
    public string? Url { get; init; }

    public bool IsNone => Kind == ClipPathKind.None;
}

/// <summary>A parsed <c>mask</c> shorthand. The renderer rasterises the source.</summary>
public sealed class MaskSpec
{
    public static readonly MaskSpec None = new() { Kind = ImageKind.None };

    public ImageKind Kind { get; init; }
    public string? Source { get; init; }
    public MaskMode Mode { get; init; } = MaskMode.MatchSource;
    public BackgroundRepeat Repeat { get; init; } = BackgroundRepeat.Repeat;

    public bool IsNone => Kind == ImageKind.None;
}

public readonly record struct GradientStop(float Offset, uint Color);

/// <summary>A parsed background layer the renderer can consume without re-tokenising CSS.</summary>
public sealed class BackgroundImageSpec
{
    public ImageKind Kind { get; init; }
    public string? Url { get; init; }
    public GradientKind GradientKind { get; init; }
    public float Angle { get; init; }
    public Point Center { get; init; }
    public Size Radius { get; init; }
    public GradientStop[] Stops { get; init; } = [];
    public BackgroundRepeat Repeat { get; init; }
    public BackgroundBox Clip { get; init; }
    public BackgroundBox Origin { get; init; }
    public bool Fixed { get; init; }

    public static readonly BackgroundImageSpec None = new() { Kind = ImageKind.None };
}

public readonly record struct ImageSpec(
    string Source,
    ObjectFit Fit,
    float Opacity,
    uint Tint);

public readonly record struct ScrollbarSpec(
    bool Vertical,
    float ThumbOffset,
    float ThumbLength,
    float TrackLength,
    uint TrackColor,
    uint ThumbColor,
    float Width);

/// <summary>
/// Owning storage for everything an instruction cannot fit inline. Constants
/// are interned: emitting the same text style, gradient or border spec twice
/// returns the same id, which makes cross-node batching cheap.
/// </summary>
public sealed class ConstantPool
{
    private readonly List<object> _items = [];
    private readonly Dictionary<object, int> _index = [];
    private readonly Dictionary<TextCommand, int> _texts = [];
    private readonly Dictionary<CornerRadius, int> _radii = [];
    private readonly Dictionary<Matrix3x2, int> _transforms = [];
    private readonly Dictionary<FilterSpec, int> _filters = [];
    private readonly Dictionary<ClipPathSpec, int> _clipPaths = [];
    private readonly Dictionary<MaskSpec, int> _masks = [];

    public int Count => _items.Count;

    public object Get(int id) => _items[id];

    public TextCommand GetText(int id) => (TextCommand)_items[id];

    public BorderSpec GetBorder(int id) => (BorderSpec)_items[id];

    public ShadowSpec GetShadow(int id) => (ShadowSpec)_items[id];

    public BackgroundImageSpec GetBackground(int id) => (BackgroundImageSpec)_items[id];

    public ImageSpec GetImage(int id) => (ImageSpec)_items[id];

    public ScrollbarSpec GetScrollbar(int id) => (ScrollbarSpec)_items[id];

    public CornerRadius GetRadius(int id) => (CornerRadius)_items[id];

    public Matrix3x2 GetTransform(int id) => (Matrix3x2)_items[id];

    public FilterSpec GetFilter(int id) => (FilterSpec)_items[id];

    public ClipPathSpec GetClipPath(int id) => (ClipPathSpec)_items[id];

    public MaskSpec GetMask(int id) => (MaskSpec)_items[id];

    public int AddText(string text, TextStyle style)
    {
        var command = new TextCommand(text, style);

        if (_texts.TryGetValue(command, out var existing))
        {
            return existing;
        }

        var id = AddCore(command);
        _texts[command] = id;
        return id;
    }

    public int AddBorder(BorderSpec border) => AddCore(border);

    public int AddShadow(ShadowSpec shadow) => AddCore(shadow);

    public int AddBackground(BackgroundImageSpec background) => AddCore(background);

    public int AddImage(ImageSpec image) => AddCore(image);

    public int AddScrollbar(ScrollbarSpec scrollbar) => AddCore(scrollbar);

    public int AddRadius(CornerRadius radius)
    {
        if (_radii.TryGetValue(radius, out var existing))
        {
            return existing;
        }

        var id = AddCore(radius);
        _radii[radius] = id;
        return id;
    }

    public int AddTransform(Matrix3x2 transform)
    {
        if (_transforms.TryGetValue(transform, out var existing))
        {
            return existing;
        }

        var id = AddCore(transform);
        _transforms[transform] = id;
        return id;
    }

    public int AddFilter(FilterSpec filter)
    {
        if (_filters.TryGetValue(filter, out var existing))
        {
            return existing;
        }

        var id = AddCore(filter);
        _filters[filter] = id;
        return id;
    }

    public int AddClipPath(ClipPathSpec clipPath)
    {
        if (_clipPaths.TryGetValue(clipPath, out var existing))
        {
            return existing;
        }

        var id = AddCore(clipPath);
        _clipPaths[clipPath] = id;
        return id;
    }

    public int AddMask(MaskSpec mask)
    {
        if (_masks.TryGetValue(mask, out var existing))
        {
            return existing;
        }

        var id = AddCore(mask);
        _masks[mask] = id;
        return id;
    }

    private int AddCore(object value)
    {
        if (_index.TryGetValue(value, out var existing))
        {
            return existing;
        }

        var id = _items.Count;

        if (id > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "The constant pool exceeds the 16-bit aux index range.");
        }

        _items.Add(value);
        _index[value] = id;
        return id;
    }
}
