namespace Donkeh.LayoutLab;

/// <summary>The unit of a parsed CSS length.</summary>
/// <remarks>
/// <para>
/// The enumeration is byte-backed so a <see cref="CssLength"/> stays small
/// enough to be copied freely. <see cref="Auto"/> and the intrinsic sizing
/// keywords are sentinels rather than measurement units; they carry no
/// meaningful numeric value.
/// </para>
/// <para>
/// Absolute units use the CSS reference pixel of 96 px per inch.
/// </para>
/// </remarks>
public enum LengthUnit : byte
{
    /// <summary>The <c>auto</c> keyword; the layout algorithm chooses the size.</summary>
    Auto,
    /// <summary>CSS pixels (<c>px</c>), the engine's canonical unit.</summary>
    Px,
    /// <summary>Percentage (<c>%</c>) of the relevant containing-block size.</summary>
    Percent,
    /// <summary>Ems (<c>em</c>), relative to the element's own font size.</summary>
    Em,
    /// <summary>Rems (<c>rem</c>), relative to the root element's font size.</summary>
    Rem,
    /// <summary>Exes (<c>ex</c>); approximated as half an em (no x-height available).</summary>
    Ex,
    /// <summary>Characters (<c>ch</c>); approximated as half an em (no advance available).</summary>
    Ch,
    /// <summary>Viewport width units (<c>vw</c>), 1% of the layout viewport width.</summary>
    Vw,
    /// <summary>Viewport height units (<c>vh</c>), 1% of the layout viewport height.</summary>
    Vh,
    /// <summary>Viewport minimum units (<c>vmin</c>), 1% of the smaller viewport axis.</summary>
    Vmin,
    /// <summary>Viewport maximum units (<c>vmax</c>), 1% of the larger viewport axis.</summary>
    Vmax,
    /// <summary>Centimetres (<c>cm</c>), converted at 96 px per inch.</summary>
    Cm,
    /// <summary>Millimetres (<c>mm</c>), converted at 96 px per inch.</summary>
    Mm,
    /// <summary>Inches (<c>in</c>), converted at 96 px per inch.</summary>
    In,
    /// <summary>Points (<c>pt</c>), converted at 96 px per 72 pt.</summary>
    Pt,
    /// <summary>Picas (<c>pc</c>), equal to 16 CSS pixels.</summary>
    Pc,
    /// <summary>A flexible grid fraction (<c>fr</c>); resolved by grid layout.</summary>
    Fr,
    /// <summary>A unitless number, used by <c>line-height</c> and <c>flex-grow</c>.</summary>
    Number,
    /// <summary>The <c>min-content</c> intrinsic sizing keyword.</summary>
    MinContent,
    /// <summary>The <c>max-content</c> intrinsic sizing keyword.</summary>
    MaxContent,
    /// <summary>The <c>fit-content</c> intrinsic sizing keyword.</summary>
    FitContent,
    /// <summary>The <c>stretch</c> keyword, plus the <c>-webkit-fill-available</c> alias.</summary>
    Stretch
}

