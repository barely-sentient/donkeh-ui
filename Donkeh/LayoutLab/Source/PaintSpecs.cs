using System.Numerics;

namespace Donkeh.LayoutLab;

/// <summary>Text style packed into the constant pool so glyph runs can be batched.</summary>
/// <param name="FontFamily">The font family name requested by CSS; matching and fallback happen in the renderer.</param>
/// <param name="FontSize">The resolved font size in CSS pixels.</param>
/// <param name="FontWeight">The numeric font weight, normally 100 to 900.</param>
/// <param name="Italic">Whether the italic face is requested.</param>
/// <param name="Color">The text colour as packed ARGB.</param>
/// <param name="LetterSpacing">Extra advance per character in CSS pixels; may be negative.</param>
/// <param name="LineHeight">The resolved line box height in CSS pixels, not a unitless multiplier.</param>
/// <param name="Align">The horizontal alignment used when the run is laid out.</param>
/// <param name="Decoration">The decoration flags painted with the run.</param>
/// <param name="WhiteSpace">The whitespace mode the run was laid out with.</param>
/// <param name="Direction">The inline base direction of the run.</param>
/// <remarks>
/// <para>
/// This is a value record, so the text engine can compare styles cheaply. The
/// style participates in the <see cref="TextCommand"/> key used by
/// <see cref="ConstantPool.AddText"/>, which lets identical runs share one
/// constant. Every length must already be resolved to CSS pixels: the
/// renderer never evaluates CSS units.
/// </para>
/// <para>
/// <see cref="Default"/> mirrors the initial style values and is used by
/// <see cref="TextLayout.Empty"/>.
/// </para>
/// </remarks>
/// <seealso cref="ComputedStyle.ToTextStyle"/>
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
    /// <summary>The default style: 16 px sans-serif, weight 400, black, line height 1.2, LTR.</summary>
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

/// <summary>A run of text paired with the style used to draw it.</summary>
/// <param name="Text">The exact glyph run, after wrapping, trimming and case handling.</param>
/// <param name="Style">The resolved style for the run.</param>
/// <remarks>
/// The pair is the constant-pool key for <see cref="InstructionOpCode.DrawText"/>;
/// identical text and style values are interned to one id.
/// </remarks>
public readonly record struct TextCommand(string Text, TextStyle Style);

/// <summary>One side of a border, resolved to pixels.</summary>
/// <param name="Width">The painted width in CSS pixels; a non-positive width paints nothing.</param>
/// <param name="Style">The border line style.</param>
/// <param name="Color">The side colour as packed ARGB.</param>
/// <remarks><see cref="None"/> is the canonical invisible side and is what a missing border resolves to.</remarks>
public readonly record struct BorderSide(float Width, BorderStyle Style, uint Color)
{
    /// <summary>An invisible side: zero width, <see cref="BorderStyle.None"/> and a zero colour.</summary>
    public static readonly BorderSide None = new(0, BorderStyle.None, 0);
}

/// <summary>A resolved border: four sides plus the shared corner radius.</summary>
/// <param name="Top">The top side.</param>
/// <param name="Right">The right side.</param>
/// <param name="Bottom">The bottom side.</param>
/// <param name="Left">The left side.</param>
/// <param name="Radius">The resolved corner radii in CSS pixels.</param>
/// <remarks>
/// Value equality makes the spec usable as a constant-pool key. The radius is
/// shared by all four corners; per-corner radii live in <see cref="Radius"/>.
/// </remarks>
public readonly record struct BorderSpec(
    BorderSide Top,
    BorderSide Right,
    BorderSide Bottom,
    BorderSide Left,
    CornerRadius Radius)
{
    /// <summary>Gets a value indicating whether no side would paint anything.</summary>
    /// <remarks>
    /// A side is invisible when its width is non-positive or its style is
    /// <see cref="BorderStyle.None"/> or <see cref="BorderStyle.Hidden"/>. The
    /// corner radius is ignored: a spec with invisible sides but rounded corners
    /// is still considered empty.
    /// </remarks>
    public bool IsEmpty =>
        (Top.Width <= 0 || Top.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Right.Width <= 0 || Right.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Bottom.Width <= 0 || Bottom.Style is BorderStyle.None or BorderStyle.Hidden)
        && (Left.Width <= 0 || Left.Style is BorderStyle.None or BorderStyle.Hidden);
}

