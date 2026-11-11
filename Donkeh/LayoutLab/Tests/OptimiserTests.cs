using System.Numerics;

namespace Donkeh.LayoutLab.Tests;

/// <summary>
/// Specification tests for the instruction optimiser. Each test builds a small
/// instruction stream by hand (the layout tests already prove the engine emits
/// those streams) and checks the documented guarantees: culling never removes
/// visible work, merging only folds opaque draw rectangles, redundant scopes
/// disappear, and compaction rewrites payloads in place.
/// </summary>
public sealed class OptimiserTests
{
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0xFF0000FF;
    private const uint HalfRed = 0x80FF0000;
    private static readonly Rect Viewport = new(0, 0, 100, 100);

    private static LayoutOptions Options(
        bool optimise = true,
        bool clipToViewport = true,
        bool cullOffscreen = true,
        bool mergeDraws = true,
        bool compactConstants = false,
        bool includeFrameMarkers = false)
    {
        return new LayoutOptions
        {
            Optimise = optimise,
            ClipToViewport = clipToViewport,
            CullOffscreen = cullOffscreen,
            MergeDraws = mergeDraws,
            CompactConstants = compactConstants,
            IncludeFrameMarkers = includeFrameMarkers
        };
    }

    private static OptimisationResult Run(
        InstructionBuffer buffer,
        ConstantPool pool,
        LayoutOptions? options = null,
        Rect? viewport = null)
    {
        return Optimiser.Run(buffer, pool, viewport ?? Viewport, options ?? Options());
    }

    private static List<InstructionOpCode> Codes(InstructionBuffer buffer)
    {
        var codes = new List<InstructionOpCode>();

        foreach (var instruction in buffer.Span)
        {
            codes.Add(instruction.OpCode);
        }

        return codes;
    }

    // ------------------------------------------------------------- pass gating