/// <summary>
/// Everything needed to turn a relative CSS length into pixels. Layout passes a
/// fresh context down the tree so <c>em</c>, viewport units and percentages all
/// resolve against the correct basis.
/// </summary>
/// <remarks>
/// <para>
/// The context is a small immutable value type; derive a modified copy with
/// <see cref="WithFontSize(float)"/> or <see cref="WithPercentBasis(float)"/>
/// rather than mutating a shared instance. Being a value type makes it safe to
/// pass down the layout recursion without allocation.
/// </para>
/// <para>
/// <see cref="PercentBasis"/> is deliberately independent of the available
/// width and height: a percentage in <c>padding-top</c> or
/// <c>margin-left</c> resolves against the containing block's inline size, not
/// the property's own axis. A non-finite basis means "percentage cannot be
/// resolved here" and resolves to zero.
/// </para>
/// </remarks>
public readonly struct LengthContext
{
    /// <summary>Initialises a length resolution context.</summary>
    /// <param name="fontSize">Font size of the current element, in CSS pixels (used by <c>em</c>).</param>
    /// <param name="rootFontSize">Font size of the root element, in CSS pixels (used by <c>rem</c>).</param>
    /// <param name="viewportWidth">
    /// Layout viewport width in CSS pixels, used by <c>vw</c>/<c>vmin</c>/<c>vmax</c>.
    /// </param>
    /// <param name="viewportHeight">
    /// Layout viewport height in CSS pixels, used by <c>vh</c>/<c>vmin</c>/<c>vmax</c>.
    /// </param>
    /// <param name="percentBasis">
    /// Basis for percentage lengths, in CSS pixels. Pass
    /// <see cref="float.NaN"/> when the basis is not yet known.
    /// </param>
    /// <param name="availableWidth">
    /// Available inline size for content, in CSS pixels; infinite when unconstrained.
    /// </param>
    /// <param name="availableHeight">
    /// Available block size for content, in CSS pixels; infinite when unconstrained.
    /// </param>
    public LengthContext(
        float fontSize = 16,
        float rootFontSize = 16,
        float viewportWidth = 0,
        float viewportHeight = 0,
        float percentBasis = float.NaN,
        float availableWidth = float.PositiveInfinity,
        float availableHeight = float.PositiveInfinity)
    {
        FontSize = fontSize;
        RootFontSize = rootFontSize;
        ViewportWidth = viewportWidth;
        ViewportHeight = viewportHeight;
        PercentBasis = percentBasis;
        AvailableWidth = availableWidth;
        AvailableHeight = availableHeight;
    }

    /// <summary>Gets the current element's font size in CSS pixels (for <c>em</c>).</summary>
    public float FontSize { get; init; }

    /// <summary>Gets the root element's font size in CSS pixels (for <c>rem</c>).</summary>
    public float RootFontSize { get; init; }

    /// <summary>Gets the layout viewport width in CSS pixels, used to resolve <c>vw</c>.</summary>
    public float ViewportWidth { get; init; }

    /// <summary>Gets the layout viewport height in CSS pixels, used to resolve <c>vh</c>.</summary>
    public float ViewportHeight { get; init; }

    /// <summary>
    /// Gets the basis for percentage lengths in CSS pixels, or
    /// <see cref="float.NaN"/> when percentages are not resolvable.
    /// </summary>
    public float PercentBasis { get; init; }

    /// <summary>Gets the available inline size in CSS pixels, or positive infinity.</summary>
    public float AvailableWidth { get; init; }

    /// <summary>Gets the available block size in CSS pixels, or positive infinity.</summary>
    public float AvailableHeight { get; init; }

    /// <summary>Returns a copy of this context with a different element font size.</summary>
    /// <param name="fontSize">New font size in CSS pixels, used to resolve <c>em</c>.</param>
    /// <returns>A new context sharing every other value with this instance.</returns>
    public LengthContext WithFontSize(float fontSize) => this with { FontSize = fontSize };

    /// <summary>Returns a copy of this context with a different percentage basis.</summary>
    /// <param name="basis">
    /// New basis in CSS pixels, or <see cref="float.NaN"/> to mark it unknown.
    /// </param>
    /// <returns>A new context sharing every other value with this instance.</returns>
    public LengthContext WithPercentBasis(float basis) => this with { PercentBasis = basis };
}

