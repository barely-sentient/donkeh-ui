namespace Donkeh.LayoutLab.Tests;

public class InstructionTests
{
    [Fact]
    public void ConstructorStoresEveryField()
    {
        var instruction = new Instruction(
            InstructionOpCode.DrawRect,
            1,
            2,
            3,
            4,
            5,
            6,
            InstructionFlags.Rounded);

        Assert.Equal(InstructionOpCode.DrawRect, instruction.OpCode);
        Assert.Equal(1, instruction.X);
        Assert.Equal(2, instruction.Y);
        Assert.Equal(3, instruction.Z);
        Assert.Equal(4, instruction.W);
        Assert.Equal(5, instruction.Payload);
        Assert.Equal((ushort)6, instruction.Aux);
        Assert.Equal(InstructionFlags.Rounded, instruction.Flags);
        Assert.Equal(new Rect(1, 2, 3, 4), instruction.Rect);
    }

    [Theory]
    [InlineData(InstructionOpCode.DrawRect, true)]
    [InlineData(InstructionOpCode.DrawRoundedRect, true)]
    [InlineData(InstructionOpCode.DrawScrollbar, true)]
    [InlineData(InstructionOpCode.DrawShadow, true)]
    [InlineData(InstructionOpCode.PushClipRect, false)]
    [InlineData(InstructionOpCode.PopClip, false)]
    [InlineData(InstructionOpCode.Nop, false)]
    public void IsDrawCoversTheDrawRange(InstructionOpCode opCode, bool expected)
    {
        var instruction = new Instruction(opCode);
        Assert.Equal(expected, instruction.IsDraw);
        Assert.Equal(expected, instruction.HasBounds);
    }

    [Theory]
    [InlineData(InstructionOpCode.PushClipRect, true)]
    [InlineData(InstructionOpCode.PushBlendMode, true)]
    [InlineData(InstructionOpCode.PushOpacity, true)]
    [InlineData(InstructionOpCode.PopClip, false)]
    [InlineData(InstructionOpCode.DrawRect, false)]
    public void IsPushCoversThePushRange(InstructionOpCode opCode, bool expected)
    {
        Assert.Equal(expected, new Instruction(opCode).IsPush);
    }

    [Theory]
    [InlineData(InstructionOpCode.PopClip, true)]
    [InlineData(InstructionOpCode.PopBlendMode, true)]
    [InlineData(InstructionOpCode.PopMask, true)]
    [InlineData(InstructionOpCode.PushClipRect, false)]
    [InlineData(InstructionOpCode.DrawRect, false)]
    public void IsPopCoversThePopRange(InstructionOpCode opCode, bool expected)
    {
        Assert.Equal(expected, new Instruction(opCode).IsPop);
    }

    [Fact]
    public void WithersReplaceOneFieldAndPreserveTheRest()
    {
        var instruction = new Instruction(InstructionOpCode.DrawText, 1, 2, 3, 4, 5, 6, InstructionFlags.None);

        var translated = instruction.Translate(10, 20);
        Assert.Equal(11, translated.X);
        Assert.Equal(22, translated.Y);
        Assert.Equal(3, translated.Z);
        Assert.Equal(4, translated.W);
        Assert.Equal(5, translated.Payload);

        var flagged = instruction.WithFlags(InstructionFlags.RightToLeft);
        Assert.Equal(InstructionFlags.RightToLeft, flagged.Flags);
        Assert.Equal(instruction.Rect, flagged.Rect);

        var payload = instruction.WithPayload(99);
        Assert.Equal(99, payload.Payload);
        Assert.Equal(instruction.Flags, payload.Flags);

        var aux = instruction.WithAux(42);
        Assert.Equal((ushort)42, aux.Aux);
        Assert.Equal(instruction.Payload, aux.Payload);
    }

    [Fact]
    public void DrawRectSetsTheOpaqueFlagForFullyOpaqueColors()
    {
        var opaque = Instruction.DrawRect(new Rect(0, 0, 10, 10), 0xFFFF0000);
        var translucent = Instruction.DrawRect(new Rect(0, 0, 10, 10), 0x80FF0000);

        Assert.True(opaque.Flags.HasFlag(InstructionFlags.Opaque));
        Assert.False(translucent.Flags.HasFlag(InstructionFlags.Opaque));
        Assert.Equal(unchecked((int)0xFFFF0000), opaque.Payload);
    }

    [Fact]
    public void DrawRectMergesExplicitFlagsWithOpaque()
    {
        var instruction = Instruction.DrawRect(new Rect(0, 0, 1, 1), CssColor.Black, InstructionFlags.Repeatable);
        Assert.True(instruction.Flags.HasFlag(InstructionFlags.Repeatable));
        Assert.True(instruction.Flags.HasFlag(InstructionFlags.Opaque));
    }

