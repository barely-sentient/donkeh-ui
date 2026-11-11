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
    private enum ConstantKind : byte
    {
        Text,
        Border,
        Shadow,
        Background,
        Image,
        Scrollbar,
        Radius,
        Transform,
        Filter,
        ClipPath,
        Mask
    }

    private readonly record struct ConstantRef(ConstantKind Kind, int Index);

    // Constants live in typed lists. Storing them as object boxes every
    // struct constant on every frame, which was one of the largest remaining
    // per-layout allocations.
    private readonly List<ConstantRef> _items = [];
    private readonly List<TextCommand> _textItems = [];
    private readonly List<BorderSpec> _borderItems = [];
    private readonly List<ShadowSpec> _shadowItems = [];
    private readonly List<BackgroundImageSpec> _backgroundItems = [];
    private readonly List<ImageSpec> _imageItems = [];
    private readonly List<ScrollbarSpec> _scrollbarItems = [];
    private readonly List<CornerRadius> _radiusItems = [];
    private readonly List<Matrix3x2> _transformItems = [];
    private readonly List<FilterSpec> _filterItems = [];
    private readonly List<ClipPathSpec> _clipPathItems = [];
    private readonly List<MaskSpec> _maskItems = [];

    private readonly Dictionary<TextCommand, int> _texts = [];
    private readonly Dictionary<CornerRadius, int> _radii = [];
    private readonly Dictionary<Matrix3x2, int> _transforms = [];
    private readonly Dictionary<FilterSpec, int> _filters = [];
    private readonly Dictionary<ClipPathSpec, int> _clipPaths = [];
    private readonly Dictionary<MaskSpec, int> _masks = [];
    private readonly Dictionary<BorderSpec, int> _borders = [];
    private readonly Dictionary<ShadowSpec, int> _shadows = [];
    private readonly Dictionary<ImageSpec, int> _images = [];
    private readonly Dictionary<ScrollbarSpec, int> _scrollbars = [];
    private readonly Dictionary<BackgroundImageSpec, int> _backgrounds = [];

    public int Count => _items.Count;

    /// <summary>Empties the pool so it can be reused for another frame.</summary>
    public void Clear()
    {
        _items.Clear();
        _textItems.Clear();
        _borderItems.Clear();
        _shadowItems.Clear();
        _backgroundItems.Clear();
        _imageItems.Clear();
        _scrollbarItems.Clear();
        _radiusItems.Clear();
        _transformItems.Clear();
        _filterItems.Clear();
        _clipPathItems.Clear();
        _maskItems.Clear();
        _texts.Clear();
        _radii.Clear();
        _transforms.Clear();
        _filters.Clear();
        _clipPaths.Clear();
        _masks.Clear();
        _borders.Clear();
        _shadows.Clear();
        _images.Clear();
        _scrollbars.Clear();
        _backgrounds.Clear();
    }

    public object Get(int id) => _items[id].Kind switch
    {
        ConstantKind.Text => _textItems[_items[id].Index],
        ConstantKind.Border => _borderItems[_items[id].Index],
        ConstantKind.Shadow => _shadowItems[_items[id].Index],
        ConstantKind.Background => _backgroundItems[_items[id].Index],
        ConstantKind.Image => _imageItems[_items[id].Index],
        ConstantKind.Scrollbar => _scrollbarItems[_items[id].Index],
        ConstantKind.Radius => _radiusItems[_items[id].Index],
        ConstantKind.Transform => _transformItems[_items[id].Index],
        ConstantKind.Filter => _filterItems[_items[id].Index],
        ConstantKind.ClipPath => _clipPathItems[_items[id].Index],
        ConstantKind.Mask => _maskItems[_items[id].Index],
        _ => throw new ArgumentOutOfRangeException(nameof(id))
    };

    public TextCommand GetText(int id) => _textItems[_items[id].Index];

    public BorderSpec GetBorder(int id) => _borderItems[_items[id].Index];

    public ShadowSpec GetShadow(int id) => _shadowItems[_items[id].Index];

    public BackgroundImageSpec GetBackground(int id) => _backgroundItems[_items[id].Index];

    public ImageSpec GetImage(int id) => _imageItems[_items[id].Index];

    public ScrollbarSpec GetScrollbar(int id) => _scrollbarItems[_items[id].Index];

    public CornerRadius GetRadius(int id) => _radiusItems[_items[id].Index];

    public Matrix3x2 GetTransform(int id) => _transformItems[_items[id].Index];

    public FilterSpec GetFilter(int id) => _filterItems[_items[id].Index];

    public ClipPathSpec GetClipPath(int id) => _clipPathItems[_items[id].Index];

    public MaskSpec GetMask(int id) => _maskItems[_items[id].Index];

    public int AddText(string text, TextStyle style)
    {
        var command = new TextCommand(text, style);

        if (_texts.TryGetValue(command, out var existing))
        {
            return existing;
        }

        var id = AddItem(_textItems, ConstantKind.Text, command);
        _texts[command] = id;
        return id;
    }

    public int AddBorder(BorderSpec border)
    {
        if (_borders.TryGetValue(border, out var existing))
        {
            return existing;
        }

        var id = AddItem(_borderItems, ConstantKind.Border, border);
        _borders[border] = id;
        return id;
    }

    public int AddShadow(ShadowSpec shadow)
    {
        if (_shadows.TryGetValue(shadow, out var existing))
        {
            return existing;
        }

        var id = AddItem(_shadowItems, ConstantKind.Shadow, shadow);
        _shadows[shadow] = id;
        return id;
    }

    public int AddBackground(BackgroundImageSpec background)
    {
        if (_backgrounds.TryGetValue(background, out var existing))
        {
            return existing;
        }

        var id = AddItem(_backgroundItems, ConstantKind.Background, background);
        _backgrounds[background] = id;
        return id;
    }

    public int AddImage(ImageSpec image)
    {
        if (_images.TryGetValue(image, out var existing))
        {
            return existing;
        }

        var id = AddItem(_imageItems, ConstantKind.Image, image);
        _images[image] = id;
        return id;
    }

    public int AddScrollbar(ScrollbarSpec scrollbar)
    {
        if (_scrollbars.TryGetValue(scrollbar, out var existing))
        {
            return existing;
        }

        var id = AddItem(_scrollbarItems, ConstantKind.Scrollbar, scrollbar);
        _scrollbars[scrollbar] = id;
        return id;
    }

    public int AddRadius(CornerRadius radius)
    {
        if (_radii.TryGetValue(radius, out var existing))
        {
            return existing;
        }

        var id = AddItem(_radiusItems, ConstantKind.Radius, radius);
        _radii[radius] = id;
        return id;
    }

    public int AddTransform(Matrix3x2 transform)
    {
        if (_transforms.TryGetValue(transform, out var existing))
        {
            return existing;
        }

        var id = AddItem(_transformItems, ConstantKind.Transform, transform);
        _transforms[transform] = id;
        return id;
    }

    public int AddFilter(FilterSpec filter)
    {
        if (_filters.TryGetValue(filter, out var existing))
        {
            return existing;
        }

        var id = AddItem(_filterItems, ConstantKind.Filter, filter);
        _filters[filter] = id;
        return id;
    }

    public int AddClipPath(ClipPathSpec clipPath)
    {
        if (_clipPaths.TryGetValue(clipPath, out var existing))
        {
            return existing;
        }

        var id = AddItem(_clipPathItems, ConstantKind.ClipPath, clipPath);
        _clipPaths[clipPath] = id;
        return id;
    }

    public int AddMask(MaskSpec mask)
    {
        if (_masks.TryGetValue(mask, out var existing))
        {
            return existing;
        }

        var id = AddItem(_maskItems, ConstantKind.Mask, mask);
        _masks[mask] = id;
        return id;
    }

    private int AddItem<T>(List<T> list, ConstantKind kind, T value)
    {
        var id = _items.Count;

        if (id > ushort.MaxValue)
        {
            throw new InvalidOperationException(
                "The constant pool exceeds the 16-bit aux index range.");
        }

        list.Add(value);
        _items.Add(new ConstantRef(kind, list.Count - 1));
        return id;
    }
}