/// <summary>A resolved box shadow in CSS pixels.</summary>
/// <param name="OffsetX">Horizontal offset; positive values move the shadow right.</param>
/// <param name="OffsetY">Vertical offset; positive values move the shadow down.</param>
/// <param name="Blur">The blur radius; zero produces a hard shadow.</param>
/// <param name="Spread">The spread distance; negative values shrink the shadow.</param>
/// <param name="Color">The shadow colour as packed ARGB, including its alpha.</param>
/// <param name="Inset">Whether the shadow is painted inside the border box.</param>
/// <remarks>
/// The record is a value type and is interned by the constant pool, so shadows
/// with identical values share one draw constant.
/// </remarks>
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
/// <param name="Kind">The filter function family.</param>
/// <param name="Amount">
/// The numeric argument: CSS pixels for <see cref="FilterFunctionKind.Blur"/>,
/// a unit multiplier for brightness/contrast/opacity/saturate, a 0 to 1
/// fraction for grayscale/invert/sepia, or degrees for hue rotation.
/// </param>
/// <param name="Shadow">The shadow definition; used only by <see cref="FilterFunctionKind.DropShadow"/>.</param>
/// <param name="Url">The external filter reference; used only by <see cref="FilterFunctionKind.Url"/>.</param>
/// <remarks>
/// Functions are applied in array order. <see cref="Url"/> filters are
/// represented but the layout engine does not resolve the referenced resource.
/// </remarks>
public readonly record struct FilterFunction(
    FilterFunctionKind Kind,
    float Amount,
    ShadowSpec Shadow = default,
    string? Url = null);

/// <summary>A parsed filter chain. The renderer applies the functions in order.</summary>
/// <remarks>
/// Instances are immutable after construction and are interned by the constant
/// pool using reference equality, since the class does not override
/// <see cref="object.Equals(object)"/>. Build one instance per distinct chain
/// and reuse it to get pooling benefits.
/// </remarks>
public sealed class FilterSpec
{
    /// <summary>The empty chain, which is the identity filter.</summary>
    public static readonly FilterSpec None = new([]);

    /// <summary>Creates a filter chain over the supplied functions.</summary>
    /// <param name="functions">The functions in application order; the array is stored by reference, not copied.</param>
    public FilterSpec(FilterFunction[] functions)
    {
        Functions = functions;
    }

    /// <summary>Gets the functions in application order.</summary>
    /// <remarks>The array is owned by this spec and must not be mutated after construction.</remarks>
    public FilterFunction[] Functions { get; }

    /// <summary>Gets a value indicating whether the chain contains no functions.</summary>
    public bool IsNone => Functions.Length == 0;
}

/// <summary>One vertex of a polygon clip path.</summary>
/// <param name="X">The horizontal coordinate as an unresolved CSS length.</param>
/// <param name="Y">The vertical coordinate as an unresolved CSS length.</param>
/// <remarks>
/// Percentages and relative units stay unresolved and are interpreted against
/// the element's border box by the renderer.
/// </remarks>
public readonly record struct ClipPathPoint(CssLength X, CssLength Y);

/// <summary>
/// A parsed <c>clip-path</c>. Percentages and relative lengths stay unresolved
/// so the renderer can resolve them against the element's border box, which is
/// carried on the <c>PushClipPath</c> instruction.
/// </summary>
/// <remarks>
/// The same instance can be referenced by several elements and is interned by
/// the constant pool using reference equality. All properties are init-only, so
/// specs are safe to share once constructed.
/// </remarks>
public sealed class ClipPathSpec
{
    /// <summary>The absent clip path, with <see cref="Kind"/> set to <see cref="ClipPathKind.None"/>.</summary>
    public static readonly ClipPathSpec None = new() { Kind = ClipPathKind.None };