    [Fact]
    public void DrawRoundedRectSetsRoundedAndCarriesTheRadiusConstant()
    {
        var instruction = Instruction.DrawRoundedRect(new Rect(1, 2, 3, 4), CssColor.White, 7);
        Assert.Equal(InstructionOpCode.DrawRoundedRect, instruction.OpCode);
        Assert.Equal((ushort)7, instruction.Aux);
        Assert.True(instruction.Flags.HasFlag(InstructionFlags.Rounded));
    }

    [Fact]
    public void PushHelpersPopulateTheExpectedFields()
    {
        var clip = Instruction.PushClip(new Rect(1, 2, 3, 4));
        Assert.Equal(InstructionOpCode.PushClipRect, clip.OpCode);
        Assert.Equal(new Rect(1, 2, 3, 4), clip.Rect);

        var rounded = Instruction.PushClipRounded(new Rect(1, 2, 3, 4), 5);
        Assert.Equal(InstructionOpCode.PushClipRoundedRect, rounded.OpCode);
        Assert.Equal((ushort)5, rounded.Aux);
        Assert.True(rounded.Flags.HasFlag(InstructionFlags.Rounded));

        var opacity = Instruction.PushOpacity(0.5f);
        Assert.Equal(InstructionOpCode.PushOpacity, opacity.OpCode);
        Assert.Equal(0.5f, opacity.X);

        var transform = Instruction.PushTransform(3);
        Assert.Equal(InstructionOpCode.PushTransform, transform.OpCode);
        Assert.Equal(3, transform.Payload);

        var filter = Instruction.PushFilter(new Rect(0, 0, 1, 1), 4);
        Assert.Equal(InstructionOpCode.PushFilter, filter.OpCode);
        Assert.Equal(4, filter.Payload);

        var backdrop = Instruction.PushBackdropFilter(new Rect(0, 0, 1, 1), 5);
        Assert.Equal(InstructionOpCode.PushBackdropFilter, backdrop.OpCode);
        Assert.Equal(5, backdrop.Payload);

        var clipPath = Instruction.PushClipPath(new Rect(0, 0, 1, 1), 6);
        Assert.Equal(InstructionOpCode.PushClipPath, clipPath.OpCode);
        Assert.Equal(6, clipPath.Payload);

        var mask = Instruction.PushMask(new Rect(0, 0, 1, 1), 7);
        Assert.Equal(InstructionOpCode.PushMask, mask.OpCode);
        Assert.Equal(7, mask.Payload);

        var blend = Instruction.PushBlendMode(MixBlendMode.Multiply);
        Assert.Equal(InstructionOpCode.PushBlendMode, blend.OpCode);
        Assert.Equal((int)MixBlendMode.Multiply, blend.Payload);

        var border = Instruction.DrawBorder(new Rect(0, 0, 1, 1), 8);
        Assert.Equal(InstructionOpCode.DrawBorder, border.OpCode);
        Assert.Equal(8, border.Payload);

        var text = Instruction.DrawText(new Rect(0, 0, 10, 12), 9, InstructionFlags.RightToLeft);
        Assert.Equal(InstructionOpCode.DrawText, text.OpCode);
        Assert.Equal(9, text.Payload);
        Assert.True(text.Flags.HasFlag(InstructionFlags.RightToLeft));
    }

    [Theory]
    [InlineData(InstructionOpCode.DrawRect, unchecked((int)0xFF000000), "DrawRect (1,2,3,4) #FF000000")]
    [InlineData(InstructionOpCode.DrawText, 5, "DrawText (1,2,3,4) text:5")]
    [InlineData(InstructionOpCode.PushClipRect, 0, "PushClipRect (1,2,3,4)")]
    [InlineData(InstructionOpCode.PushOpacity, 5, "PushOpacity 5")]
    public void ToStringFormatsEachInstructionFamily(
        InstructionOpCode opCode, int payload, string expected)
    {
        var instruction = new Instruction(opCode, 1, 2, 3, 4, payload);
        Assert.Equal(expected, instruction.ToString());
    }

