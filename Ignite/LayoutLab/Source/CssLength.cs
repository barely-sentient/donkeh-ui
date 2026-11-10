namespace Ignite.LayoutLab;

public enum LengthUnit : byte
{
    Auto,
    Px,
    Percent,
    Em,
    Rem,
    Ex,
    Ch,
    Vw,
    Vh,
    Vmin,
    Vmax,
    Cm,
    Mm,
    In,
    Pt,
    Pc,
    Fr,
    Number,
    MinContent,
    MaxContent,
    FitContent,
    Stretch
}

/// <summary>
/// Everything needed to turn a relative CSS length into pixels. Layout passes a
/// fresh context down the tree so <c>em</c>, viewport units and percentages all
/// resolve against the correct basis.
/// </summary>
public readonly struct LengthContext
{
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

    public float FontSize { get; init; }
    public float RootFontSize { get; init; }
    public float ViewportWidth { get; init; }
    public float ViewportHeight { get; init; }
    public float PercentBasis { get; init; }
    public float AvailableWidth { get; init; }
    public float AvailableHeight { get; init; }

    public LengthContext WithFontSize(float fontSize) => this with { FontSize = fontSize };

    public LengthContext WithPercentBasis(float basis) => this with { PercentBasis = basis };
}

/// <summary>
/// A parsed CSS length. Simple values stay inline; <c>calc()</c>,
/// <c>min()</c>, <c>max()</c> and <c>clamp()</c> keep a small expression tree
/// that is built once when the declaration is first seen.
/// </summary>
public readonly struct CssLength
{
    internal CssLength(float value, LengthUnit unit, CssExpression? expression = null)
    {
        Value = value;
        Unit = unit;
        Expression = expression;
    }

    public float Value { get; }
    public LengthUnit Unit { get; }
    internal CssExpression? Expression { get; }

    public static readonly CssLength Auto = new(0, LengthUnit.Auto);
    public static readonly CssLength Zero = new(0, LengthUnit.Px);
    public static readonly CssLength MinContent = new(0, LengthUnit.MinContent);
    public static readonly CssLength MaxContent = new(0, LengthUnit.MaxContent);
    public static readonly CssLength Stretch = new(0, LengthUnit.Stretch);

    public bool IsAuto => Unit == LengthUnit.Auto;
    public bool IsPercent => Unit == LengthUnit.Percent;
    public bool IsFr => Unit == LengthUnit.Fr;
    public bool IsNumber => Unit == LengthUnit.Number;
    public bool IsExpression => Expression is not null;

    public bool IsIntrinsic => Unit is LengthUnit.MinContent
        or LengthUnit.MaxContent
        or LengthUnit.FitContent
        or LengthUnit.Stretch;

    public static CssLength Px(float value) => new(value, LengthUnit.Px);
    public static CssLength Percent(float value) => new(value, LengthUnit.Percent);
    public static CssLength Em(float value) => new(value, LengthUnit.Em);
    public static CssLength Rem(float value) => new(value, LengthUnit.Rem);
    public static CssLength Number(float value) => new(value, LengthUnit.Number);
    public static CssLength Fr(float value) => new(value, LengthUnit.Fr);

    public float Resolve(in LengthContext context)
    {
        return Resolve(in context, context.PercentBasis);
    }

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
    public float ResolveOr(in LengthContext context, float percentBasis, float fallback)
    {
        if (Unit is LengthUnit.Auto or LengthUnit.MinContent or LengthUnit.MaxContent
            or LengthUnit.FitContent or LengthUnit.Stretch or LengthUnit.Fr)
        {
            return fallback;
        }

        if (Unit == LengthUnit.Percent && !float.IsFinite(percentBasis))
        {
            return fallback;
        }

        return Resolve(in context, percentBasis);
    }

    public bool IsDefinite(in LengthContext context, float percentBasis)
    {
        if (Unit is LengthUnit.Auto or LengthUnit.MinContent or LengthUnit.MaxContent
            or LengthUnit.FitContent or LengthUnit.Stretch or LengthUnit.Fr)
        {
            return false;
        }

        return Unit != LengthUnit.Percent || float.IsFinite(percentBasis);
    }

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
            _ => $"{Value:0.##}{Unit.ToString().ToLowerInvariant()}"
        };
    }
}

public abstract class CssExpression
{
    public abstract float Resolve(in LengthContext context);
}

internal sealed class CssLiteralExpression : CssExpression
{
    private readonly CssLength _length;

    public CssLiteralExpression(CssLength length) => _length = length;

    public override float Resolve(in LengthContext context) => _length.Resolve(in context);
}

internal sealed class CssUnaryExpression : CssExpression
{
    private readonly bool _negate;
    private readonly CssExpression _operand;

    public CssUnaryExpression(bool negate, CssExpression operand)
    {
        _negate = negate;
        _operand = operand;
    }

    public override float Resolve(in LengthContext context)
    {
        var value = _operand.Resolve(in context);
        return _negate ? -value : value;
    }
}

internal sealed class CssBinaryExpression : CssExpression
{
    private readonly char _op;
    private readonly CssExpression _left;
    private readonly CssExpression _right;

    public CssBinaryExpression(char op, CssExpression left, CssExpression right)
    {
        _op = op;
        _left = left;
        _right = right;
    }

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
}

internal enum CssFunctionKind : byte
{
    Min,
    Max,
    Clamp
}

internal sealed class CssFunctionExpression : CssExpression
{
    private readonly CssFunctionKind _kind;
    private readonly CssExpression[] _arguments;

    public CssFunctionExpression(CssFunctionKind kind, CssExpression[] arguments)
    {
        _kind = kind;
        _arguments = arguments;
    }

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

    private float Min(in LengthContext context)
    {
        var value = float.PositiveInfinity;

        for (var i = 0; i < _arguments.Length; i++)
        {
            value = MathF.Min(value, _arguments[i].Resolve(in context));
        }

        return float.IsPositiveInfinity(value) ? 0 : value;
    }

    private float Max(in LengthContext context)
    {
        var value = float.NegativeInfinity;

        for (var i = 0; i < _arguments.Length; i++)
        {
            value = MathF.Max(value, _arguments[i].Resolve(in context));
        }

        return float.IsNegativeInfinity(value) ? 0 : value;
    }

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
public readonly record struct CssLength4(CssLength Top, CssLength Right, CssLength Bottom, CssLength Left)
{
    public static readonly CssLength4 Auto = new(CssLength.Auto, CssLength.Auto, CssLength.Auto, CssLength.Auto);
    public static readonly CssLength4 Zero = new(CssLength.Zero, CssLength.Zero, CssLength.Zero, CssLength.Zero);

    public CssLength this[int index] => index switch
    {
        0 => Top,
        1 => Right,
        2 => Bottom,
        3 => Left,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };

    public CssLength4 WithTop(CssLength value) => new(value, Right, Bottom, Left);
    public CssLength4 WithRight(CssLength value) => new(Top, value, Bottom, Left);
    public CssLength4 WithBottom(CssLength value) => new(Top, Right, value, Left);
    public CssLength4 WithLeft(CssLength value) => new(Top, Right, Bottom, value);

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
public readonly record struct CssCornerRadius4(
    CssLength TopLeft,
    CssLength TopRight,
    CssLength BottomRight,
    CssLength BottomLeft)
{
    public static readonly CssCornerRadius4 Zero = new(
        CssLength.Zero,
        CssLength.Zero,
        CssLength.Zero,
        CssLength.Zero);

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

public static class CssLengths
{
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

    public static CssLength4 All(CssLength value) => new(value, value, value, value);
}
