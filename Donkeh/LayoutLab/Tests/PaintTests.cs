using Donkeh.Data.Dom;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for display-list emission: backgrounds, borders,
/// shadows, outlines, text, scroll scopes and the balanced clip/opacity/
/// transform/filter/blend stacks, plus stacking order and viewport anchoring.
/// </summary>
public class PaintTests
{
    private static LayoutResult Lay(string css, IgniteSupportedDomNode root,
        float width = 1000, float height = 800, bool optimise = false,
        bool frameMarkers = false)
    {
        var options = TestSupport.Options(width, height, optimise);
        options.TextMeasurer = TestSupport.FixedMeasurer.Instance;
        options.IncludeFrameMarkers = frameMarkers;
        return TestSupport.Layout(TestSupport.Build(css), root, options);
    }

    private static List<InstructionOpCode> OpCodes(LayoutResult result)
    {
        var codes = new List<InstructionOpCode>();

        foreach (var instruction in result.Opcodes)
        {
            codes.Add(instruction.OpCode);
        }

        return codes;
    }

    private static List<uint> Colors(LayoutResult result)
    {
        var colors = new List<uint>();

        foreach (var instruction in TestSupport.Instructions(result, InstructionOpCode.DrawRect))
        {
            colors.Add(unchecked((uint)instruction.Payload));
        }

        return colors;
    }

    // ---------------------------------------------------------- backgrounds