    [Fact]
    public void BufferAddsAndExposesInstructions()
    {
        var buffer = new InstructionBuffer(4);
        buffer.Add(Instruction.DrawRect(new Rect(0, 0, 1, 1), CssColor.Black));
        buffer.Add(Instruction.PushClip(new Rect(0, 0, 1, 1)));

        Assert.Equal(2, buffer.Count);
        Assert.Equal(2, buffer.Span.Length);
        Assert.Equal(InstructionOpCode.PushClipRect, buffer[1].OpCode);

        buffer.Clear();
        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void BufferIndexerRejectsStaleCapacitySlots()
    {
        var buffer = new InstructionBuffer(8);
        buffer.Add(Instruction.DrawRect(new Rect(0, 0, 1, 1), CssColor.Black));
        buffer.Clear();

        Assert.Throws<IndexOutOfRangeException>(() => _ = buffer[0]);
    }

    [Fact]
    public void TranslateLeavesScalarInstructionsUnchanged()
    {
        var opacity = Instruction.PushOpacity(0.5f);

        Assert.Equal(0.5f, opacity.Translate(10, 20).X);
        Assert.Equal(0, opacity.Translate(10, 20).Y);
    }

    [Fact]
    public void BufferGrowsAndCopiesRanges()
    {
        var buffer = new InstructionBuffer(4);
        buffer.EnsureCapacity(40);
        Assert.True(buffer.Array.Length >= 40);

        Instruction[] source =
        [
            Instruction.DrawRect(new Rect(0, 0, 1, 1), CssColor.Black),
            Instruction.DrawRect(new Rect(1, 0, 1, 1), CssColor.White)
        ];

        buffer.AddRange(source);
        Assert.Equal(2, buffer.Count);
        Assert.Equal(source[1], buffer[1]);
    }

    [Fact]
    public void BufferSkipsInvisibleDraws()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRect(Rect.Empty, CssColor.Black);
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), CssColor.Transparent);
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), 0x00FFFFFF);
        buffer.AddDrawRect(new Rect(0, 0, 10, 10), CssColor.Black);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(InstructionOpCode.DrawRect, buffer[0].OpCode);
    }

    [Fact]
    public void BufferSkipsInvisibleRoundedDraws()
    {
        var buffer = new InstructionBuffer();
        buffer.AddDrawRoundedRect(Rect.Empty, CssColor.Black, 0);
        buffer.AddDrawRoundedRect(new Rect(0, 0, 10, 10), CssColor.Transparent, 0);
        buffer.AddDrawRoundedRect(new Rect(0, 0, 10, 10), CssColor.Black, 0);

        Assert.Equal(1, buffer.Count);
        Assert.Equal(InstructionOpCode.DrawRoundedRect, buffer[0].OpCode);
    }

    [Fact]
    public void BufferAlwaysPushesClipsButSkipsIdentityScopes()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushClip(Rect.Empty);
        buffer.AddPopClip();
        buffer.AddPushOpacity(1);
        buffer.AddPushOpacity(0.5f);
        buffer.AddPopOpacity();
        buffer.AddPushBlendMode(MixBlendMode.Normal);
        buffer.AddPushBlendMode(MixBlendMode.Screen);
        buffer.AddPopBlendMode();

        Assert.Equal(6, buffer.Count);
        Assert.Equal(InstructionOpCode.PushClipRect, buffer[0].OpCode);
        Assert.Equal(InstructionOpCode.PopClip, buffer[1].OpCode);
        Assert.Equal(InstructionOpCode.PushOpacity, buffer[2].OpCode);
        Assert.Equal(InstructionOpCode.PopOpacity, buffer[3].OpCode);
        Assert.Equal(InstructionOpCode.PushBlendMode, buffer[4].OpCode);
        Assert.Equal(InstructionOpCode.PopBlendMode, buffer[5].OpCode);
    }

    [Fact]
    public void BufferScopeHelpersAreBalanced()
    {
        var buffer = new InstructionBuffer();
        buffer.AddPushTransform(1);
        buffer.AddPopTransform();
        buffer.AddPushFilter(Rect.Empty, 2);
        buffer.AddPopFilter();
        buffer.AddPushBackdropFilter(Rect.Empty, 3);
        buffer.AddPopBackdropFilter();
        buffer.AddPushClipPath(Rect.Empty, 4);
        buffer.AddPopClipPath();
        buffer.AddPushMask(Rect.Empty, 5);
        buffer.AddPopMask();
        buffer.AddBeginFrame();
        buffer.AddEndFrame();

        Assert.Equal(12, buffer.Count);
        Assert.Equal(InstructionOpCode.PopTransform, buffer[1].OpCode);
        Assert.Equal(InstructionOpCode.PopFilter, buffer[3].OpCode);
        Assert.Equal(InstructionOpCode.PopBackdropFilter, buffer[5].OpCode);
        Assert.Equal(InstructionOpCode.PopClipPath, buffer[7].OpCode);
        Assert.Equal(InstructionOpCode.PopMask, buffer[9].OpCode);
        Assert.Equal(InstructionOpCode.BeginFrame, buffer[10].OpCode);
        Assert.Equal(InstructionOpCode.EndFrame, buffer[11].OpCode);
    }
}