    /// <summary>Gets the geometry family of the clip path.</summary>
    public ClipPathKind Kind { get; init; }

    /// <summary>Gets the top inset from the border box; used by <see cref="ClipPathKind.Inset"/>.</summary>
    public CssLength InsetTop { get; init; } = CssLength.Zero;

    /// <summary>Gets the right inset from the border box; used by <see cref="ClipPathKind.Inset"/>.</summary>
    public CssLength InsetRight { get; init; } = CssLength.Zero;

    /// <summary>Gets the bottom inset from the border box; used by <see cref="ClipPathKind.Inset"/>.</summary>
    public CssLength InsetBottom { get; init; } = CssLength.Zero;

    /// <summary>Gets the left inset from the border box; used by <see cref="ClipPathKind.Inset"/>.</summary>
    public CssLength InsetLeft { get; init; } = CssLength.Zero;

    /// <summary>Gets the horizontal radius of a circle or ellipse clip; defaults to 50 percent of the box width.</summary>
    public CssLength RadiusX { get; init; } = CssLength.Percent(50);

    /// <summary>Gets the vertical radius of a circle or ellipse clip; defaults to 50 percent of the box height.</summary>
    public CssLength RadiusY { get; init; } = CssLength.Percent(50);

    /// <summary>Gets the horizontal centre, normally a fraction of the border box width.</summary>
    public CssLength CenterX { get; init; } = CssLength.Percent(50);

    /// <summary>Gets the vertical centre, normally a fraction of the border box height.</summary>
    public CssLength CenterY { get; init; } = CssLength.Percent(50);

    /// <summary>Gets the polygon vertices, or <see langword="null"/> for non-polygon kinds.</summary>
    public ClipPathPoint[]? Points { get; init; }

    /// <summary>Gets the external resource reference, or <see langword="null"/> for geometric kinds.</summary>
    public string? Url { get; init; }

    /// <summary>Gets a value indicating whether this spec represents no clipping.</summary>
    public bool IsNone => Kind == ClipPathKind.None;
}

/// <summary>A parsed <c>mask</c> shorthand. The renderer rasterises the source.</summary>
/// <remarks>
/// Like the other paint specs, a mask instance is interned by reference, so
/// reuse one instance for equal masks.
/// </remarks>
public sealed class MaskSpec
{
    /// <summary>The absent mask, with <see cref="Kind"/> set to <see cref="ImageKind.None"/>.</summary>
    public static readonly MaskSpec None = new() { Kind = ImageKind.None };

    /// <summary>Gets the kind of mask source (URL or gradient).</summary>
    public ImageKind Kind { get; init; }

    /// <summary>Gets the source URL or gradient text, or <see langword="null"/> when <see cref="Kind"/> is none.</summary>
    public string? Source { get; init; }

    /// <summary>Gets how the mask's alpha is combined with the element.</summary>
    public MaskMode Mode { get; init; } = MaskMode.MatchSource;

    /// <summary>Gets how the mask image tiles.</summary>
    public BackgroundRepeat Repeat { get; init; } = BackgroundRepeat.Repeat;

    /// <summary>Gets a value indicating whether this spec represents no masking.</summary>
    public bool IsNone => Kind == ImageKind.None;
}

/// <summary>One colour stop in a gradient.</summary>
/// <param name="Offset">
/// The position along the gradient as a fraction in the range 0 to 1, not a
/// percentage. A NaN means "not specified"; the parser resolves such stops
/// before the spec reaches the renderer.
/// </param>
/// <param name="Color">The stop colour as packed ARGB.</param>
/// <remarks>
/// During parsing the first and last unspecified offsets become 0 and 1, and
/// unspecified interior stops are distributed evenly between their nearest
/// specified neighbours. Author-specified offsets are clamped to 0 to 1 and are
/// not forced to be monotonic.
/// </remarks>
public readonly record struct GradientStop(float Offset, uint Color);