/// <summary>
/// A parsed CSS length. Simple values stay inline; <c>calc()</c>,
/// <c>min()</c>, <c>max()</c> and <c>clamp()</c> keep a small expression tree
/// that is built once when the declaration is first seen.
/// </summary>
/// <remarks>
/// <para>
/// The struct is immutable and cheap to copy. When <see cref="Expression"/> is
/// non-<see langword="null"/>, <see cref="Value"/> and <see cref="Unit"/> are
/// ignored by <see cref="Resolve(in LengthContext)"/>; the expression tree is
/// re-evaluated against each context, which is necessary because a declaration
/// can be inherited or reused with a different percentage basis.
/// </para>
/// <para>
/// Percentages are never pre-multiplied: they store the percentage itself (for
/// example <c>50</c> for <c>50%</c>) and are multiplied by the basis at
/// resolution time.
/// </para>
/// </remarks>
public readonly struct CssLength
{
    /// <summary>Initialises a length from a numeric value and unit.</summary>
    /// <param name="value">Numeric component in <paramref name="unit"/>.</param>
    /// <param name="unit">Unit or sentinel keyword.</param>
    /// <param name="expression">
    /// Optional <c>calc()</c>-style expression that takes precedence over the
    /// literal value.
    /// </param>
    internal CssLength(float value, LengthUnit unit, CssExpression? expression = null)
    {
        Value = value;
        Unit = unit;
        Expression = expression;
    }

    /// <summary>Gets the numeric component, interpreted according to <see cref="Unit"/>.</summary>
    public float Value { get; }

    /// <summary>Gets the unit or sentinel keyword that qualifies <see cref="Value"/>.</summary>
    public LengthUnit Unit { get; }

    /// <summary>
    /// Gets the optional expression tree for <c>calc()</c>, <c>min()</c>,
    /// <c>max()</c> or <c>clamp()</c>.
    /// </summary>
    internal CssExpression? Expression { get; }

    /// <summary>The <c>auto</c> keyword; the layout algorithm decides the size.</summary>
    public static readonly CssLength Auto = new(0, LengthUnit.Auto);

    /// <summary>A zero pixel length, <c>0px</c>.</summary>
    public static readonly CssLength Zero = new(0, LengthUnit.Px);

    /// <summary>The <c>min-content</c> intrinsic sizing keyword.</summary>
    public static readonly CssLength MinContent = new(0, LengthUnit.MinContent);

    /// <summary>The <c>max-content</c> intrinsic sizing keyword.</summary>
    public static readonly CssLength MaxContent = new(0, LengthUnit.MaxContent);

    /// <summary>The <c>stretch</c> keyword, treated as a definite fill of the containing block.</summary>
    public static readonly CssLength Stretch = new(0, LengthUnit.Stretch);

    /// <summary>Gets a value indicating whether this length is the <c>auto</c> keyword.</summary>
    public bool IsAuto => Unit == LengthUnit.Auto;

    /// <summary>Gets a value indicating whether this length is a percentage.</summary>
    public bool IsPercent => Unit == LengthUnit.Percent;

    /// <summary>Gets a value indicating whether this length is a grid fraction (<c>fr</c>).</summary>
    public bool IsFr => Unit == LengthUnit.Fr;

    /// <summary>Gets a value indicating whether this length is a unitless number.</summary>
    public bool IsNumber => Unit == LengthUnit.Number;

    /// <summary>
    /// Gets a value indicating whether this length carries a
    /// <c>calc()</c>-style expression tree.
    /// </summary>
    public bool IsExpression => Expression is not null;

    /// <summary>
    /// Gets a value indicating whether this length is one of the intrinsic
    /// sizing keywords (<c>min-content</c>, <c>max-content</c>,
    /// <c>fit-content</c> or <c>stretch</c>).
    /// </summary>
    public bool IsIntrinsic => Unit is LengthUnit.MinContent
        or LengthUnit.MaxContent
        or LengthUnit.FitContent
        or LengthUnit.Stretch;

    /// <summary>Creates a pixel length.</summary>
    /// <param name="value">Value in CSS pixels.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Px"/>.</returns>
    public static CssLength Px(float value) => new(value, LengthUnit.Px);

    /// <summary>Creates a percentage length.</summary>
    /// <param name="value">Percentage value, where <c>50</c> means <c>50%</c>.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Percent"/>.</returns>
    public static CssLength Percent(float value) => new(value, LengthUnit.Percent);

    /// <summary>Creates an <c>em</c> length.</summary>
    /// <param name="value">Multiple of the element's font size.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Em"/>.</returns>
    public static CssLength Em(float value) => new(value, LengthUnit.Em);

    /// <summary>Creates a <c>rem</c> length.</summary>
    /// <param name="value">Multiple of the root font size.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Rem"/>.</returns>
    public static CssLength Rem(float value) => new(value, LengthUnit.Rem);

    /// <summary>Creates a unitless number.</summary>
    /// <param name="value">The number itself.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Number"/>.</returns>
    public static CssLength Number(float value) => new(value, LengthUnit.Number);

    /// <summary>Creates a grid fraction length.</summary>
    /// <param name="value">Fraction of the free space, where <c>1</c> means <c>1fr</c>.</param>
    /// <returns>A <see cref="CssLength"/> in <see cref="LengthUnit.Fr"/>.</returns>
    public static CssLength Fr(float value) => new(value, LengthUnit.Fr);

    /// <summary>
    /// Resolves this length to CSS pixels using the context's own percentage
    /// basis.
    /// </summary>
    /// <param name="context">Resolution context supplying font sizes, viewport and basis.</param>
    /// <returns>The resolved length in CSS pixels.</returns>
    /// <remarks>
    /// Delegates to <see cref="Resolve(in LengthContext, float)"/> with
    /// <see cref="LengthContext.PercentBasis"/>. Sentinels such as
    /// <c>auto</c> and the intrinsic keywords resolve to zero, so callers that
    /// need a fallback should use <see cref="ResolveOr(in LengthContext, float, float)"/>.
    /// </remarks>
    public float Resolve(in LengthContext context)
    {
        return Resolve(in context, context.PercentBasis);
    }

    /// <summary>
    /// Resolves this length to CSS pixels against an explicit percentage basis.
    /// </summary>
    /// <param name="context">
    /// Resolution context supplying font sizes and viewport dimensions.
    /// </param>
    /// <param name="percentBasis">Basis in CSS pixels for a percentage length.</param>
    /// <returns>The resolved length in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// Conversion factors follow CSS: <c>in</c> = 96 px, <c>cm</c> = 96/2.54 px,
    /// <c>mm</c> = 96/25.4 px, <c>pt</c> = 96/72 px and <c>pc</c> = 16 px.
    /// </para>
    /// <para>
    /// <c>ex</c> and <c>ch</c> are approximated as half the font size because
    /// the engine has no font metrics at layout time. A percentage with a
    /// non-finite basis resolves to zero rather than <see cref="float.NaN"/>,
    /// keeping arithmetic downstream finite. An expression tree, when present,
    /// is evaluated with <paramref name="percentBasis"/> substituted into a
    /// derived context.
    /// </para>
    /// </remarks>
    public float Resolve(in LengthContext context, float percentBasis)
    {
        if (Expression is not null)
        {
            var scoped = context.WithPercentBasis(percentBasis);
            return Expression.Resolve(in scoped);
        }

        return Unit switch
        {
            LengthUnit.Px or LengthUnit.Number => Value,
            LengthUnit.Percent => float.IsFinite(percentBasis) ? Value * percentBasis * 0.01f : 0,
            LengthUnit.Em => Value * context.FontSize,
            LengthUnit.Rem => Value * context.RootFontSize,
            LengthUnit.Ex or LengthUnit.Ch => Value * context.FontSize * 0.5f,
            LengthUnit.Vw => Value * context.ViewportWidth * 0.01f,
            LengthUnit.Vh => Value * context.ViewportHeight * 0.01f,
            LengthUnit.Vmin => Value * MathF.Min(context.ViewportWidth, context.ViewportHeight) * 0.01f,
            LengthUnit.Vmax => Value * MathF.Max(context.ViewportWidth, context.ViewportHeight) * 0.01f,
            LengthUnit.Cm => Value * (96f / 2.54f),
            LengthUnit.Mm => Value * (96f / 25.4f),
            LengthUnit.In => Value * 96f,
            LengthUnit.Pt => Value * (96f / 72f),
            LengthUnit.Pc => Value * 16f,
            _ => 0
        };
    }

    /// <summary>
    /// Resolves a length, returning <paramref name="fallback"/> for auto,
    /// intrinsic keywords and percentages with no resolvable basis.
    /// </summary>
    /// <param name="context">
    /// Resolution context supplying font sizes and viewport dimensions.
    /// </param>
    /// <param name="percentBasis">Basis in CSS pixels for a percentage length.</param>
    /// <param name="fallback">Value in CSS pixels to use when this length is not definite.</param>
    /// <returns>The resolved length, or <paramref name="fallback"/>.</returns>
    /// <remarks>
    /// Use this instead of testing <see cref="IsAuto"/> and then calling
    /// <see cref="Resolve(in LengthContext)"/>: it also covers <c>fr</c>, the
    /// intrinsic keywords and unresolved percentages. A sentinel unit returns
    /// <paramref name="fallback"/> before any expression is evaluated. When the
    /// basis is not finite, an expression that contains a percentage also returns
    /// the fallback instead of silently dropping the percentage term.
    /// </remarks>
    public float ResolveOr(in LengthContext context, float percentBasis, float fallback)
    {
        if (Unit is LengthUnit.Auto or LengthUnit.MinContent or LengthUnit.MaxContent
            or LengthUnit.FitContent or LengthUnit.Stretch or LengthUnit.Fr)
        {
            return fallback;
        }

        if (!float.IsFinite(percentBasis)
            && (Unit == LengthUnit.Percent || Expression?.HasPercent == true))
        {
            // An expression such as calc(100% - 10px) is just as indefinite as
            // a literal percentage when the basis is unknown; resolving it would
            // silently drop the percentage term.
            return fallback;
        }

        return Resolve(in context, percentBasis);
    }

    /// <summary>Gets a value indicating whether this length resolves to a concrete pixel value.</summary>
    /// <param name="percentBasis">Basis in CSS pixels for a percentage length.</param>
    /// <returns>
    /// <see langword="false"/> for <c>auto</c>, <c>fr</c> and the intrinsic
    /// keywords, and for a percentage (literal or inside an expression) when
    /// <paramref name="percentBasis"/> is not finite; otherwise <see langword="true"/>.
    /// </returns>
    public bool IsDefinite(float percentBasis)
    {
        if (Unit is LengthUnit.Auto or LengthUnit.MinContent or LengthUnit.MaxContent
            or LengthUnit.FitContent or LengthUnit.Stretch or LengthUnit.Fr)
        {
            return false;
        }

        return float.IsFinite(percentBasis)
            || (Unit != LengthUnit.Percent && Expression?.HasPercent != true);
    }

    /// <summary>Returns a short CSS-like textual form of this length, for diagnostics.</summary>
    /// <returns>
    /// Text such as <c>12.5px</c>, <c>50%</c> or <c>auto</c>. Expression trees
    /// are rendered as the literal <c>calc(...)</c>.
    /// </returns>
    /// <remarks>Formatting uses the invariant culture and at most two decimal places.</remarks>
    public override string ToString()
    {
        if (Expression is not null)
        {
            return "calc(...)";
        }

        return Unit switch
        {
            LengthUnit.Auto => "auto",
            LengthUnit.Percent => $"{Value:0.##}%",
            LengthUnit.Px => $"{Value:0.##}px",
            LengthUnit.Em => $"{Value:0.##}em",
            LengthUnit.Rem => $"{Value:0.##}rem",
            LengthUnit.Fr => $"{Value:0.##}fr",
            LengthUnit.Number => $"{Value:0.##}",
            LengthUnit.MinContent => "min-content",
            LengthUnit.MaxContent => "max-content",
            LengthUnit.FitContent => "fit-content",
            LengthUnit.Stretch => "stretch",
            _ => $"{Value:0.##}{Unit.ToString().ToLowerInvariant()}"
        };
    }
}

