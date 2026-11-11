using System.Numerics;

namespace Ignite.LayoutLab;

/// <summary>
/// A parsed transform list. Percentages are kept unresolved because CSS
/// resolves them against the border box, which is only known after layout.
/// </summary>
/// <remarks>
/// <para>
/// The operations are stored in source order.
/// <see cref="Resolve(float, float, in LengthContext)"/> post-multiplies each
/// operation's matrix onto the accumulated matrix, so with
/// <see cref="System.Numerics"/>' row-vector convention the first function in
/// the list is applied to a point first.
/// </para>
/// <para>
/// The class has no mutators, but <see cref="Operations"/> is exposed as a
/// shared array and should be treated as read-only. The spec carries no
/// origin: the paint stage applies <c>transform-origin</c> as a separate
/// translate sandwich around this matrix.
/// </para>
/// </remarks>
public sealed class TransformSpec
{
    /// <summary>Initialises a transform list.</summary>
    /// <param name="operations">
    /// Operations in source order. The array is stored by reference and must
    /// not be mutated afterwards.
    /// </param>
    public TransformSpec(TransformOperation[] operations)
    {
        Operations = operations;
    }

    /// <summary>Gets the transform operations in source order.</summary>
    public TransformOperation[] Operations { get; }

    /// <summary>A transform list with no operations, equivalent to <c>transform: none</c>.</summary>
    public static readonly TransformSpec Identity = new([]);

    /// <summary>Gets a value indicating whether the list is empty.</summary>
    public bool IsIdentity => Operations.Length == 0;

    /// <summary>Computes the combined matrix for a laid-out box.</summary>
    /// <param name="width">
    /// Border-box width in CSS pixels, used as the percentage basis for the x
    /// axis.
    /// </param>
    /// <param name="height">
    /// Border-box height in CSS pixels, used as the percentage basis for the y
    /// axis.
    /// </param>
    /// <param name="context">
    /// Length context for em/rem/viewport units inside the transform arguments.
    /// </param>
    /// <returns>The accumulated row-vector transform matrix.</returns>
    /// <remarks>
    /// Iterates the operations once, allocating nothing. Each operation is
    /// resolved against the supplied box size rather than the element's content
    /// size, matching CSS percentage semantics for transforms.
    /// </remarks>
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

/// <summary>
/// One function in a transform list, with its arguments stored unresolved.
/// </summary>
/// <param name="Kind">Which transform function this is.</param>
/// <param name="X">
/// Primary argument. For <see cref="TransformKind.Translate"/> and
/// <see cref="TransformKind.Scale"/> it is the x component; for
/// <see cref="TransformKind.Rotate"/> and <see cref="TransformKind.Skew"/> it
/// is unused; for <see cref="TransformKind.Matrix"/> it is unused.
/// </param>
/// <param name="Y">
/// Secondary argument. Translation y for <see cref="TransformKind.Translate"/>,
/// scale y for <see cref="TransformKind.Scale"/> (auto means "copy x"), the
/// skew-y angle in degrees for <see cref="TransformKind.Skew"/>, and unused
/// otherwise.
/// </param>
/// <param name="AngleDegrees">
/// Rotation or skew-x angle in degrees for <see cref="TransformKind.Rotate"/>
/// and <see cref="TransformKind.Skew"/>; unused otherwise.
/// </param>
/// <param name="Matrix">
/// Pre-built matrix for <see cref="TransformKind.Matrix"/>; ignored for other kinds.
/// </param>
/// <remarks>
/// Lengths stay unresolved so font- and percentage-relative values can be
/// evaluated against the final box. The record is small and copyable; the
/// embedded <see cref="Matrix3x2"/> is a value type, so no allocation occurs
/// during resolution.
/// </remarks>
public readonly record struct TransformOperation(
    TransformKind Kind,
    CssLength X,
    CssLength Y,
    float AngleDegrees,
    Matrix3x2 Matrix)
{
    /// <summary>Resolves this operation into a <see cref="System.Numerics"/> matrix.</summary>
    /// <param name="width">Border-box width in CSS pixels, used as the x percentage basis.</param>
    /// <param name="height">Border-box height in CSS pixels, used as the y percentage basis.</param>
    /// <param name="context">Length context for font- and viewport-relative arguments.</param>
    /// <returns>The operation's transform matrix in row-vector form.</returns>
    /// <remarks>
    /// <para>
    /// <see cref="TransformKind.Translate"/> creates a translation;
    /// <see cref="TransformKind.Scale"/> builds a scale where an auto y copies
    /// the x factor (this represents the single-argument <c>scale()</c>);
    /// <see cref="TransformKind.Rotate"/> converts degrees to radians around
    /// the origin; <see cref="TransformKind.Skew"/> uses
    /// <see cref="AngleDegrees"/> for x and the resolved <see cref="Y"/> number
    /// for y; <see cref="TransformKind.Matrix"/> returns <see cref="Matrix"/>
    /// unchanged.
    /// </para>
    /// <para>
    /// The parser also stores <c>scaleX()</c> with an auto Y, so it currently
    /// resolves to a uniform scale on both axes rather than horizontal-only
    /// scaling. <c>scaleY()</c> sets an explicit X of 1 and is unaffected.
    /// </para>
    /// <para>
    /// An unrecognised kind returns <see cref="Matrix3x2.Identity"/>. Invalid
    /// or missing arguments resolve to zero through
    /// <see cref="CssLength.ResolveOr(in LengthContext, float, float)"/>, so a
    /// malformed declaration degrades to a degenerate transform rather than
    /// throwing.
    /// </para>
    /// </remarks>
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

/// <summary>The CSS transform function families supported by the engine.</summary>
public enum TransformKind : byte
{
    /// <summary><c>translate()</c>, <c>translateX()</c> and <c>translateY()</c>.</summary>
    Translate,
    /// <summary><c>scale()</c>, <c>scaleX()</c> and <c>scaleY()</c>.</summary>
    Scale,
    /// <summary><c>rotate()</c> and <c>rotateZ()</c>; only 2D rotation is supported.</summary>
    Rotate,
    /// <summary><c>skew()</c> and its single-axis forms; angles are in degrees.</summary>
    Skew,
    /// <summary><c>matrix()</c> with six values; 3D <c>matrix3d()</c> is not supported.</summary>
    Matrix
}