/// <summary>A parsed background layer the renderer can consume without re-tokenising CSS.</summary>
/// <remarks>
/// <para>
/// <see cref="Kind"/> selects which fields are meaningful: URL layers use
/// <see cref="Url"/>; gradient layers use <see cref="GradientKind"/>,
/// <see cref="Angle"/>, <see cref="Center"/>, <see cref="Radius"/> and
/// <see cref="Stops"/>.
/// </para>
/// <para>
/// Gradient geometry is normalised to the background box:
/// <see cref="Center"/> components are fractions in the range 0 to 1 and
/// <see cref="Radius"/> components are fractions of the box size (0.5 means a
/// radius of half the box). <see cref="Angle"/> is in CSS degrees, where 0
/// points to the top, 90 to the right and the linear default is 180 (to the
/// bottom).
/// </para>
/// <para>
/// Stop offsets are normalised during parsing: the first and last unspecified
/// offsets become 0 and 1, interior gaps are filled evenly, and explicit
/// percentages or angles are converted to the 0 to 1 fraction. The array is
/// immutable once published.
/// </para>
/// <para>
/// <see cref="Repeat"/>, <see cref="Clip"/>, <see cref="Origin"/> and
/// <see cref="Fixed"/> are copied from the element style by the paint pass, so
/// the same parsed layer can be painted with different settings. Because the
/// class does not override equality, the constant pool interns it by reference.
/// </para>
/// </remarks>
public sealed class BackgroundImageSpec
{
    /// <summary>Gets the layer kind: none, URL, linear, radial or conic.</summary>
    public ImageKind Kind { get; init; }

    /// <summary>Gets the image URL for <see cref="ImageKind.Url"/>, or <see langword="null"/>.</summary>
    public string? Url { get; init; }

    /// <summary>Gets the gradient family; meaningful for gradient kinds only.</summary>
    public GradientKind GradientKind { get; init; }

    /// <summary>Gets the gradient angle in CSS degrees; 0 is up and 90 is right.</summary>
    public float Angle { get; init; }

    /// <summary>Gets the gradient centre as fractions of the box, normally (0.5, 0.5).</summary>
    public Point Center { get; init; }

    /// <summary>Gets the radial radius as fractions of the box size, normally (0.5, 0.5).</summary>
    public Size Radius { get; init; }

    /// <summary>Gets the normalised colour stops in order; empty when the layer has none.</summary>
    public GradientStop[] Stops { get; init; } = [];

    /// <summary>Gets how the layer tiles inside its positioning area.</summary>
    public BackgroundRepeat Repeat { get; init; }

    /// <summary>Gets the box the layer is clipped to.</summary>
    public BackgroundBox Clip { get; init; }

    /// <summary>Gets the box the layer is positioned and sized against.</summary>
    public BackgroundBox Origin { get; init; }

    /// <summary>Gets whether the layer is fixed to the viewport.</summary>
    public bool Fixed { get; init; }

    /// <summary>The canonical absent layer, with <see cref="Kind"/> set to <see cref="ImageKind.None"/>.</summary>
    public static readonly BackgroundImageSpec None = new() { Kind = ImageKind.None };
}

/// <summary>A bitmap to draw into a replaced element's content box.</summary>
/// <param name="Source">The image source identifier, normally a URL or file path.</param>
/// <param name="Fit">How the bitmap is fitted into the destination rectangle.</param>
/// <param name="Opacity">The extra opacity multiplier in the range 0 to 1.</param>
/// <param name="Tint">
/// A packed ARGB colour multiplied with the texture; <c>0xFFFFFFFF</c> leaves
/// the image unmodified.
/// </param>
/// <remarks>
/// The record is a value type, so identical images share one constant-pool
/// entry. The layout engine only emits a <c>DrawImage</c> instruction when
/// <see cref="LayoutOptions.ImageProvider"/> returns a value.
/// </remarks>
public readonly record struct ImageSpec(
    string Source,
    ObjectFit Fit,
    float Opacity,
    uint Tint);