    [Fact]
    public void BackgroundPaintsItsBorderBox()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 100px; height: 50px; background: #ff0000; }", root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawRect);
        TestSupport.AssertRect(draw.Rect, 0, 0, 100, 50);
        Assert.Equal(0xFFFF0000u, unchecked((uint)draw.Payload));
        Assert.True((draw.Flags & InstructionFlags.Opaque) != 0);
    }

    [Fact]
    public void TransparentBackgroundEmitsNothing()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 100px; height: 50px; }", root);

        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.DrawRect));
    }

    [Fact]
    public void TranslucentBackgroundIsNotMarkedOpaque()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: rgba(255, 0, 0, 0.5); }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawRect);
        Assert.Equal(0x7FFF0000u, unchecked((uint)draw.Payload));
        Assert.True((draw.Flags & InstructionFlags.Opaque) == 0);
    }

    [Fact]
    public void RoundedBackgroundUsesARadiusConstant()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: #123456; border-radius: 10px; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawRoundedRect);
        Assert.Equal(new CornerRadius(10, 10, 10, 10), result.Constants.GetRadius(draw.Aux));
        Assert.True((draw.Flags & InstructionFlags.Rounded) != 0);
    }

    [Fact]
    public void BackgroundClipContentBoxUsesTheContentRect()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; padding: 10px; border: 5px solid #000;"
            + " background: #ff0000; background-clip: content-box; }",
            root);

        TestSupport.AssertRect(
            TestSupport.Single(result, InstructionOpCode.DrawRect).Rect, 15, 15, 100, 50);
    }

    [Fact]
    public void BackgroundClipPaddingBoxUsesThePaddingRect()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; padding: 10px; border: 5px solid #000;"
            + " background: #ff0000; background-clip: padding-box; }",
            root);

        TestSupport.AssertRect(
            TestSupport.Single(result, InstructionOpCode.DrawRect).Rect, 5, 5, 120, 70);
    }

    [Fact]
    public void BackgroundImageUrlEmitsABackgroundDraw()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background-image: url(a.png); }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawBackgroundImage);
        var spec = result.Constants.GetBackground(draw.Payload);
        Assert.Equal(ImageKind.Url, spec.Kind);
        Assert.Contains("a.png", spec.Url);
        TestSupport.AssertRect(draw.Rect, 0, 0, 100, 50);
    }

    [Fact]
    public void GradientBackgroundImageIsRepeatable()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px;"
            + " background-image: linear-gradient(red, blue); }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawBackgroundImage);
        var spec = result.Constants.GetBackground(draw.Payload);
        Assert.Equal(ImageKind.LinearGradient, spec.Kind);
        Assert.Equal(GradientKind.Linear, spec.GradientKind);
        Assert.Equal(2, spec.Stops.Length);
        Assert.True((draw.Flags & InstructionFlags.Repeatable) != 0);
    }

    [Fact]
    public void MultipleBackgroundLayersEachEmitADraw()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px;"
            + " background-image: url(a.png), url(b.png); }",
            root);

        Assert.Equal(2, TestSupport.Instructions(result, InstructionOpCode.DrawBackgroundImage).Count);
        Assert.Equal(2, result.Constants.Count);
    }

    [Fact]
    public void BackgroundSpecsAreRebuiltWhenLayerPropertiesChange()
    {
        var engine = new LayoutEngine(TestSupport.Build(
            "#root { width: 100px; height: 50px;"
            + " background-image: linear-gradient(red, blue);"
            + " background-repeat: repeat; }"));
        var root = TestSupport.Node("div", "root");
        var options = TestSupport.Options(optimise: false);
        options.TextMeasurer = TestSupport.FixedMeasurer.Instance;

        var first = engine.Layout(root, options);
        var style = TestSupport.Box(first, root).Style;

        var firstDraw = TestSupport.Single(first, InstructionOpCode.DrawBackgroundImage);
        Assert.Equal(
            BackgroundRepeat.Repeat,
            first.Constants.GetBackground(firstDraw.Payload).Repeat);

        // Mutating the live style in place must invalidate the cached specs.
        style.BackgroundRepeat = BackgroundRepeat.NoRepeat;
        var second = engine.Repaint(first, options);

        var secondDraw = TestSupport.Single(second, InstructionOpCode.DrawBackgroundImage);
        Assert.Equal(
            BackgroundRepeat.NoRepeat,
            second.Constants.GetBackground(secondDraw.Payload).Repeat);
    }

    // -------------------------------------------------------------- borders

    [Fact]
    public void BorderPaintsABorderSpec()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; border: 2px solid #0000ff; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawBorder);
        var spec = result.Constants.GetBorder(draw.Payload);

        Assert.Equal(2, spec.Top.Width, 3);
        Assert.Equal(BorderStyle.Solid, spec.Top.Style);
        Assert.Equal(0xFF0000FFu, spec.Top.Color);
        Assert.Equal(spec.Top, spec.Right);
        Assert.Equal(spec.Top, spec.Bottom);
        Assert.Equal(spec.Top, spec.Left);
    }

    [Fact]
    public void BorderCarriesTheCornerRadius()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; border: 1px solid #000; border-radius: 5px; }",
            root);

        var spec = result.Constants.GetBorder(
            TestSupport.Single(result, InstructionOpCode.DrawBorder).Payload);
        Assert.Equal(new CornerRadius(5, 5, 5, 5), spec.Radius);
    }

    [Fact]
    public void HiddenBorderStylePaintsNothing()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; border: 2px none #000; }",
            root);

        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.DrawBorder));
    }

    [Fact]
    public void OutlineIsInflatedByHalfWidthAndOffset()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; outline: 2px solid #ff0000;"
            + " outline-offset: 1px; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawOutline);
        TestSupport.AssertRect(draw.Rect, -2, -2, 104, 54);

        var spec = result.Constants.GetBorder(draw.Payload);
        Assert.Equal(2, spec.Top.Width, 3);
        Assert.Equal(0xFFFF0000u, spec.Top.Color);
    }

    [Fact]
    public void OutlineWithoutAStyleIsNotPainted()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 100px; height: 50px; outline-width: 2px; }", root);

        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.DrawOutline));
    }

    // -------------------------------------------------------------- shadows

    [Fact]
    public void OuterShadowPaintsBeforeTheBackground()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px;"
            + " box-shadow: 4px 5px 6px 7px #000; background: #fff; }",
            root);

        var codes = OpCodes(result);
        var shadowIndex = codes.IndexOf(InstructionOpCode.DrawShadow);
        var backgroundIndex = codes.IndexOf(InstructionOpCode.DrawRect);
        Assert.True(shadowIndex >= 0);
        Assert.True(shadowIndex < backgroundIndex);

        var spec = result.Constants.GetShadow(
            TestSupport.Single(result, InstructionOpCode.DrawShadow).Payload);
        Assert.Equal(new ShadowSpec(4, 5, 6, 7, 0xFF000000, false), spec);
    }

    [Fact]
    public void InsetShadowPaintsAfterTheBorder()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; border: 1px solid #000;"
            + " box-shadow: inset 1px 2px 3px 4px #000; background: #fff; }",
            root);

        var codes = OpCodes(result);
        Assert.True(codes.IndexOf(InstructionOpCode.DrawInsetShadow)
            > codes.IndexOf(InstructionOpCode.DrawBorder));

        var spec = result.Constants.GetShadow(
            TestSupport.Single(result, InstructionOpCode.DrawInsetShadow).Payload);
        Assert.True(spec.Inset);
        Assert.Equal(1, spec.OffsetX, 3);
        Assert.Equal(2, spec.OffsetY, 3);
        Assert.Equal(3, spec.Blur, 3);
        Assert.Equal(4, spec.Spread, 3);
    }

    [Fact]
    public void MultipleShadowsAreSplitIntoOuterAndInsetDraws()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px;"
            + " box-shadow: 1px 1px 1px #000, inset 2px 2px 2px #111, 3px 3px 3px #222; }",
            root);

        Assert.Equal(2, TestSupport.Instructions(result, InstructionOpCode.DrawShadow).Count);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.DrawInsetShadow));
    }

    // ----------------------------------------------------------------- text

    [Fact]
    public void TextPaintsOneDrawPerLine()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px; color: #112233; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawText);
        var command = result.Constants.GetText(draw.Payload);

        Assert.Equal("hi", command.Text);
        Assert.Equal(10, command.Style.FontSize, 3);
        Assert.Equal(20, command.Style.LineHeight, 3);
        Assert.Equal(0xFF112233u, command.Style.Color);
        TestSupport.AssertRect(draw.Rect, 0, 0, 20, 20);
    }

    [Fact]
    public void WrappedTextPaintsEveryLine()
    {
        var root = TestSupport.Node("div", "root", "abcdefghijkl");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px;"
            + " overflow-wrap: anywhere; }",
            root);

        var draws = TestSupport.Instructions(result, InstructionOpCode.DrawText);
        Assert.Equal(2, draws.Count);
        Assert.Equal(0, draws[0].Y, 3);
        Assert.Equal(20, draws[1].Y, 3);
    }

    [Fact]
    public void UnderlinePaintsADecorationLine()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px;"
            + " text-decoration: underline; }",
            root);

        var line = TestSupport.Single(result, InstructionOpCode.DrawLine);
        Assert.Equal(17, line.Y, 3);
        Assert.Equal(20, line.Z, 3);
        Assert.Equal(1, line.W, 3);
    }

    [Fact]
    public void OverlinePaintsAtTheTopOfTheLine()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px;"
            + " text-decoration: overline; }",
            root);

        Assert.Equal(0, TestSupport.Single(result, InstructionOpCode.DrawLine).Y, 3);
    }

    [Fact]
    public void LineThroughPaintsThroughTheMiddle()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px;"
            + " text-decoration: line-through; }",
            root);

        Assert.Equal(9.6f, TestSupport.Single(result, InstructionOpCode.DrawLine).Y, 3);
    }

    [Fact]
    public void CombinedDecorationsPaintMultipleLines()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; line-height: 20px;"
            + " text-decoration: underline line-through; }",
            root);

        Assert.Equal(2, TestSupport.Instructions(result, InstructionOpCode.DrawLine).Count);
    }

    [Fact]
    public void RightToLeftTextSetsTheFlag()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var result = Lay(
            "#root { width: 100px; font-size: 10px; direction: rtl; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawText);
        Assert.True((draw.Flags & InstructionFlags.RightToLeft) != 0);
    }

    // ------------------------------------------------------- visibility/order

    [Fact]
    public void HiddenVisibilitySkipsOwnPixelsButPaintsChildren()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { visibility: hidden; background: #ff0000; }"
            + " #child { visibility: visible; width: 10px; height: 10px; background: #0000ff; }",
            root);

        var colors = Colors(result);
        Assert.Single(colors);
        Assert.Equal(0xFF0000FFu, colors[0]);
    }

    [Fact]
    public void DisplayNoneChildPaintsNothing()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { width: 100px; height: 50px; }"
            + " #child { display: none; background: #ff0000; }",
            root);

        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.DrawRect));
    }

    [Fact]
    public void ZIndexOrdersPaintLayers()
    {
        var root = TestSupport.Node("div", "root");
        var red = TestSupport.Node("div", "red");
        var green = TestSupport.Node("div", "green");
        var blue = TestSupport.Node("div", "blue");
        root.Children = [red, green, blue];

        var result = Lay(
            "#red { z-index: 5; width: 10px; height: 10px; background: #ff0000; }"
            + " #green { z-index: -1; width: 10px; height: 10px; background: #00ff00; }"
            + " #blue { z-index: 0; width: 10px; height: 10px; background: #0000ff; }",
            root);

        Assert.Equal(
            [0xFF00FF00u, 0xFF0000FFu, 0xFFFF0000u],
            Colors(result));
    }

    [Fact]
    public void PositiveZDescendantEscapesANormalFlowParent()
    {
        var root = TestSupport.Node("div", "root");
        var outer = TestSupport.Node("div", "outer");
        var inner = TestSupport.Node("div", "inner");
        var sibling = TestSupport.Node("div", "sibling");
        root.Children = [outer, sibling];
        outer.Children = [inner];

        var result = Lay(
            "#inner { position: relative; z-index: 10; width: 10px; height: 10px;"
            + " background: #ff0000; }"
            + " #sibling { position: relative; z-index: 1; width: 10px; height: 10px;"
            + " background: #0000ff; }",
            root);

        Assert.Equal([0xFF0000FFu, 0xFFFF0000u], Colors(result));
    }

    [Fact]
    public void PositiveZDescendantCannotEscapeAnOpacityContext()
    {
        var root = TestSupport.Node("div", "root");
        var outer = TestSupport.Node("div", "outer");
        var inner = TestSupport.Node("div", "inner");
        var sibling = TestSupport.Node("div", "sibling");
        root.Children = [outer, sibling];
        outer.Children = [inner];

        var result = Lay(
            "#outer { opacity: 0.5; }"
            + " #inner { position: relative; z-index: 10; width: 10px; height: 10px;"
            + " background: #ff0000; }"
            + " #sibling { position: relative; z-index: 1; width: 10px; height: 10px;"
            + " background: #0000ff; }",
            root);

        // The red box belongs to #outer's stacking context, which paints below
        // the blue sibling even though the red box's own z-index is higher.
        Assert.Equal([0xFFFF0000u, 0xFF0000FFu], Colors(result));
    }

    [Fact]
    public void FixedBoxInheritsAnOpacityAncestor()
    {
        var root = TestSupport.Node("div", "root");
        var fixedChild = TestSupport.Node("div", "fixed");
        root.Children = [fixedChild];

        var result = Lay(
            "#root { opacity: 0.5; height: 100px; }"
            + " #fixed { position: fixed; left: 0; top: 0; width: 10px; height: 10px;"
            + " background: #0000ff; }",
            root);

        var codes = OpCodes(result);
        var blue = codes.FindIndex(code => code == InstructionOpCode.DrawRect);
        var push = codes.LastIndexOf(InstructionOpCode.PushOpacity);
        var pop = codes.LastIndexOf(InstructionOpCode.PopOpacity);

        Assert.True(push >= 0);
        Assert.True(push < blue);
        Assert.True(blue < pop);
        Assert.Equal(2, codes.Count(code => code == InstructionOpCode.PushOpacity));
    }

    [Fact]
    public void BackgroundThenBorderThenTextThenChildren()
    {
        var root = TestSupport.Node("div", "root", "hi");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { width: 100px; height: 50px; background: #fff;"
            + " border: 1px solid #000; font-size: 10px; line-height: 20px; }"
            + " #child { width: 10px; height: 10px; background: #0000ff; }",
            root);

        var codes = OpCodes(result);
        var background = codes.IndexOf(InstructionOpCode.DrawRect);
        var border = codes.IndexOf(InstructionOpCode.DrawBorder);
        var text = codes.IndexOf(InstructionOpCode.DrawText);
        var childDraw = codes.LastIndexOf(InstructionOpCode.DrawRect);

        Assert.True(background < border);
        Assert.True(border < text);
        Assert.True(text < childDraw);
    }

    // -------------------------------------------------------------- scopes

    [Fact]
    public void OpacityEmitsAPushAndPop()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: #fff; opacity: 0.5; }",
            root);

        var codes = OpCodes(result);
        Assert.Equal(InstructionOpCode.PushOpacity, codes[0]);
        Assert.Equal(InstructionOpCode.PopOpacity, codes[^1]);
        Assert.Equal(0.5f, result.Opcodes[0].X, 3);
    }

    [Fact]
    public void FullyOpaqueDoesNotPushAnOpacityScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 10px; height: 10px; opacity: 1; }", root);

        Assert.DoesNotContain(InstructionOpCode.PushOpacity, OpCodes(result));
    }

    [Fact]
    public void TransformEmitsAPushWithAResolvedMatrix()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: #fff;"
            + " transform: translateX(10px); }",
            root);

        var codes = OpCodes(result);
        Assert.Equal(InstructionOpCode.PushTransform, codes[0]);
        Assert.Equal(InstructionOpCode.PopTransform, codes[^1]);

        var matrix = result.Constants.GetTransform(result.Opcodes[0].Payload);
        Assert.Equal(10, matrix.M31, 3);
    }

    [Fact]
    public void IdentityTransformDoesNotPushAScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 10px; height: 10px; transform: none; }", root);

        Assert.DoesNotContain(InstructionOpCode.PushTransform, OpCodes(result));
    }

    [Fact]
    public void FilterEmitsAScopedPush()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: #fff; filter: blur(2px); }",
            root);

        var push = TestSupport.Single(result, InstructionOpCode.PushFilter);
        var spec = result.Constants.GetFilter(push.Payload);
        Assert.Equal(FilterFunctionKind.Blur, spec.Functions[0].Kind);
        Assert.Equal(2, spec.Functions[0].Amount, 3);
        TestSupport.AssertRect(push.Rect, 0, 0, 100, 50);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopFilter));
    }

    [Fact]
    public void BackdropFilterEmitsItsOwnScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; backdrop-filter: brightness(0.5); }",
            root);

        var push = TestSupport.Single(result, InstructionOpCode.PushBackdropFilter);
        var spec = result.Constants.GetFilter(push.Payload);
        Assert.Equal(FilterFunctionKind.Brightness, spec.Functions[0].Kind);
        Assert.Equal(0.5f, spec.Functions[0].Amount, 3);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopBackdropFilter));
    }

    [Fact]
    public void ClipPathEmitsAClipPathScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; clip-path: circle(50%); }",
            root);

        var push = TestSupport.Single(result, InstructionOpCode.PushClipPath);
        var spec = result.Constants.GetClipPath(push.Payload);
        Assert.Equal(ClipPathKind.Circle, spec.Kind);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopClipPath));
    }

    [Fact]
    public void MaskEmitsAMaskScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; mask: url(m.png); }",
            root);

        var push = TestSupport.Single(result, InstructionOpCode.PushMask);
        var spec = result.Constants.GetMask(push.Payload);
        Assert.Equal(ImageKind.Url, spec.Kind);
        Assert.Contains("m.png", spec.Source);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopMask));
    }

    [Fact]
    public void BlendModeEmitsABlendScope()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; mix-blend-mode: multiply; }",
            root);

        var push = TestSupport.Single(result, InstructionOpCode.PushBlendMode);
        Assert.Equal((int)MixBlendMode.Multiply, push.Payload);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopBlendMode));
    }

    [Fact]
    public void ScopesNestAndUnwindInReverseOrder()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay(
            "#root { width: 100px; height: 50px; background: #fff; opacity: 0.5;"
            + " transform: translateX(10px); filter: blur(1px); }",
            root);

        var codes = OpCodes(result);
        var pushOpacity = codes.IndexOf(InstructionOpCode.PushOpacity);
        var pushTransform = codes.IndexOf(InstructionOpCode.PushTransform);
        var pushFilter = codes.IndexOf(InstructionOpCode.PushFilter);
        var popFilter = codes.IndexOf(InstructionOpCode.PopFilter);
        var popTransform = codes.IndexOf(InstructionOpCode.PopTransform);
        var popOpacity = codes.IndexOf(InstructionOpCode.PopOpacity);

        Assert.True(pushOpacity < pushTransform);
        Assert.True(pushTransform < pushFilter);
        Assert.True(pushFilter < popFilter);
        Assert.True(popFilter < popTransform);
        Assert.True(popTransform < popOpacity);
    }

    // ------------------------------------------------------------ scrolling

    [Fact]
    public void ScrollContainerPushesAPaddingBoxClip()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #child { height: 300px; background: #0f0; }",
            root);

        var clip = TestSupport.Single(result, InstructionOpCode.PushClipRect);
        TestSupport.AssertRect(clip.Rect, 0, 0, 1000, 100);
        Assert.Single(TestSupport.Instructions(result, InstructionOpCode.PopClip));
    }

    [Fact]
    public void ScrollOffsetShiftsChildrenAndSurvivesAsAnInstruction()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];
        root.ScrollOffsetY = 30;

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #child { height: 300px; background: #0f0; }",
            root);

        var offset = TestSupport.Single(result, InstructionOpCode.SetScrollOffset);
        Assert.Equal(30, offset.Y, 3);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawRect);
        TestSupport.AssertRect(draw.Rect, 0, -30, 1000, 300);
    }

    [Fact]
    public void RoundedScrollContainerPushesARoundedClip()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; border-radius: 8px; }"
            + " #child { height: 300px; }",
            root);

        var clip = TestSupport.Single(result, InstructionOpCode.PushClipRoundedRect);
        Assert.Equal(new CornerRadius(8, 8, 8, 8), result.Constants.GetRadius(clip.Aux));
        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.PushClipRect));
    }

    [Fact]
    public void VerticalScrollbarUsesStyleColors()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; scrollbar-color: #ff0000 #0000ff; }"
            + " #child { height: 300px; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawScrollbar);
        var spec = result.Constants.GetScrollbar(draw.Payload);

        Assert.True(spec.Vertical);
        Assert.Equal(0xFFFF0000u, spec.ThumbColor);
        Assert.Equal(0xFF0000FFu, spec.TrackColor);
        Assert.Equal(10, spec.Width, 3);
        Assert.Equal(100, spec.TrackLength, 3);
    }

    [Fact]
    public void HorizontalOverflowDrawsAHorizontalScrollbar()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-x: auto; width: 100px; height: 100px; }"
            + " #child { width: 300px; height: 300px; }",
            root);

        var draw = TestSupport.Single(result, InstructionOpCode.DrawScrollbar);
        Assert.False(result.Constants.GetScrollbar(draw.Payload).Vertical);
    }

    [Fact]
    public void ScrollContainerWithoutOverflowDrawsNoScrollbar()
    {
        var root = TestSupport.Node("div", "root");
        var child = TestSupport.Node("div", "child");
        root.Children = [child];

        var result = Lay(
            "#root { overflow-y: auto; height: 300px; }"
            + " #child { height: 100px; }",
            root);

        Assert.Empty(TestSupport.Instructions(result, InstructionOpCode.DrawScrollbar));
    }

    [Fact]
    public void FixedChildIgnoresAncestorScrollTranslation()
    {
        var root = TestSupport.Node("div", "root");
        var flow = TestSupport.Node("div", "flow");
        var fixedChild = TestSupport.Node("div", "fixed");
        root.Children = [flow, fixedChild];
        root.ScrollOffsetY = 40;

        var result = Lay(
            "#root { overflow-y: auto; height: 100px; }"
            + " #flow { height: 300px; }"
            + " #fixed { position: fixed; left: 0; top: 0; width: 10px; height: 10px;"
            + " background: #0000ff; }",
            root);

        foreach (var draw in TestSupport.Instructions(result, InstructionOpCode.DrawRect))
        {
            if (unchecked((uint)draw.Payload) == 0xFF0000FFu)
            {
                TestSupport.AssertRect(draw.Rect, 0, 0, 10, 10);
                return;
            }
        }

        Assert.Fail("The fixed child was not painted.");
    }

    // ------------------------------------------------------------- markers

    [Fact]
    public void FrameMarkersWrapTheStream()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 10px; height: 10px; }", root, frameMarkers: true);

        Assert.Equal(InstructionOpCode.BeginFrame, result.Opcodes[0].OpCode);
        Assert.Equal(InstructionOpCode.EndFrame, result.Opcodes[^1].OpCode);
    }

    [Fact]
    public void FrameMarkersAreOptional()
    {
        var root = TestSupport.Node("div", "root");
        var result = Lay("#root { width: 10px; height: 10px; }", root);

        Assert.DoesNotContain(InstructionOpCode.BeginFrame, OpCodes(result));
        Assert.DoesNotContain(InstructionOpCode.EndFrame, OpCodes(result));
    }
}