/// <summary>
/// Base type for the small expression trees that back <c>calc()</c>,
/// <c>min()</c>, <c>max()</c> and <c>clamp()</c> lengths.
/// </summary>
/// <remarks>
/// <para>
/// Expressions are built once by <see cref="CssValueParser"/> when a
/// declaration is parsed and are then evaluated each time a length is
/// resolved, because <c>em</c>, percentages and viewport units depend on the
/// box being laid out. Trees are read-only after construction and can
/// therefore be shared between style values.
/// </para>
/// <para>
/// Evaluation is pure and allocation-free; implementations must not retain or
/// mutate the supplied <see cref="LengthContext"/>.
/// </para>
/// </remarks>
public abstract class CssExpression
{
    /// <summary>Evaluates this expression to a length in CSS pixels.</summary>
    /// <param name="context">
    /// Resolution context supplying font sizes, viewport dimensions and the
    /// percentage basis.
    /// </param>
    /// <returns>The computed value in CSS pixels.</returns>
    public abstract float Resolve(in LengthContext context);

    /// <summary>
    /// Gets a value indicating whether the expression depends on a percentage
    /// basis. Used by <see cref="CssLength.ResolveOr(in LengthContext, float, float)"/>
    /// to return its fallback when the basis is indefinite.
    /// </summary>
    public abstract bool HasPercent { get; }
}