/// <summary>The complete paint state of one scrollbar.</summary>
/// <param name="Vertical">Whether the bar is vertical.</param>
/// <param name="ThumbOffset">The thumb offset along the track in CSS pixels.</param>
/// <param name="ThumbLength">The thumb length along the track in CSS pixels.</param>
/// <param name="TrackLength">The track length along the scroll axis in CSS pixels.</param>
/// <param name="TrackColor">The track colour as packed ARGB.</param>
/// <param name="ThumbColor">The thumb colour as packed ARGB.</param>
/// <param name="Width">The bar thickness in CSS pixels, normally <see cref="ScrollbarLayout.Thickness"/>.</param>
/// <remarks>
/// The track rectangle itself is carried by the <c>DrawScrollbar</c>
/// instruction; this spec holds the metrics and colours that vary per frame.
/// </remarks>
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
/// <remarks>
/// <para>
/// Constants live in one typed list per kind plus a shared reference list that
/// records the kind and per-kind index. This avoids boxing value types on the
/// hot path. Ids are dense, start at 0 and follow first-insertion order; they
/// are invalidated by <see cref="Clear"/>.
/// </para>
/// <para>
/// Interning uses value equality for records such as <see cref="TextCommand"/>,
/// <see cref="BorderSpec"/>, <see cref="ShadowSpec"/>,
/// <see cref="ImageSpec"/>, <see cref="ScrollbarSpec"/>,
/// <see cref="CornerRadius"/> and <see cref="System.Numerics.Matrix3x2"/>.
/// Reference-typed specs (<see cref="BackgroundImageSpec"/>,
/// <see cref="FilterSpec"/>, <see cref="ClipPathSpec"/>,
/// <see cref="MaskSpec"/>) are interned by reference, so callers should reuse
/// instances when they want sharing.
/// </para>
/// <para>
/// The pool is owned by the layout engine and cleared at the start of each
/// frame. It is not thread-safe and supports at most 65,536 constants, because
/// <see cref="Instruction.Aux"/> is 16 bits.
/// </para>
/// </remarks>
/// <seealso cref="Instruction.Payload"/>
/// <seealso cref="Instruction.Aux"/>
public sealed class ConstantPool
{
    /// <summary>The storage family a constant id refers to.</summary>
    private enum ConstantKind : byte
    {
        /// <summary>A text run and style.</summary>
        Text,

        /// <summary>A border specification.</summary>
        Border,

        /// <summary>A box shadow.</summary>
        Shadow,

        /// <summary>A background layer.</summary>
        Background,

        /// <summary>A bitmap image.</summary>
        Image,

        /// <summary>A scrollbar paint state.</summary>
        Scrollbar,

        /// <summary>Corner radii.</summary>
        Radius,

        /// <summary>A transform matrix.</summary>
        Transform,

        /// <summary>A filter chain.</summary>
        Filter,

        /// <summary>A clip path.</summary>
        ClipPath,

        /// <summary>A mask.</summary>
        Mask
    }

    /// <summary>Maps a public constant id to its kind and index in the typed list.</summary>
    /// <param name="Kind">The storage family.</param>
    /// <param name="Index">The zero-based index within that family's list.</param>
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

    /// <summary>Gets the number of interned constants, which is also the next id to be assigned.</summary>
    public int Count => _items.Count;

    /// <summary>Empties the pool so it can be reused for another frame.</summary>
    /// <remarks>
    /// Clears both the typed lists and the interning maps. Capacity is retained,
    /// but every previously returned id becomes invalid.
    /// </remarks>
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

