using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>
/// A parsed transform list. Percentages are kept unresolved because CSS
/// resolves them against the border box, which is only known after layout.
/// </summary>
public sealed class TransformSpec
{
    public TransformSpec(TransformOperation[] operations)
    {
        Operations = operations;
    }

    public TransformOperation[] Operations { get; }

    public static readonly TransformSpec Identity = new([]);

    public bool IsIdentity => Operations.Length == 0;

    public Matrix3x2 Resolve(float width, float height, in LengthContext context)
    {
        var matrix = Matrix3x2.Identity;

        foreach (var operation in Operations)
        {
            matrix *= operation.ToMatrix(width, height, in context);
        }

        return matrix;
    }
}

public readonly record struct TransformOperation(
    TransformKind Kind,
    CssLength X,
    CssLength Y,
    float AngleDegrees,
    Matrix3x2 Matrix)
{
    public Matrix3x2 ToMatrix(float width, float height, in LengthContext context)
    {
        var x = X.ResolveOr(in context, width, 0);
        var y = Y.ResolveOr(in context, height, 0);

        return Kind switch
        {
            TransformKind.Translate => Matrix3x2.CreateTranslation(x, y),
            TransformKind.Scale => Matrix3x2.CreateScale(x, Y.IsAuto ? x : y),
            TransformKind.Rotate => Matrix3x2.CreateRotation(AngleDegrees * (MathF.PI / 180f)),
            TransformKind.Skew => Matrix3x2.CreateSkew(
                AngleDegrees * (MathF.PI / 180f),
                y * (MathF.PI / 180f)),
            TransformKind.Matrix => Matrix,
            _ => Matrix3x2.Identity
        };
    }
}

public enum TransformKind : byte
{
    Translate,
    Scale,
    Rotate,
    Skew,
    Matrix
}