/// <summary>
/// An expression node holding a single literal <see cref="CssLength"/>.
/// </summary>
/// <remarks>Used for numeric, dimension and percentage leaves inside a <c>calc()</c> tree.</remarks>
internal sealed class CssLiteralExpression : CssExpression
{
    private readonly CssLength _length;

    /// <summary>Wraps a literal length as an expression node.</summary>
    /// <param name="length">The literal to return when resolved.</param>
    public CssLiteralExpression(CssLength length) => _length = length;

    /// <inheritdoc/>
    public override float Resolve(in LengthContext context) => _length.Resolve(in context);

    /// <inheritdoc/>
    public override bool HasPercent => _length.Unit == LengthUnit.Percent;
}

/// <summary>
/// A unary sign expression, representing <c>+x</c> or <c>-x</c> inside a
/// <c>calc()</c>.
/// </summary>
internal sealed class CssUnaryExpression : CssExpression
{
    private readonly bool _negate;
    private readonly CssExpression _operand;

    /// <summary>Creates a unary expression.</summary>
    /// <param name="negate">
    /// <see langword="true"/> to negate the operand, <see langword="false"/> to
    /// keep its sign.
    /// </param>
    /// <param name="operand">Expression to evaluate.</param>
    public CssUnaryExpression(bool negate, CssExpression operand)
    {
        _negate = negate;
        _operand = operand;
    }