    /// <summary>Creates an independent copy of the pool.</summary>
    /// <returns>
    /// A pool with the same ids and values that keeps working after this pool is
    /// cleared or reused for another frame.
    /// </returns>
    /// <remarks>
    /// Typed lists and interning maps are copied so the snapshot has no shared
    /// mutable state with the original. Reference-typed constants are still
    /// shared by reference and remain immutable.
    /// </remarks>
    internal ConstantPool Snapshot()
    {
        var copy = new ConstantPool();
        copy._items.AddRange(_items);
        copy._textItems.AddRange(_textItems);
        copy._borderItems.AddRange(_borderItems);
        copy._shadowItems.AddRange(_shadowItems);
        copy._backgroundItems.AddRange(_backgroundItems);
        copy._imageItems.AddRange(_imageItems);
        copy._scrollbarItems.AddRange(_scrollbarItems);
        copy._radiusItems.AddRange(_radiusItems);
        copy._transformItems.AddRange(_transformItems);
        copy._filterItems.AddRange(_filterItems);
        copy._clipPathItems.AddRange(_clipPathItems);
        copy._maskItems.AddRange(_maskItems);

        foreach (var pair in _texts)
        {
            copy._texts[pair.Key] = pair.Value;
        }

        foreach (var pair in _radii)
        {
            copy._radii[pair.Key] = pair.Value;
        }

        foreach (var pair in _transforms)
        {
            copy._transforms[pair.Key] = pair.Value;
        }

        foreach (var pair in _filters)
        {
            copy._filters[pair.Key] = pair.Value;
        }

        foreach (var pair in _clipPaths)
        {
            copy._clipPaths[pair.Key] = pair.Value;
        }

        foreach (var pair in _masks)
        {
            copy._masks[pair.Key] = pair.Value;
        }

        foreach (var pair in _borders)
        {
            copy._borders[pair.Key] = pair.Value;
        }

        foreach (var pair in _shadows)
        {
            copy._shadows[pair.Key] = pair.Value;
        }

        foreach (var pair in _images)
        {
            copy._images[pair.Key] = pair.Value;
        }

        foreach (var pair in _scrollbars)
        {
            copy._scrollbars[pair.Key] = pair.Value;
        }

        foreach (var pair in _backgrounds)
        {
            copy._backgrounds[pair.Key] = pair.Value;
        }

        return copy;
    }

    /// <summary>Gets a constant by id, boxed as <see cref="object"/>.</summary>
    /// <param name="id">A constant id previously returned by one of the <c>Add</c> methods.</param>
    /// <returns>The stored value, boxed for value-typed constants.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="id"/> is outside the range 0 to
    /// <see cref="Count"/> - 1.
    /// </exception>
    /// <remarks>
    /// Prefer the typed getters: this overload allocates a box for every
    /// value-typed constant and is intended for diagnostics and tooling.
    /// </remarks>
    public object Get(int id)
    {
        if ((uint)id >= (uint)_items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var reference = _items[id];
        return reference.Kind switch
        {
            ConstantKind.Text => _textItems[reference.Index],
            ConstantKind.Border => _borderItems[reference.Index],
            ConstantKind.Shadow => _shadowItems[reference.Index],
            ConstantKind.Background => _backgroundItems[reference.Index],
            ConstantKind.Image => _imageItems[reference.Index],
            ConstantKind.Scrollbar => _scrollbarItems[reference.Index],
            ConstantKind.Radius => _radiusItems[reference.Index],
            ConstantKind.Transform => _transformItems[reference.Index],
            ConstantKind.Filter => _filterItems[reference.Index],
            ConstantKind.ClipPath => _clipPathItems[reference.Index],
            ConstantKind.Mask => _maskItems[reference.Index],
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        };
    }

    /// <summary>Gets a text command by id.</summary>
    /// <param name="id">An id returned by <see cref="AddText"/>.</param>
    /// <returns>The interned text and style.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a text constant.</exception>
    public TextCommand GetText(int id) => GetConstant(id, ConstantKind.Text, _textItems);

