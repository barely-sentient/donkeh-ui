using System.Numerics;

namespace Donkeh.LayoutLab.Tests;

public class CssTransformTests
{
    private static readonly LengthContext Context = new(fontSize: 10, percentBasis: 100);

    private static TransformSpec Spec(params TransformOperation[] operations) => new(operations);

    [Fact]
    public void TheIdentitySpecResolvesToTheIdentityMatrix()
    {
        Assert.True(TransformSpec.Identity.IsIdentity);
        Assert.Empty(TransformSpec.Identity.Operations);
        Assert.Equal(Matrix3x2.Identity, TransformSpec.Identity.Resolve(100, 100, Context));
    }

    [Fact]
    public void TranslateUsesPixelsAndPercentages()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Translate,
            CssLength.Px(10),
            CssLength.Percent(50),
            0,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(200, 40, Context);
        Assert.Equal(10, matrix.M31, 3);
        Assert.Equal(20, matrix.M32, 3);
    }

    [Fact]
    public void TranslateDefaultsAMissingAxisToZero()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Translate,
            CssLength.Px(7),
            CssLength.Zero,
            0,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(10, 10, Context);
        Assert.Equal(7, matrix.M31, 3);
        Assert.Equal(0, matrix.M32, 3);
    }

    [Fact]
    public void ScaleWithOneValueScalesBothAxes()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Scale,
            CssLength.Number(2),
            CssLength.Auto,
            0,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(100, 100, Context);
        Assert.Equal(2, matrix.M11, 3);
        Assert.Equal(2, matrix.M22, 3);
    }

    [Fact]
    public void ScaleWithTwoValuesKeepsThemSeparate()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Scale,
            CssLength.Number(2),
            CssLength.Number(3),
            0,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(100, 100, Context);
        Assert.Equal(2, matrix.M11, 3);
        Assert.Equal(3, matrix.M22, 3);
    }

    [Fact]
    public void RotationInterpretsDegrees()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Rotate,
            CssLength.Zero,
            CssLength.Zero,
            90,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(100, 100, Context);
        Assert.Equal(0, matrix.M11, 3);
        Assert.Equal(1, matrix.M12, 3);
        Assert.Equal(-1, matrix.M21, 3);
        Assert.Equal(0, matrix.M22, 3);
    }

    [Fact]
    public void SkewInterpretsBothAnglesAsDegrees()
    {
        var spec = Spec(new TransformOperation(
            TransformKind.Skew,
            CssLength.Zero,
            CssLength.Number(45),
            45,
            Matrix3x2.Identity));

        var matrix = spec.Resolve(100, 100, Context);
        Assert.Equal(1, matrix.M11, 3);
        Assert.Equal(1, matrix.M12, 3);
        Assert.Equal(1, matrix.M21, 3);
        Assert.Equal(1, matrix.M22, 3);
    }

    [Fact]
    public void MatrixOperationUsesTheInlineMatrix()
    {
        var inline = new Matrix3x2(1, 2, 3, 4, 5, 6);
        var spec = Spec(new TransformOperation(
            TransformKind.Matrix,
            CssLength.Zero,
            CssLength.Zero,
            0,
            inline));

        Assert.Equal(inline, spec.Resolve(100, 100, Context));
    }

    [Fact]
    public void OperationsAreAppliedRightToLeft()
    {
        var spec = Spec(
            new TransformOperation(TransformKind.Translate, CssLength.Px(10), CssLength.Px(20), 0, Matrix3x2.Identity),
            new TransformOperation(TransformKind.Scale, CssLength.Number(2), CssLength.Number(2), 0, Matrix3x2.Identity));

        var matrix = spec.Resolve(100, 100, Context);
        var transformed = Vector2.Transform(new Vector2(1, 1), matrix);

        // CSS applies the rightmost function first: scale (1,1) to (2,2),
        // then translate by (10,20).
        Assert.Equal(12, transformed.X, 3);
        Assert.Equal(22, transformed.Y, 3);
    }

    [Fact]
    public void ScaleXOnlyScalesTheHorizontalAxis()
    {
        var spec = TestSupport.Style("transform: scaleX(2)").Transform;

        Assert.NotNull(spec);
        var matrix = spec!.Resolve(100, 100, Context);
        Assert.Equal(2, matrix.M11, 3);
        Assert.Equal(1, matrix.M22, 3);
    }

    [Fact]
    public void UnknownOperationKindsBehaveAsIdentity()
    {
        var spec = Spec(new TransformOperation((TransformKind)99, CssLength.Px(5), CssLength.Px(5), 30, Matrix3x2.Identity));
        Assert.Equal(Matrix3x2.Identity, spec.Resolve(100, 100, Context));
    }

    [Fact]
    public void TransformKindContainsEverySupportedOperation()
    {
        Assert.Contains(TransformKind.Translate, Enum.GetValues<TransformKind>());
        Assert.Contains(TransformKind.Scale, Enum.GetValues<TransformKind>());
        Assert.Contains(TransformKind.Rotate, Enum.GetValues<TransformKind>());
        Assert.Contains(TransformKind.Skew, Enum.GetValues<TransformKind>());
        Assert.Contains(TransformKind.Matrix, Enum.GetValues<TransformKind>());
    }
}
