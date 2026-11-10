using System.Runtime.CompilerServices;

namespace Ignite.LayoutLab;

/// <summary>
/// A single paint instruction. The layout is fixed at 24 bytes so a frame's
/// instruction stream stays cache friendly and can be copied/translated in bulk.
/// </summary>
/// <remarks>
/// Four floats (<see cref="X"/>..<see cref="W"/>) cover the overwhelmingly
/// common rect/radius/opacity cases without touching the constant pool. Larger
/// payloads - text, gradients, borders, transforms - live in a
/// <see cref="ConstantPool"/> and are addressed by <see cref="Payload"/> or
/// <see cref="Aux"/>.
/// </remarks>
public readonly struct Instruction
{
    public Instruction(
        InstructionOpCode opCode,
        float x = 0,
        float y = 0,
        float z = 0,
        float w = 0,
        int payload = 0,
        ushort aux = 0,
        InstructionFlags flags = InstructionFlags.None)
    {
        OpCode = opCode;
        X = x;
        Y = y;
        Z = z;
        W = w;
        Payload = payload;
        Aux = aux;
        Flags = flags;
    }

    public InstructionOpCode OpCode { get; }

    public InstructionFlags Flags { get; }

    /// <summary>Small constant index, used for corner radii.</summary>
    public ushort Aux { get; }

    /// <summary>Packed colour, constant index, or layer id, depending on the opcode.</summary>
    public int Payload { get; }

    public float X { get; }
    public float Y { get; }
    public float Z { get; }
    public float W { get; }

    public Rect Rect => new(X, Y, Z, W);

    public bool IsDraw => OpCode is >= InstructionOpCode.DrawRect and <= InstructionOpCode.DrawScrollbar;

    public bool IsPush => OpCode is >= InstructionOpCode.PushClipRect and <= InstructionOpCode.PushBlendMode;

    public bool IsPop => OpCode is >= InstructionOpCode.PopClip and <= InstructionOpCode.PopBlendMode;

    public bool HasBounds => IsDraw;

    /// <summary>Returns a copy translated by <paramref name="dx"/>/<paramref name="dy"/>.</summary>
    public Instruction Translate(float dx, float dy)
    {
        return new Instruction(OpCode, X + dx, Y + dy, Z, W, Payload, Aux, Flags);
    }

    public Instruction WithFlags(InstructionFlags flags)
    {
        return new Instruction(OpCode, X, Y, Z, W, Payload, Aux, flags);
    }

    public Instruction WithPayload(int payload)
    {
        return new Instruction(OpCode, X, Y, Z, W, payload, Aux, Flags);
    }

    public Instruction WithAux(ushort aux)
    {
        return new Instruction(OpCode, X, Y, Z, W, Payload, aux, Flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction DrawRect(in Rect rect, uint color, InstructionFlags flags = InstructionFlags.None)
    {
        return new Instruction(
            InstructionOpCode.DrawRect,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            unchecked((int)color),
            flags: flags | (color >= 0xFF000000 ? InstructionFlags.Opaque : InstructionFlags.None));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction DrawRoundedRect(
        in Rect rect,
        uint color,
        ushort radiusConstant,
        InstructionFlags flags = InstructionFlags.None)
    {
        return new Instruction(
            InstructionOpCode.DrawRoundedRect,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            unchecked((int)color),
            radiusConstant,
            flags | InstructionFlags.Rounded);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction DrawBorder(in Rect rect, int borderConstant)
    {
        return new Instruction(
            InstructionOpCode.DrawBorder,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            borderConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction DrawText(in Rect rect, int textConstant, InstructionFlags flags = InstructionFlags.None)
    {
        return new Instruction(
            InstructionOpCode.DrawText,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            textConstant,
            flags: flags);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushClip(in Rect rect)
    {
        return new Instruction(InstructionOpCode.PushClipRect, rect.X, rect.Y, rect.Width, rect.Height);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushClipRounded(in Rect rect, ushort radiusConstant)
    {
        return new Instruction(
            InstructionOpCode.PushClipRoundedRect,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            aux: radiusConstant,
            flags: InstructionFlags.Rounded);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushOpacity(float opacity)
    {
        return new Instruction(InstructionOpCode.PushOpacity, opacity);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushTransform(int matrixConstant)
    {
        return new Instruction(InstructionOpCode.PushTransform, payload: matrixConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushFilter(in Rect rect, int filterConstant)
    {
        return new Instruction(
            InstructionOpCode.PushFilter,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            filterConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushBackdropFilter(in Rect rect, int filterConstant)
    {
        return new Instruction(
            InstructionOpCode.PushBackdropFilter,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            filterConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushClipPath(in Rect rect, int clipPathConstant)
    {
        return new Instruction(
            InstructionOpCode.PushClipPath,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            clipPathConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushMask(in Rect rect, int maskConstant)
    {
        return new Instruction(
            InstructionOpCode.PushMask,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            maskConstant);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushBlendMode(MixBlendMode mode)
    {
        return new Instruction(InstructionOpCode.PushBlendMode, payload: (int)mode);
    }

    public override string ToString()
    {
        return OpCode switch
        {
            InstructionOpCode.DrawRect or InstructionOpCode.DrawRoundedRect
                => $"{OpCode} ({X:0.##},{Y:0.##},{Z:0.##},{W:0.##}) #{Payload:X8}",
            InstructionOpCode.DrawText
                => $"{OpCode} ({X:0.##},{Y:0.##},{Z:0.##},{W:0.##}) text:{Payload}",
            InstructionOpCode.PushClipRect or InstructionOpCode.PushClipRoundedRect
                => $"{OpCode} ({X:0.##},{Y:0.##},{Z:0.##},{W:0.##})",
            _ => $"{OpCode} {Payload}"
        };
    }
}

/// <summary>
/// Growable instruction buffer with cached emit helpers. A frame is built into
/// one of these, then handed to the optimiser as a span.
/// </summary>
public sealed class InstructionBuffer
{
    private Instruction[] _items;
    private int _count;

    public InstructionBuffer(int capacity = 128)
    {
        _items = new Instruction[Math.Max(4, capacity)];
    }

    public int Count => _count;

    public Instruction[] Array => _items;

    public ReadOnlySpan<Instruction> Span => _items.AsSpan(0, _count);

    public ref Instruction this[int index] => ref _items[index];

    public void Clear() => _count = 0;

    public void EnsureCapacity(int required)
    {
        if (required <= _items.Length)
        {
            return;
        }

        var size = Math.Max(required, Math.Max(16, _items.Length * 2));
        System.Array.Resize(ref _items, size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in Instruction instruction)
    {
        if (_count == _items.Length)
        {
            EnsureCapacity(_count + 1);
        }

        _items[_count++] = instruction;
    }

    public void AddRange(ReadOnlySpan<Instruction> instructions)
    {
        EnsureCapacity(_count + instructions.Length);
        instructions.CopyTo(_items.AsSpan(_count));
        _count += instructions.Length;
    }

    public void AddDrawRect(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color & 0xFF000000) == 0)
        {
            return;
        }

        Add(Instruction.DrawRect(rect, color));
    }

    public void AddDrawRoundedRect(in Rect rect, uint color, ushort radiusConstant)
    {
        if (rect.IsEmpty || (color & 0xFF000000) == 0)
        {
            return;
        }

        Add(Instruction.DrawRoundedRect(rect, color, radiusConstant));
    }

    public void AddPushClip(in Rect rect)
    {
        // An empty clip still has to be pushed so the matching pop stays
        // balanced. The optimiser removes both and culls the contents.
        Add(Instruction.PushClip(rect));
    }

    public void AddPopClip() => Add(new Instruction(InstructionOpCode.PopClip));

    public void AddPushOpacity(float opacity)
    {
        if (opacity < 0.9999f)
        {
            Add(Instruction.PushOpacity(opacity));
        }
    }

    public void AddPopOpacity() => Add(new Instruction(InstructionOpCode.PopOpacity));

    public void AddPushTransform(int matrixConstant)
    {
        Add(new Instruction(InstructionOpCode.PushTransform, payload: matrixConstant));
    }

    public void AddPopTransform() => Add(new Instruction(InstructionOpCode.PopTransform));

    public void AddPushFilter(in Rect rect, int filterConstant)
    {
        Add(Instruction.PushFilter(rect, filterConstant));
    }

    public void AddPopFilter() => Add(new Instruction(InstructionOpCode.PopFilter));

    public void AddPushBackdropFilter(in Rect rect, int filterConstant)
    {
        Add(Instruction.PushBackdropFilter(rect, filterConstant));
    }

    public void AddPopBackdropFilter() => Add(new Instruction(InstructionOpCode.PopBackdropFilter));

    public void AddPushClipPath(in Rect rect, int clipPathConstant)
    {
        Add(Instruction.PushClipPath(rect, clipPathConstant));
    }

    public void AddPopClipPath() => Add(new Instruction(InstructionOpCode.PopClipPath));

    public void AddPushMask(in Rect rect, int maskConstant)
    {
        Add(Instruction.PushMask(rect, maskConstant));
    }

    public void AddPopMask() => Add(new Instruction(InstructionOpCode.PopMask));

    public void AddPushBlendMode(MixBlendMode mode)
    {
        if (mode != MixBlendMode.Normal)
        {
            Add(Instruction.PushBlendMode(mode));
        }
    }

    public void AddPopBlendMode() => Add(new Instruction(InstructionOpCode.PopBlendMode));

    public void AddBeginFrame() => Add(new Instruction(InstructionOpCode.BeginFrame));

    public void AddEndFrame() => Add(new Instruction(InstructionOpCode.EndFrame));
}