    /// <inheritdoc/>
    public override float Resolve(in LengthContext context)
    {
        var value = _operand.Resolve(in context);
        return _negate ? -value : value;
    }

    /// <inheritdoc/>
    public override bool HasPercent => _operand.HasPercent;
}

/// <summary>
/// A binary arithmetic expression (<c>+</c>, <c>-</c>, <c>*</c> or <c>/</c>)
/// inside a <c>calc()</c>.
/// </summary>
/// <remarks>
/// The parser binds <c>*</c> and <c>/</c> more tightly than <c>+</c> and
/// <c>-</c>, so the tree already encodes correct precedence. Division by zero
/// yields <c>0</c> instead of infinity or an exception, keeping layout finite.
/// </remarks>
internal sealed class CssBinaryExpression : CssExpression
{
    private readonly char _op;
    private readonly CssExpression _left;
    private readonly CssExpression _right;

    /// <summary>Creates a binary expression.</summary>
    /// <param name="op">One of <c>+</c>, <c>-</c>, <c>*</c> or <c>/</c>.</param>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    public CssBinaryExpression(char op, CssExpression left, CssExpression right)
    {
        _op = op;
        _left = left;
        _right = right;
    }

    /// <inheritdoc/>
    public override float Resolve(in LengthContext context)
    {
        var left = _left.Resolve(in context);
        var right = _right.Resolve(in context);

        return _op switch
        {
            '+' => left + right,
            '-' => left - right,
            '*' => left * right,
            '/' => right == 0 ? 0 : left / right,
            _ => 0
        };
    }

    /// <inheritdoc/>
    public override bool HasPercent => _left.HasPercent || _right.HasPercent;
}

/// <summary>Identifies which CSS sizing function an expression node represents.</summary>
internal enum CssFunctionKind : byte
{
    /// <summary>The <c>min()</c> function: smallest of its arguments.</summary>
    Min,
    /// <summary>The <c>max()</c> function: largest of its arguments.</summary>
    Max,
    /// <summary>The <c>clamp()</c> function: <c>min(max, max(min, value))</c>.</summary>
    Clamp
}

/// <summary>
/// An expression node for <c>min()</c>, <c>max()</c> and <c>clamp()</c>.
/// </summary>
/// <remarks>
/// Arguments are evaluated left to right. Empty argument lists resolve to zero,
/// and a <c>clamp()</c> with fewer than three arguments resolves its first
/// argument, mirroring the parser's tolerant recovery rather than failing.
/// </remarks>
internal sealed class CssFunctionExpression : CssExpression
{
    private readonly CssFunctionKind _kind;
    private readonly CssExpression[] _arguments;

    /// <summary>Creates a sizing-function expression.</summary>
    /// <param name="kind">Which function to evaluate.</param>
    /// <param name="arguments">Resolved-order argument list; must not be mutated afterwards.</param>
    public CssFunctionExpression(CssFunctionKind kind, CssExpression[] arguments)
    {
        _kind = kind;
        _arguments = arguments;
    }

    /// <inheritdoc/>
    public override float Resolve(in LengthContext context)
    {
        return _kind switch
        {
            CssFunctionKind.Min => Min(in context),
            CssFunctionKind.Max => Max(in context),
            CssFunctionKind.Clamp => Clamp(in context),
            _ => 0
        };
    }