    /// <summary>Gets a border specification by id.</summary>
    /// <param name="id">An id returned by <see cref="AddBorder"/>.</param>
    /// <returns>The interned border specification.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a border constant.</exception>
    public BorderSpec GetBorder(int id) => GetConstant(id, ConstantKind.Border, _borderItems);

    /// <summary>Gets a box-shadow specification by id.</summary>
    /// <param name="id">An id returned by <see cref="AddShadow"/>.</param>
    /// <returns>The interned shadow specification.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a shadow constant.</exception>
    public ShadowSpec GetShadow(int id) => GetConstant(id, ConstantKind.Shadow, _shadowItems);

    /// <summary>Gets a background layer by id.</summary>
    /// <param name="id">An id returned by <see cref="AddBackground"/>.</param>
    /// <returns>The interned background layer instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a background constant.</exception>
    public BackgroundImageSpec GetBackground(int id) => GetConstant(id, ConstantKind.Background, _backgroundItems);

    /// <summary>Gets an image specification by id.</summary>
    /// <param name="id">An id returned by <see cref="AddImage"/>.</param>
    /// <returns>The interned image specification.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not an image constant.</exception>
    public ImageSpec GetImage(int id) => GetConstant(id, ConstantKind.Image, _imageItems);

    /// <summary>Gets a scrollbar specification by id.</summary>
    /// <param name="id">An id returned by <see cref="AddScrollbar"/>.</param>
    /// <returns>The interned scrollbar specification.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a scrollbar constant.</exception>
    public ScrollbarSpec GetScrollbar(int id) => GetConstant(id, ConstantKind.Scrollbar, _scrollbarItems);

    /// <summary>Gets corner radii by id.</summary>
    /// <param name="id">An id returned by <see cref="AddRadius"/>.</param>
    /// <returns>The interned radii in CSS pixels.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a radius constant.</exception>
    public CornerRadius GetRadius(int id) => GetConstant(id, ConstantKind.Radius, _radiusItems);

    /// <summary>Gets a transform matrix by id.</summary>
    /// <param name="id">An id returned by <see cref="AddTransform"/>.</param>
    /// <returns>The interned transform matrix.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a transform constant.</exception>
    public Matrix3x2 GetTransform(int id) => GetConstant(id, ConstantKind.Transform, _transformItems);

    /// <summary>Gets a filter chain by id.</summary>
    /// <param name="id">An id returned by <see cref="AddFilter"/>.</param>
    /// <returns>The interned filter chain instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a filter constant.</exception>
    public FilterSpec GetFilter(int id) => GetConstant(id, ConstantKind.Filter, _filterItems);

    /// <summary>Gets a clip path by id.</summary>
    /// <param name="id">An id returned by <see cref="AddClipPath"/>.</param>
    /// <returns>The interned clip path instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a clip-path constant.</exception>
    public ClipPathSpec GetClipPath(int id) => GetConstant(id, ConstantKind.ClipPath, _clipPathItems);

    /// <summary>Gets a mask by id.</summary>
    /// <param name="id">An id returned by <see cref="AddMask"/>.</param>
    /// <returns>The interned mask instance.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> is not a mask constant.</exception>
    public MaskSpec GetMask(int id) => GetConstant(id, ConstantKind.Mask, _maskItems);

    /// <summary>Returns a typed constant, validating both the id and its storage kind.</summary>
    /// <param name="id">Constant id to resolve.</param>
    /// <param name="kind">Storage family the id must belong to.</param>
    /// <param name="items">Typed list for that family.</param>
    /// <returns>The stored value.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="id"/> is outside the pool.</exception>
    /// <exception cref="ArgumentException"><paramref name="id"/> belongs to a different family.</exception>
    private T GetConstant<T>(int id, ConstantKind kind, List<T> items)
    {
        if ((uint)id >= (uint)_items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(id));
        }

        var reference = _items[id];

        if (reference.Kind != kind)
        {
            throw new ArgumentException(
                $"Constant {id} is a {reference.Kind} constant, not {kind}.",
                nameof(id));
        }