    [Fact]
    public void DisabledOptimisationReturnsTheInputUntouched()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.Nop));
        buffer.AddDrawRect(new Rect(500, 500, 10, 10), Red);

        var pool = new ConstantPool();
        var result = Run(buffer, pool, Options(optimise: false));

        Assert.Same(buffer, result.Instructions);
        Assert.Same(pool, result.Constants);
        Assert.Equal(OptimisationStats.Empty, result.Stats);
        Assert.Equal(2, result.Instructions.Count);
    }

    [Fact]
    public void NopsAreRemoved()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.Nop));
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.Add(new Instruction(InstructionOpCode.Nop));

        var result = Run(buffer, new ConstantPool());

        Assert.Equal([InstructionOpCode.DrawRect], Codes(result.Instructions));
    }

    [Fact]
    public void FrameMarkersAreOptional()
    {
        var buffer = new InstructionBuffer();
        buffer.AddBeginFrame();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddEndFrame();

        var without = Run(buffer, new ConstantPool());
        var with = Run(
            buffer,
            new ConstantPool(),
            Options(includeFrameMarkers: true));

        Assert.Equal([InstructionOpCode.DrawRect], Codes(without.Instructions));
        Assert.Equal(
            [InstructionOpCode.BeginFrame, InstructionOpCode.DrawRect, InstructionOpCode.EndFrame],
            Codes(with.Instructions));
    }

    // --------------------------------------------------------------- culling

    [Fact]
    public void DrawsOutsideTheViewportAreCulled()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(500, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        Assert.Single(result.Instructions.Span.ToArray());
        Assert.Equal(1, result.Stats.Culled);
    }

    [Fact]
    public void PartiallyVisibleDrawsSurvive()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(-5, -5, 10, 10), Red);
        buffer.AddDrawRect(new Rect(95, 95, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Culled);
    }

    [Fact]
    public void CullOffscreenCanBeDisabled()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(500, 500, 10, 10), Red);

        var result = Run(
            buffer,
            new ConstantPool(),
            Options(cullOffscreen: false, clipToViewport: false));

        Assert.Single(result.Instructions.Span.ToArray());
        Assert.Equal(0, result.Stats.Culled);
    }

    [Fact]
    public void DrawsInsideAClipAreCulledWhenTheyMissIt()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(new Rect(0, 0, 50, 50));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddDrawRect(new Rect(70, 70, 10, 10), Blue);
        buffer.AddPopClip();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(1, result.Stats.Culled);
        Assert.Equal(
            [InstructionOpCode.PushClipRect, InstructionOpCode.DrawRect, InstructionOpCode.PopClip],
            Codes(result.Instructions));
    }

    [Fact]
    public void EmptyClipRemovesTheScopeAndItsContents()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(new Rect(200, 200, 50, 50));
        buffer.AddDrawRect(new Rect(200, 200, 10, 10), Red);
        buffer.AddPopClip();
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Blue);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal([InstructionOpCode.DrawRect], Codes(result.Instructions));
        Assert.Equal(1, result.Stats.Culled);
        Assert.Equal(1, result.Stats.ScopesRemoved);
    }

    [Fact]
    public void ClippingWithoutCullStillHidesAnEmptyClip()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(new Rect(200, 200, 50, 50));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddPopClip();

        var result = Run(
            buffer,
            new ConstantPool(),
            Options(cullOffscreen: false));

        Assert.Empty(result.Instructions.Span.ToArray());
        Assert.Equal(1, result.Stats.Culled);
    }

    // ------------------------------------------------------------- clip scopes

    [Fact]
    public void AClipEqualToTheCurrentClipIsRemoved()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(Viewport);
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddPopClip();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal([InstructionOpCode.DrawRect], Codes(result.Instructions));
        Assert.Equal(1, result.Stats.ScopesRemoved);
    }

    [Fact]
    public void NestedClipsIntersect()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(new Rect(0, 0, 50, 50));
        buffer.AddPushClip(new Rect(25, 25, 50, 50));
        buffer.AddDrawRect(new Rect(30, 30, 5, 5), Red);
        buffer.AddDrawRect(new Rect(5, 5, 5, 5), Blue);
        buffer.AddPopClip();
        buffer.AddPopClip();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(1, result.Stats.Culled);
        Assert.Equal(
            [
                InstructionOpCode.PushClipRect,
                InstructionOpCode.PushClipRect,
                InstructionOpCode.DrawRect,
                InstructionOpCode.PopClip,
                InstructionOpCode.PopClip
            ],
            Codes(result.Instructions));
    }

    [Fact]
    public void RoundedClipWithTheSameBoundsIsNotRedundant()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.PushClipRoundedRect, 0, 0, 100, 100));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.Add(new Instruction(InstructionOpCode.PopClip));

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(
            [InstructionOpCode.PushClipRoundedRect, InstructionOpCode.DrawRect, InstructionOpCode.PopClip],
            Codes(result.Instructions));
        Assert.Equal(0, result.Stats.ScopesRemoved);
    }

    [Fact]
    public void UnmatchedPopsAreDropped()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.PopClip));
        buffer.Add(new Instruction(InstructionOpCode.PopOpacity));
        buffer.Add(new Instruction(InstructionOpCode.PopTransform));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal([InstructionOpCode.DrawRect], Codes(result.Instructions));
    }

    // --------------------------------------------------------------- opacity

    [Fact]
    public void FullyOpaqueOpacityScopeIsRemoved()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.PushOpacity, 1));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.Add(new Instruction(InstructionOpCode.PopOpacity));

        var result = Run(buffer, new ConstantPool());

        Assert.Equal([InstructionOpCode.DrawRect], Codes(result.Instructions));
        Assert.Equal(1, result.Stats.ScopesRemoved);
    }

    [Fact]
    public void FractionalOpacityScopeIsKept()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushOpacity(0.5f);
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddPopOpacity();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(
            [InstructionOpCode.PushOpacity, InstructionOpCode.DrawRect, InstructionOpCode.PopOpacity],
            Codes(result.Instructions));
    }

    // ------------------------------------------------ transform/filter scopes

    [Fact]
    public void CullingIsSuspendedInsideATransform()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushTransform(0);
        buffer.AddDrawRect(new Rect(500, 500, 10, 10), Red);
        buffer.AddPopTransform();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(3, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Culled);
    }

    [Fact]
    public void CullingIsSuspendedInsideAFilter()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushFilter(new Rect(0, 0, 100, 100), 0);
        buffer.AddDrawRect(new Rect(500, 500, 10, 10), Red);
        buffer.AddPopFilter();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(3, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Culled);
    }

    [Fact]
    public void CullingResumesAfterTheFilterScopeCloses()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushFilter(new Rect(0, 0, 100, 100), 0);
        buffer.AddDrawRect(new Rect(500, 500, 10, 10), Red);
        buffer.AddPopFilter();
        buffer.AddDrawRect(new Rect(600, 600, 10, 10), Blue);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(1, result.Stats.Culled);
        Assert.Equal(
            [InstructionOpCode.PushFilter, InstructionOpCode.DrawRect, InstructionOpCode.PopFilter],
            Codes(result.Instructions));
    }

    // --------------------------------------------------------------- merging

    [Fact]
    public void AdjacentHorizontalOpaqueRectsMerge()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(10, 0, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        var draw = Assert.Single(result.Instructions.Span.ToArray());
        Assert.Equal(InstructionOpCode.DrawRect, draw.OpCode);
        TestSupport.AssertRect(draw.Rect, 0, 0, 20, 10);
        Assert.Equal(1, result.Stats.Merged);
    }

    [Fact]
    public void AdjacentVerticalOpaqueRectsMerge()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(0, 10, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        var draw = Assert.Single(result.Instructions.Span.ToArray());
        TestSupport.AssertRect(draw.Rect, 0, 0, 10, 20);
    }

    [Fact]
    public void OverlappingRectsMergeToTheirUnion()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(5, 0, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        TestSupport.AssertRect(Assert.Single(result.Instructions.Span.ToArray()).Rect, 0, 0, 15, 10);
    }

    [Fact]
    public void DisjointRectsDoNotMerge()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(50, 50, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Merged);
    }

    [Fact]
    public void DifferentColoursDoNotMerge()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(10, 0, 10, 10), Blue);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Merged);
    }

    [Fact]
    public void TranslucentRectsDoNotMerge()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), HalfRed);
        buffer.AddDrawRect(new Rect(10, 0, 10, 10), HalfRed);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Merged);
    }

    [Fact]
    public void MergeDrawsCanBeDisabled()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddDrawRect(new Rect(10, 0, 10, 10), Red);

        var result = Run(buffer, new ConstantPool(), Options(mergeDraws: false));

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Merged);
    }

    [Fact]
    public void AClipBoundaryPreventsMerging()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);
        buffer.AddPushClip(new Rect(0, 0, 50, 50));
        buffer.AddPopClip();
        buffer.AddDrawRect(new Rect(10, 0, 10, 10), Red);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, Codes(result.Instructions).Count(code => code == InstructionOpCode.DrawRect));
        Assert.Equal(0, result.Stats.Merged);
    }

    [Fact]
    public void RoundedRectsAreNeverMerged()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRoundedRect(new Rect(0, 0, 10, 10), Red, 0);
        buffer.AddDrawRoundedRect(new Rect(10, 0, 10, 10), Red, 0);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(2, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Merged);
    }

    // ------------------------------------------------------- shadow cull bounds

    [Fact]
    public void ShadowBleedIsUsedForCulling()
    {
        var pool = new ConstantPool();
        var shadow = pool.AddShadow(new ShadowSpec(0, 0, 30, 0, Red, false));

        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.DrawShadow, 105, 0, 20, 20, shadow));

        var result = Run(buffer, pool);

        Assert.Single(result.Instructions.Span.ToArray());
    }

    [Fact]
    public void DistantShadowsAreStillCulled()
    {
        var pool = new ConstantPool();
        var shadow = pool.AddShadow(new ShadowSpec(0, 0, 2, 0, Red, false));

        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.DrawShadow, 500, 500, 20, 20, shadow));

        var result = Run(buffer, pool);

        Assert.Empty(result.Instructions.Span.ToArray());
        Assert.Equal(1, result.Stats.Culled);
    }

    // --------------------------------------------------------- scroll and layers

    [Fact]
    public void ScrollOffsetsArePreserved()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.SetScrollOffset, 5, 6));

        var result = Run(buffer, new ConstantPool());

        var instruction = Assert.Single(result.Instructions.Span.ToArray());
        Assert.Equal(InstructionOpCode.SetScrollOffset, instruction.OpCode);
        Assert.Equal(5, instruction.X);
        Assert.Equal(6, instruction.Y);
    }

    [Fact]
    public void LayerScopesArePreserved()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.PushLayer));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.Add(new Instruction(InstructionOpCode.PopLayer));

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(
            [InstructionOpCode.PushLayer, InstructionOpCode.DrawRect, InstructionOpCode.PopLayer],
            Codes(result.Instructions));
    }

    [Fact]
    public void EveryDecorationScopeStaysBalanced()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushTransform(0);
        buffer.AddPushFilter(new Rect(0, 0, 100, 100), 0);
        buffer.AddPushBackdropFilter(new Rect(0, 0, 100, 100), 0);
        buffer.AddPushClipPath(new Rect(0, 0, 100, 100), 0);
        buffer.AddPushMask(new Rect(0, 0, 100, 100), 0);
        buffer.AddPushBlendMode(MixBlendMode.Multiply);
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddPopBlendMode();
        buffer.AddPopMask();
        buffer.AddPopClipPath();
        buffer.AddPopBackdropFilter();
        buffer.AddPopFilter();
        buffer.AddPopTransform();

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(13, result.Instructions.Count);
        Assert.Equal(0, result.Stats.Saved);
    }

    // ------------------------------------------------------------- compaction

    [Fact]
    public void CompactionKeepsOnlyReferencedConstants()
    {
        var pool = new ConstantPool();
        var used = pool.AddBorder(new BorderSpec(
            new BorderSide(1, BorderStyle.Solid, Red),
            BorderSide.None,
            BorderSide.None,
            BorderSide.None,
            default));
        pool.AddShadow(new ShadowSpec(0, 0, 1, 0, Blue, false));

        var buffer = new InstructionBuffer();
        buffer.Add(Instruction.DrawBorder(new Rect(0, 0, 10, 10), used));

        var result = Run(buffer, pool, Options(compactConstants: true));

        Assert.Equal(2, pool.Count);
        Assert.Equal(1, result.Constants.Count);
        Assert.Equal(0, result.Instructions[0].Payload);
        Assert.Equal(
            new BorderSide(1, BorderStyle.Solid, Red),
            result.Constants.GetBorder(0).Top);
    }

    [Fact]
    public void CompactionSharesRepeatedConstants()
    {
        var pool = new ConstantPool();
        var border = pool.AddBorder(new BorderSpec(
            new BorderSide(1, BorderStyle.Solid, Red),
            BorderSide.None,
            BorderSide.None,
            BorderSide.None,
            default));

        var buffer = new InstructionBuffer();
        buffer.Add(Instruction.DrawBorder(new Rect(0, 0, 10, 10), border));
        buffer.Add(Instruction.DrawBorder(new Rect(0, 0, 10, 10), border));

        var result = Run(buffer, pool, Options(compactConstants: true));

        Assert.Equal(1, result.Constants.Count);
        Assert.Equal(0, result.Instructions[0].Payload);
        Assert.Equal(0, result.Instructions[1].Payload);
    }

    [Fact]
    public void CompactionRemapsTextConstants()
    {
        var pool = new ConstantPool();
        var unused = pool.AddText("unused", TextStyle.Default);
        var used = pool.AddText("kept", TextStyle.Default);

        var buffer = new InstructionBuffer();
        buffer.Add(Instruction.DrawText(new Rect(0, 0, 40, 16), used));

        var result = Run(buffer, pool, Options(compactConstants: true));

        Assert.Equal(1, result.Constants.Count);
        var command = result.Constants.GetText(result.Instructions[0].Payload);
        Assert.Equal("kept", command.Text);
        Assert.Equal(0, result.Instructions[0].Payload);
    }

    [Fact]
    public void CompactionRemapsRadiusConstantsInAux()
    {
        var pool = new ConstantPool();
        pool.AddRadius(new CornerRadius(1, 1, 1, 1));
        var used = pool.AddRadius(new CornerRadius(4, 4, 4, 4));

        var buffer = new InstructionBuffer();
        buffer.Add(Instruction.DrawRoundedRect(
            new Rect(0, 0, 10, 10), Red, checked((ushort)used)));

        var result = Run(buffer, pool, Options(compactConstants: true));

        Assert.Equal(1, result.Constants.Count);
        Assert.Equal((ushort)0, result.Instructions[0].Aux);
        Assert.Equal(new CornerRadius(4, 4, 4, 4), result.Constants.GetRadius(0));
    }

    [Fact]
    public void CompactionLeavesColourPayloadsAlone()
    {
        var pool = new ConstantPool();

        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), Red);

        var result = Run(buffer, pool, Options(compactConstants: true));

        Assert.Equal(0, result.Constants.Count);
        Assert.Equal(unchecked((int)Red), result.Instructions[0].Payload);
    }

    [Fact]
    public void CompactionIsOffByDefault()
    {
        var pool = new ConstantPool();
        var used = pool.AddText("kept", TextStyle.Default);

        var buffer = new InstructionBuffer();
        buffer.Add(Instruction.DrawText(new Rect(0, 0, 40, 16), used));

        var result = Run(buffer, pool);

        Assert.Same(pool, result.Constants);
        Assert.Equal(used, result.Instructions[0].Payload);
    }

    // ------------------------------------------------------------------ stats

    [Fact]
    public void StatsDescribeTheReduction()
    {
        var buffer = new InstructionBuffer();
        buffer.Add(new Instruction(InstructionOpCode.Nop));
        buffer.AddDrawRect(new Rect(10, 10, 10, 10), Red);
        buffer.AddDrawRect(new Rect(20, 10, 10, 10), Red);
        buffer.AddDrawRect(new Rect(900, 900, 10, 10), Blue);

        var result = Run(buffer, new ConstantPool());

        Assert.Equal(4, result.Stats.InputInstructions);
        Assert.Equal(1, result.Stats.OutputInstructions);
        Assert.Equal(1, result.Stats.Culled);
        Assert.Equal(1, result.Stats.Merged);
        Assert.Equal(0, result.Stats.ScopesRemoved);
        Assert.Equal(3, result.Stats.Saved);
    }

    [Fact]
    public void SavedNeverGoesNegative()
    {
        Assert.Equal(0, new OptimisationStats(2, 5, 0, 0, 0).Saved);
        Assert.Equal(4, new OptimisationStats(9, 5, 0, 0, 0).Saved);
    }
}