    /// <inheritdoc/>
    public override bool HasPercent
    {
        get
        {
            for (var i = 0; i < _arguments.Length; i++)
            {
                if (_arguments[i].HasPercent)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Resolves the smallest argument, or zero for an empty list.</summary>
    /// <param name="context">Resolution context forwarded to each argument.</param>
    /// <returns>The minimum value in CSS pixels.</returns>
    private float Min(in LengthContext context)
    {
        var value = float.PositiveInfinity;

        for (var i = 0; i < _arguments.Length; i++)
        {
            value = MathF.Min(value, _arguments[i].Resolve(in context));
        }

        return float.IsPositiveInfinity(value) ? 0 : value;
    }

    /// <summary>Resolves the largest argument, or zero for an empty list.</summary>
    /// <param name="context">Resolution context forwarded to each argument.</param>
    /// <returns>The maximum value in CSS pixels.</returns>
    private float Max(in LengthContext context)
    {
        var value = float.NegativeInfinity;

        for (var i = 0; i < _arguments.Length; i++)
        {
            value = MathF.Max(value, _arguments[i].Resolve(in context));
        }

        return float.IsNegativeInfinity(value) ? 0 : value;
    }

    /// <summary>Clamps the middle argument between the first and third.</summary>
    /// <param name="context">Resolution context forwarded to each argument.</param>
    /// <returns>
    /// <c>max(min, min(value, max))</c>, the first argument when fewer than
    /// three were supplied, or zero when there are none.
    /// </returns>
    private float Clamp(in LengthContext context)
    {
        if (_arguments.Length < 3)
        {
            return _arguments.Length == 0 ? 0 : _arguments[0].Resolve(in context);
        }

        var minimum = _arguments[0].Resolve(in context);
        var value = _arguments[1].Resolve(in context);
        var maximum = _arguments[2].Resolve(in context);

        return MathF.Max(minimum, MathF.Min(value, maximum));
    }
}

/// <summary>Four CSS lengths, used for margins, padding, insets and border widths.</summary>
/// <param name="Top">Top edge length.</param>
/// <param name="Right">Right edge length.</param>
/// <param name="Bottom">Bottom edge length.</param>
/// <param name="Left">Left edge length.</param>
/// <remarks>
/// The component order is the CSS shorthand order (top, right, bottom, left).
/// Values are stored unresolved so a declaration can be shared where the
/// percentage basis differs; use <see cref="Resolve(in LengthContext, float)"/>
/// to obtain pixels.
/// </remarks>
public readonly record struct CssLength4(CssLength Top, CssLength Right, CssLength Bottom, CssLength Left)
{
    /// <summary>An <c>auto</c> value on all four sides.</summary>
    public static readonly CssLength4 Auto = new(CssLength.Auto, CssLength.Auto, CssLength.Auto, CssLength.Auto);

    /// <summary>A zero pixel value on all four sides.</summary>
    public static readonly CssLength4 Zero = new(CssLength.Zero, CssLength.Zero, CssLength.Zero, CssLength.Zero);

    /// <summary>Gets the length at a shorthand position.</summary>
    /// <param name="index">Position: 0 = top, 1 = right, 2 = bottom, 3 = left.</param>
    /// <value>The length stored at <paramref name="index"/>.</value>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="index"/> is outside the range 0 to 3.
    /// </exception>
    public CssLength this[int index] => index switch
    {
        0 => Top,
        1 => Right,
        2 => Bottom,
        3 => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    /// <summary>Returns a copy with a different top length.</summary>
    /// <param name="value">New top length.</param>
    /// <returns>A new value sharing the other three sides.</returns>
    public CssLength4 WithTop(CssLength value) => new(value, Right, Bottom, Left);

    /// <summary>Returns a copy with a different right length.</summary>
    /// <param name="value">New right length.</param>
    /// <returns>A new value sharing the other three sides.</returns>
    public CssLength4 WithRight(CssLength value) => new(Top, value, Bottom, Left);

    /// <summary>Returns a copy with a different bottom length.</summary>
    /// <param name="value">New bottom length.</param>
    /// <returns>A new value sharing the other three sides.</returns>
    public CssLength4 WithBottom(CssLength value) => new(Top, Right, value, Left);

    /// <summary>Returns a copy with a different left length.</summary>
    /// <param name="value">New left length.</param>
    /// <returns>A new value sharing the other three sides.</returns>
    public CssLength4 WithLeft(CssLength value) => new(Top, Right, Bottom, value);

    /// <summary>Resolves all four sides to CSS pixels.</summary>
    /// <param name="context">Resolution context supplying font sizes and viewport dimensions.</param>
    /// <param name="percentBasis">Basis in CSS pixels for percentage sides.</param>
    /// <returns>A <see cref="Thickness"/> in CSS pixels.</returns>
    /// <remarks>
    /// <c>auto</c>, intrinsic keywords and unresolved percentages resolve to
    /// zero. For margins and paddings, CSS resolves all four percentage sides
    /// against the containing block's inline size, so pass that single size as
    /// <paramref name="percentBasis"/> even for vertical sides; other
    /// properties should pass the basis their grammar specifies.
    /// </remarks>
    public Thickness Resolve(in LengthContext context, float percentBasis)
    {
        return new Thickness(
            Top.ResolveOr(in context, percentBasis, 0),
            Right.ResolveOr(in context, percentBasis, 0),
            Bottom.ResolveOr(in context, percentBasis, 0),
            Left.ResolveOr(in context, percentBasis, 0));
    }
}

/// <summary>Four CSS corner radii. Percentages resolve against the box size.</summary>
/// <param name="TopLeft">Top-left corner radius.</param>
/// <param name="TopRight">Top-right corner radius.</param>
/// <param name="BottomRight">Bottom-right corner radius.</param>
/// <param name="BottomLeft">Bottom-left corner radius.</param>
/// <remarks>
/// The CSS <c>border-radius</c> shorthand allows a horizontal and a vertical
/// radius per corner separated by <c>/</c>; this type stores only the
/// horizontal component, matching the engine's circular-corner support.
/// </remarks>
public readonly record struct CssCornerRadius4(
    CssLength TopLeft,
    CssLength TopRight,
    CssLength BottomRight,
    CssLength BottomLeft)
{
    /// <summary>A zero radius on all four corners.</summary>
    public static readonly CssCornerRadius4 Zero = new(
        CssLength.Zero,
        CssLength.Zero,
        CssLength.Zero,
        CssLength.Zero);

    /// <summary>Resolves all four radii to CSS pixels.</summary>
    /// <param name="context">Resolution context supplying font sizes and viewport dimensions.</param>
    /// <param name="width">Border-box width in CSS pixels.</param>
    /// <param name="height">Border-box height in CSS pixels.</param>
    /// <returns>A <see cref="CornerRadius"/> in CSS pixels.</returns>
    /// <remarks>
    /// CSS resolves percentage radii against the corresponding box dimension,
    /// but this implementation uses the smaller of the two so corners stay
    /// circular and cannot overlap along the short axis. The basis is clamped
    /// to at least zero, and sentinel or unresolved values become zero.
    /// </remarks>
    public CornerRadius Resolve(in LengthContext context, float width, float height)
    {
        var basis = MathF.Max(0, MathF.Min(width, height));

        return new CornerRadius(
            TopLeft.ResolveOr(in context, basis, 0),
            TopRight.ResolveOr(in context, basis, 0),
            BottomRight.ResolveOr(in context, basis, 0),
            BottomLeft.ResolveOr(in context, basis, 0));
    }
}

/// <summary>Factory and shorthand helpers for <see cref="CssLength4"/> values.</summary>
public static class CssLengths
{
    /// <summary>
    /// Parses a one-to-four value CSS box shorthand such as <c>margin</c> or
    /// <c>padding</c>.
    /// </summary>
    /// <param name="value">Shorthand text, for example <c>8px 16px</c>.</param>
    /// <returns>
    /// The four sides expanded using the CSS rules: one value applies to all
    /// sides; two mean vertical then horizontal; three mean top, horizontal,
    /// bottom; four mean top, right, bottom, left.
    /// </returns>
    /// <remarks>
    /// Splitting respects nested parentheses, quotes and <c>url()</c> content,
    /// so a <c>calc()</c> containing spaces counts as one value. An empty or
    /// whitespace-only string yields <see cref="CssLength4.Zero"/>; a fifth and
    /// later value is ignored.
    /// </remarks>
    public static CssLength4 ParseShorthand(string value)
    {
        var parts = CssValueParser.SplitTopLevel(value, ' ');

        return parts.Count switch
        {
            0 => CssLength4.Zero,
            1 => All(CssValueParser.ParseLength(parts[0])),
            2 => new CssLength4(
                CssValueParser.ParseLength(parts[0]),
                CssValueParser.ParseLength(parts[1]),
                CssValueParser.ParseLength(parts[0]),
                CssValueParser.ParseLength(parts[1])),
            3 => new CssLength4(
                CssValueParser.ParseLength(parts[0]),
                CssValueParser.ParseLength(parts[1]),
                CssValueParser.ParseLength(parts[2]),
                CssValueParser.ParseLength(parts[1])),
            _ => new CssLength4(
                CssValueParser.ParseLength(parts[0]),
                CssValueParser.ParseLength(parts[1]),
                CssValueParser.ParseLength(parts[2]),
                CssValueParser.ParseLength(parts[3]))
        };
    }

    /// <summary>Creates a <see cref="CssLength4"/> with the same length on every side.</summary>
    /// <param name="value">Length to repeat.</param>
    /// <returns>A uniform four-side value.</returns>
    public static CssLength4 All(CssLength value) => new(value, value, value, value);
}