        return items[reference.Index];
    }

    /// <summary>Interns a text run and its style.</summary>
    /// <param name="text">The exact text of the run.</param>
    /// <param name="style">The resolved style for the run.</param>
    /// <returns>The id of the interned <see cref="TextCommand"/>.</returns>
    /// <remarks>Text and style are compared by value, so identical runs share one id.</remarks>
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

    /// <summary>Interns a border specification.</summary>
    /// <param name="border">The resolved border.</param>
    /// <returns>The id of the interned <see cref="BorderSpec"/>.</returns>
    /// <remarks>Values are compared structurally; identical borders share one id.</remarks>
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

    /// <summary>Interns a box-shadow specification.</summary>
    /// <param name="shadow">The resolved shadow.</param>
    /// <returns>The id of the interned <see cref="ShadowSpec"/>.</returns>
    /// <remarks>Values are compared structurally; identical shadows share one id.</remarks>
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

    /// <summary>Interns a background layer.</summary>
    /// <param name="background">
    /// The parsed layer, normally an entry from
    /// <see cref="ComputedStyle.BackgroundImages"/> merged with the style's
    /// repeat/clip/origin values.
    /// </param>
    /// <returns>The id of the interned layer.</returns>
    /// <remarks>
    /// The class uses reference equality, so equal-looking layers only share an
    /// id when they are the same instance. The paint pass caches these instances
    /// on <see cref="ComputedStyle.CachedBackgroundSpecs"/> for that reason.
    /// </remarks>
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

    /// <summary>Interns a bitmap image specification.</summary>
    /// <param name="image">The resolved image.</param>
    /// <returns>The id of the interned <see cref="ImageSpec"/>.</returns>
    /// <remarks>Values are compared structurally; identical images share one id.</remarks>
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

    /// <summary>Interns a scrollbar paint state.</summary>
    /// <param name="scrollbar">The scrollbar metrics and colours.</param>
    /// <returns>The id of the interned <see cref="ScrollbarSpec"/>.</returns>
    /// <remarks>Values are compared structurally; identical states share one id within a frame.</remarks>
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

    /// <summary>Interns corner radii.</summary>
    /// <param name="radius">The resolved radii in CSS pixels.</param>
    /// <returns>The id of the interned <see cref="CornerRadius"/>.</returns>
    /// <remarks>The id is stored in <see cref="Instruction.Aux"/>, so it must fit in 16 bits.</remarks>
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

    /// <summary>Interns a transform matrix.</summary>
    /// <param name="transform">The combined transform matrix.</param>
    /// <returns>The id of the interned <see cref="Matrix3x2"/>.</returns>
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

    /// <summary>Interns a filter chain.</summary>
    /// <param name="filter">The parsed filter chain.</param>
    /// <returns>The id of the interned chain.</returns>
    /// <remarks>Reference equality applies; reuse a <see cref="FilterSpec"/> instance for equal chains.</remarks>
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

    /// <summary>Interns a clip path.</summary>
    /// <param name="clipPath">The parsed clip path.</param>
    /// <returns>The id of the interned clip path.</returns>
    /// <remarks>Reference equality applies; reuse a <see cref="ClipPathSpec"/> instance for equal shapes.</remarks>
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

    /// <summary>Interns a mask.</summary>
    /// <param name="mask">The parsed mask.</param>
    /// <returns>The id of the interned mask.</returns>
    /// <remarks>Reference equality applies; reuse a <see cref="MaskSpec"/> instance for equal masks.</remarks>
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

    /// <summary>Appends a value to its typed list and records the id-to-kind mapping.</summary>
    /// <typeparam name="T">The constant's storage type.</typeparam>
    /// <param name="list">The typed list for the constant's family.</param>
    /// <param name="kind">The constant family.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The new constant id, which is the index in the shared reference list.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the pool already holds 65,536 constants, because the id can no
    /// longer be addressed by a 16-bit <see cref="Instruction.Aux"/>.
    /// </exception>
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
