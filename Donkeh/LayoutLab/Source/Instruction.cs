using System.Runtime.CompilerServices;

namespace Donkeh.LayoutLab;

/// <summary>
/// A single paint instruction. The layout is fixed at 24 bytes so a frame's
/// instruction stream stays cache friendly and can be copied/translated in bulk.
/// </summary>
/// <remarks>
/// <para>
/// Four floats (<see cref="X"/>..<see cref="W"/>) cover the overwhelmingly
/// common rect/radius/opacity cases without touching the constant pool. Larger
/// payloads - text, gradients, borders, transforms - live in a
/// <see cref="ConstantPool"/> and are addressed by <see cref="Payload"/> or
/// <see cref="Aux"/>.
/// </para>
/// <para>
/// The 24 bytes comprise four <see cref="float"/> values (16 bytes), the
/// <see cref="OpCode"/> and <see cref="Flags"/> bytes, the 16-bit
/// <see cref="Aux"/> index, and the 32-bit <see cref="Payload"/>. The meaning of
/// every field except <see cref="OpCode"/> depends on the opcode; see
/// <see cref="InstructionOpCode"/> for the contract of each operation.
/// </para>
/// <para>
/// <see cref="Z"/> and <see cref="W"/> are the rectangle width and height, not
/// a depth and a width. Instructions that are not draws reuse the four floats
/// for scalars such as an opacity multiplier or a scroll offset, in which case
/// <see cref="Rect"/> is meaningless.
/// </para>
/// <para>
/// Instructions are immutable value types. <see cref="Translate"/> and the
/// <c>With*</c> helpers return modified copies, so a single instruction can be
/// copied into both the raw and the optimised stream without aliasing.
/// </para>
/// </remarks>
/// <seealso cref="InstructionBuffer"/>
/// <seealso cref="InstructionOpCode"/>
/// <seealso cref="InstructionFlags"/>
public readonly struct Instruction
{
    /// <summary>
    /// Initialises an instruction with explicit values for every field.
    /// </summary>
    /// <param name="opCode">The operation to perform.</param>
    /// <param name="x">
    /// Rectangle X origin in CSS pixels, or an opcode-specific scalar such as
    /// the opacity multiplier for <see cref="InstructionOpCode.PushOpacity"/>.
    /// </param>
    /// <param name="y">
    /// Rectangle Y origin in CSS pixels, or an opcode-specific scalar such as
    /// the vertical scroll offset.
    /// </param>
    /// <param name="z">Rectangle width in CSS pixels, or an opcode-specific value.</param>
    /// <param name="w">Rectangle height in CSS pixels, or an opcode-specific value.</param>
    /// <param name="payload">
    /// Packed ARGB colour, constant-pool index, matrix id or
    /// <see cref="MixBlendMode"/> value, depending on the opcode.
    /// </param>
    /// <param name="aux">
    /// Small constant-pool index; the painter uses it for corner radii.
    /// </param>
    /// <param name="flags">Per-instruction flags; see <see cref="InstructionFlags"/>.</param>
    /// <remarks>
    /// Prefer the typed factory methods such as <see cref="DrawRect"/> and
    /// <see cref="PushClip"/>, which fill the fields an opcode expects and add
    /// its implied flags. This constructor exists for the optimiser and for
    /// custom painters that emit an opcode without a dedicated helper; no
    /// validation is performed, so an opcode/field mismatch produces an
    /// instruction the renderer cannot interpret.
    /// </remarks>
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

    /// <summary>Gets the operation this instruction performs.</summary>
    /// <remarks>
    /// The opcode selects the interpretation of <see cref="X"/>..<see cref="W"/>,
    /// <see cref="Payload"/> and <see cref="Aux"/>. Draw and push opcodes are
    /// grouped into contiguous numeric ranges so a renderer can classify an
    /// instruction with two comparisons (see <see cref="IsDraw"/> and
    /// <see cref="IsPush"/>).
    /// </remarks>
    public InstructionOpCode OpCode { get; }

    /// <summary>Gets the per-instruction flags that qualify the opcode.</summary>
    /// <remarks>
    /// Flags are a bit mask; test individual bits rather than comparing the
    /// value. They are hints to the renderer and optimiser (for example
    /// <see cref="InstructionFlags.Opaque"/> permits overwriting the
    /// destination), not part of the geometry.
    /// </remarks>
    /// <seealso cref="InstructionFlags"/>
    public InstructionFlags Flags { get; }

    /// <summary>
    /// Gets the 16-bit constant-pool index, used for corner radii by
    /// <see cref="InstructionOpCode.DrawRoundedRect"/> and
    /// <see cref="InstructionOpCode.PushClipRoundedRect"/>.
    /// </summary>
    /// <remarks>
    /// A <see cref="ushort"/> keeps the instruction at 24 bytes while still
    /// indexing up to 65,536 radius constants. Zero is both a valid pool index
    /// and the default, so opcodes that do not use <see cref="Aux"/> leave it at
    /// zero; read it only when the opcode documents it.
    /// </remarks>
    public ushort Aux { get; }

    /// <summary>Packed colour, constant index, or layer id, depending on the opcode.</summary>
    /// <remarks>
    /// <para>
    /// For the colour-bearing draws (<see cref="InstructionOpCode.DrawRect"/>,
    /// <see cref="InstructionOpCode.DrawRoundedRect"/>,
    /// <see cref="InstructionOpCode.DrawLine"/>,
    /// <see cref="InstructionOpCode.DrawEllipse"/>) the value is an
    /// <c>0xAARRGGBB</c> colour reinterpreted as a signed <see cref="int"/>.
    /// </para>
    /// <para>
    /// For text, borders, shadows, backgrounds, images and scrollbars it is a
    /// zero-based index into the frame's <see cref="ConstantPool"/>. For
    /// <see cref="InstructionOpCode.PushTransform"/> it indexes a
    /// <see cref="System.Numerics.Matrix3x2"/> constant, and for
    /// <see cref="InstructionOpCode.PushBlendMode"/> it is a
    /// <see cref="MixBlendMode"/> value. Other opcodes leave it at zero.
    /// </para>
    /// </remarks>
    public int Payload { get; }

    /// <summary>
    /// Gets the rectangle X origin, in CSS pixels relative to the viewport
    /// unless a transform scope is active.
    /// </summary>
    /// <remarks>
    /// Not a draw rect for every opcode: <see cref="InstructionOpCode.PushOpacity"/>
    /// stores the opacity multiplier here and <see cref="InstructionOpCode.SetScrollOffset"/>
    /// stores the horizontal scroll offset.
    /// </remarks>
    public float X { get; }

    /// <summary>
    /// Gets the rectangle Y origin, in CSS pixels relative to the viewport
    /// unless a transform scope is active.
    /// </summary>
    /// <remarks>
    /// <see cref="InstructionOpCode.SetScrollOffset"/> stores the vertical
    /// scroll offset here. Effect pushes carry an element rectangle, so they use
    /// this as the Y origin; opcodes with no geometry leave it at zero.
    /// </remarks>
    public float Y { get; }

    /// <summary>Gets the rectangle width in CSS pixels.</summary>
    /// <remarks>
    /// Named <c>Z</c> for compactness in the constructor only; it is the third
    /// component of <see cref="Rect"/> and always means width.
    /// </remarks>
    public float Z { get; }

    /// <summary>Gets the rectangle height in CSS pixels.</summary>
    /// <remarks>
    /// Named <c>W</c> for compactness in the constructor only; it is the fourth
    /// component of <see cref="Rect"/> and always means height.
    /// </remarks>
    public float W { get; }

    /// <summary>
    /// Gets the geometry as a <see cref="Donkeh.LayoutLab.Rect"/> built from
    /// <see cref="X"/>, <see cref="Y"/>, <see cref="Z"/> and <see cref="W"/>.
    /// </summary>
    /// <remarks>
    /// Only meaningful for opcodes that carry a rectangle. For
    /// <see cref="InstructionOpCode.PushOpacity"/> and
    /// <see cref="InstructionOpCode.SetScrollOffset"/> the same fields hold
    /// scalars, so the returned rectangle is not drawable geometry.
    /// </remarks>
    public Rect Rect => new(X, Y, Z, W);

    /// <summary>Gets a value indicating whether the opcode is a draw call.</summary>
    /// <remarks>
    /// True for the contiguous opcode range <see cref="InstructionOpCode.DrawRect"/>
    /// through <see cref="InstructionOpCode.DrawScrollbar"/>. Draw calls carry
    /// their conservative paint bounds in <see cref="Rect"/> and may be culled
    /// by the optimiser.
    /// </remarks>
    public bool IsDraw => OpCode is >= InstructionOpCode.DrawRect and <= InstructionOpCode.DrawScrollbar;

    /// <summary>Gets a value indicating whether the opcode opens a scoped effect.</summary>
    /// <remarks>
    /// True for <see cref="InstructionOpCode.PushClipRect"/> through
    /// <see cref="InstructionOpCode.PushBlendMode"/>. Every push must be closed
    /// by the matching pop so the renderer's state stacks stay balanced.
    /// </remarks>
    public bool IsPush => OpCode is >= InstructionOpCode.PushClipRect and <= InstructionOpCode.PushBlendMode;

    /// <summary>Gets a value indicating whether the opcode closes a scoped effect.</summary>
    /// <remarks>
    /// True for <see cref="InstructionOpCode.PopClip"/> through
    /// <see cref="InstructionOpCode.PopBlendMode"/>.
    /// </remarks>
    public bool IsPop => OpCode is >= InstructionOpCode.PopClip and <= InstructionOpCode.PopBlendMode;

    /// <summary>
    /// Gets a value indicating whether <see cref="Rect"/> describes conservative
    /// paint bounds.
    /// </summary>
    /// <remarks>
    /// Currently equivalent to <see cref="IsDraw"/>. Scoped pushes such as
    /// <see cref="InstructionOpCode.PushFilter"/> also carry an element rect,
    /// but it describes the effect region rather than painted pixels, so it is
    /// not reported as paint bounds.
    /// </remarks>
    public bool HasBounds => IsDraw;

    /// <summary>Returns a copy translated by <paramref name="dx"/>/<paramref name="dy"/>.</summary>
    /// <param name="dx">Horizontal offset in CSS pixels.</param>
    /// <param name="dy">Vertical offset in CSS pixels.</param>
    /// <returns>A new instruction with the same opcode, size, payload and flags.</returns>
    /// <remarks>
    /// Only <see cref="X"/> and <see cref="Y"/> change; <see cref="Z"/>,
    /// <see cref="W"/> and <see cref="Payload"/> are copied unchanged. Use this
    /// to move a pre-built instruction stream, for example when a layout box is
    /// re-offset without re-emitting its contents. Do not use it on
    /// <see cref="InstructionOpCode.PushOpacity"/>, where <see cref="X"/> is an
    /// opacity multiplier.
    /// </remarks>
    public Instruction Translate(float dx, float dy)
    {
        return new Instruction(OpCode, X + dx, Y + dy, Z, W, Payload, Aux, Flags);
    }

    /// <summary>Returns a copy with the supplied per-instruction flags.</summary>
    /// <param name="flags">The complete replacement flag set; existing flags are discarded.</param>
    /// <returns>A new instruction with identical geometry and payload.</returns>
    /// <remarks>
    /// This is a replacement, not a merge. To add a flag, OR it into the
    /// existing <see cref="Flags"/> value at the call site.
    /// </remarks>
    public Instruction WithFlags(InstructionFlags flags)
    {
        return new Instruction(OpCode, X, Y, Z, W, Payload, Aux, flags);
    }

    /// <summary>Returns a copy with the supplied payload.</summary>
    /// <param name="payload">Replacement colour, constant index, matrix id or blend mode.</param>
    /// <returns>A new instruction with identical geometry and flags.</returns>
    /// <remarks>
    /// Used by the optimiser when constants are compacted and indices must be
    /// renumbered, and by callers that reuse a cached instruction with a
    /// different colour.
    /// </remarks>
    public Instruction WithPayload(int payload)
    {
        return new Instruction(OpCode, X, Y, Z, W, payload, Aux, Flags);
    }

    /// <summary>Returns a copy with the supplied 16-bit auxiliary index.</summary>
    /// <param name="aux">Replacement constant-pool index, normally a corner radius.</param>
    /// <returns>A new instruction with identical geometry and flags.</returns>
    public Instruction WithAux(ushort aux)
    {
        return new Instruction(OpCode, X, Y, Z, W, Payload, aux, Flags);
    }

    /// <summary>Emits a solid rectangle fill.</summary>
    /// <param name="rect">Rectangle to fill, in absolute CSS pixels.</param>
    /// <param name="color">Packed ARGB colour.</param>
    /// <param name="flags">
    /// Extra flags; <see cref="InstructionFlags.Opaque"/> is added when the alpha
    /// byte is 0xFF.
    /// </param>
    /// <returns>
    /// A <see cref="InstructionOpCode.DrawRect"/> instruction with the colour in
    /// <see cref="Payload"/>.
    /// </returns>
    /// <remarks>
    /// The opaque flag lets the renderer skip blending and lets the optimiser
    /// merge adjacent fills. Passing a fully transparent colour is not
    /// rejected here; <see cref="InstructionBuffer.AddDrawRect"/> skips it.
    /// </remarks>
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

    /// <summary>Emits a rectangle fill with rounded corners.</summary>
    /// <param name="rect">Rectangle to fill, in absolute CSS pixels.</param>
    /// <param name="color">Packed ARGB colour.</param>
    /// <param name="radiusConstant">Constant-pool index of the <see cref="CornerRadius"/>.</param>
    /// <param name="flags">
    /// Extra flags; <see cref="InstructionFlags.Rounded"/> is always added.
    /// </param>
    /// <returns>A <see cref="InstructionOpCode.DrawRoundedRect"/> instruction.</returns>
    /// <remarks>
    /// Unlike <see cref="DrawRect"/>, this does not infer
    /// <see cref="InstructionFlags.Opaque"/> from the alpha byte; callers that
    /// know the corners are fully covered must set it themselves.
    /// </remarks>
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

    /// <summary>Emits a border-box stroke.</summary>
    /// <param name="rect">Border box in absolute CSS pixels.</param>
    /// <param name="borderConstant">Constant-pool index of the <see cref="BorderSpec"/>.</param>
    /// <returns>A <see cref="InstructionOpCode.DrawBorder"/> instruction with no flags.</returns>
    /// <remarks>
    /// The four side widths, styles, colours and the corner radius all live in
    /// the <see cref="BorderSpec"/>; the instruction carries only geometry and
    /// the pool index.
    /// </remarks>
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

    /// <summary>Emits one laid-out text line.</summary>
    /// <param name="rect">Line box in absolute CSS pixels.</param>
    /// <param name="textConstant">Constant-pool index of the <c>TextCommand</c>.</param>
    /// <param name="flags">Use <see cref="InstructionFlags.RightToLeft"/> for RTL runs.</param>
    /// <returns>A <see cref="InstructionOpCode.DrawText"/> instruction.</returns>
    /// <remarks>
    /// Each visual line is a separate instruction so the renderer can clip and
    /// batch per line. The rect is the line box, not the glyph ink bounds.
    /// </remarks>
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

    /// <summary>Opens a rectangular clip scope.</summary>
    /// <param name="rect">Clip rectangle in absolute CSS pixels.</param>
    /// <returns>A <see cref="InstructionOpCode.PushClipRect"/> instruction.</returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopClip"/>. Nested clips
    /// intersect, so a push never expands the effective clip.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushClip(in Rect rect)
    {
        return new Instruction(InstructionOpCode.PushClipRect, rect.X, rect.Y, rect.Width, rect.Height);
    }

    /// <summary>Opens a clip scope with rounded corners.</summary>
    /// <param name="rect">Clip rectangle in absolute CSS pixels.</param>
    /// <param name="radiusConstant">Constant-pool index of the <see cref="CornerRadius"/>.</param>
    /// <returns>A <see cref="InstructionOpCode.PushClipRoundedRect"/> instruction.</returns>
    /// <remarks>
    /// Always sets <see cref="InstructionFlags.Rounded"/> and must be matched by
    /// <see cref="InstructionOpCode.PopClip"/>. The optimiser treats a rounded
    /// clip as significant even when its bounds equal the current clip, because
    /// the corner rounding still changes the result.
    /// </remarks>
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

    /// <summary>Opens a group-opacity scope.</summary>
    /// <param name="opacity">Multiplier in the range 0 to 1.</param>
    /// <returns>
    /// A <see cref="InstructionOpCode.PushOpacity"/> instruction with the
    /// multiplier in <see cref="X"/>.
    /// </returns>
    /// <remarks>
    /// The value is not clamped or validated; the painter clamps style opacity
    /// before calling. Must be matched by
    /// <see cref="InstructionOpCode.PopOpacity"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushOpacity(float opacity)
    {
        return new Instruction(InstructionOpCode.PushOpacity, opacity);
    }

    /// <summary>Opens a transform scope.</summary>
    /// <param name="matrixConstant">
    /// Constant-pool index of the <see cref="System.Numerics.Matrix3x2"/>.
    /// </param>
    /// <returns>
    /// A <see cref="InstructionOpCode.PushTransform"/> instruction with no
    /// geometry.
    /// </returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopTransform"/>.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushTransform(int matrixConstant)
    {
        return new Instruction(InstructionOpCode.PushTransform, payload: matrixConstant);
    }

    /// <summary>Opens a filter scope.</summary>
    /// <param name="rect">
    /// Element border box in absolute CSS pixels, used to size the filter
    /// surface.
    /// </param>
    /// <param name="filterConstant">Constant-pool index of the <see cref="FilterSpec"/>.</param>
    /// <returns>A <see cref="InstructionOpCode.PushFilter"/> instruction.</returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopFilter"/>.
    /// </remarks>
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

    /// <summary>Opens a backdrop-filter scope.</summary>
    /// <param name="rect">Element border box in absolute CSS pixels.</param>
    /// <param name="filterConstant">Constant-pool index of the <see cref="FilterSpec"/>.</param>
    /// <returns>A <see cref="InstructionOpCode.PushBackdropFilter"/> instruction.</returns>
    /// <remarks>
    /// The renderer filters the already-painted backdrop inside the rect before
    /// drawing the element. Must be matched by
    /// <see cref="InstructionOpCode.PopBackdropFilter"/>.
    /// </remarks>
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

    /// <summary>Opens a clip-path scope.</summary>
    /// <param name="rect">
    /// Element border box in absolute CSS pixels, used as the reference box.
    /// </param>
    /// <param name="clipPathConstant">
    /// Constant-pool index of the <see cref="ClipPathSpec"/>.
    /// </param>
    /// <returns>A <see cref="InstructionOpCode.PushClipPath"/> instruction.</returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopClipPath"/>.
    /// </remarks>
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

    /// <summary>Opens a mask scope.</summary>
    /// <param name="rect">
    /// Element border box in absolute CSS pixels, used as the reference box.
    /// </param>
    /// <param name="maskConstant">Constant-pool index of the <see cref="MaskSpec"/>.</param>
    /// <returns>A <see cref="InstructionOpCode.PushMask"/> instruction.</returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopMask"/>.
    /// </remarks>
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

    /// <summary>Opens a mix-blend-mode scope.</summary>
    /// <param name="mode">Blend mode applied to the enclosed draws.</param>
    /// <returns>
    /// A <see cref="InstructionOpCode.PushBlendMode"/> instruction with the mode
    /// in <see cref="Payload"/>.
    /// </returns>
    /// <remarks>
    /// Must be matched by <see cref="InstructionOpCode.PopBlendMode"/>.
    /// <see cref="MixBlendMode.Normal"/> is still emitted by this factory;
    /// <see cref="InstructionBuffer.AddPushBlendMode"/> filters it out.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Instruction PushBlendMode(MixBlendMode mode)
    {
        return new Instruction(InstructionOpCode.PushBlendMode, payload: (int)mode);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The concrete format is the opcode plus its geometry and, where useful,
    /// the payload. Draw rects and text include the rect and payload; clip
    /// pushes include the rect; every other opcode prints just the opcode and
    /// payload. The format is intended for logs and tests, not for parsing.
    /// </remarks>
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
/// <remarks>
/// <para>
/// The buffer owns a single <see cref="Instruction"/> array that grows
/// geometrically and never shrinks. <see cref="Clear"/> only resets the count,
/// so after the first few frames the engine reuses the same storage with no
/// allocation; this is the engine's pooling strategy rather than a separate
/// pool type. The layout engine typically keeps one instance for the raw
/// display list and one for the optimiser output.
/// </para>
/// <para>
/// <see cref="Array"/>, <see cref="Span"/> and the indexer expose the backing
/// storage directly: edits through them bypass <see cref="Count"/> validation,
/// and <see cref="Span"/> must not be held across a call that can grow the
/// buffer. The type is not thread-safe; use one buffer per frame or thread.
/// </para>
/// <para>
/// The <c>Add*</c> helpers encode painter conventions that the raw factories
/// do not: fully transparent fills are skipped, near-opaque opacity and
/// <see cref="MixBlendMode.Normal"/> pushes are omitted, and an empty clip is
/// still pushed so the matching pop stays balanced.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var buffer = new InstructionBuffer();
/// buffer.AddDrawRect(new Rect(0, 0, 100, 40), 0xFF336699);
///
/// ushort radius = 0; // index returned by ConstantPool.AddRadius
/// buffer.AddPushOpacity(0.5f);
/// buffer.AddDrawRoundedRect(new Rect(8, 8, 84, 84), 0xFFFFFFFF, radius);
/// buffer.AddPopOpacity();
///
/// foreach (var instruction in buffer.Span)
/// {
///     // submit to the renderer
/// }
/// </code>
/// </example>
/// <seealso cref="Instruction"/>
/// <seealso cref="Optimiser"/>
public sealed class InstructionBuffer
{
    /// <summary>
    /// Backing storage; valid entries are the first <see cref="_count"/>
    /// elements.
    /// </summary>
    /// <remarks>
    /// Grown by <see cref="EnsureCapacity"/> using <c>Array.Resize</c>; never
    /// shrunk.
    /// </remarks>
    private Instruction[] _items;

    /// <summary>Number of valid instructions at the start of <see cref="_items"/>.</summary>
    private int _count;

    /// <summary>Creates a buffer with the requested initial capacity.</summary>
    /// <param name="capacity">
    /// Initial slot count. Values below four are raised to four so the first few
    /// emissions do not immediately resize.
    /// </param>
    /// <remarks>
    /// The capacity is only a starting point; the buffer grows on demand. No
    /// instruction storage is shared between buffers.
    /// </remarks>
    public InstructionBuffer(int capacity = 128)
    {
        _items = new Instruction[Math.Max(4, capacity)];
    }

    /// <summary>Gets the number of instructions currently in the buffer.</summary>
    /// <remarks>
    /// This is the exclusive end of <see cref="Span"/>; capacity is not
    /// included.
    /// </remarks>
    public int Count => _count;

    /// <summary>Gets the backing array, which may be longer than <see cref="Count"/>.</summary>
    /// <remarks>
    /// <para>
    /// Entries at indices <see cref="Count"/> and beyond are stale remnants of
    /// earlier frames. Read only the first <see cref="Count"/> entries.
    /// </para>
    /// <para>
    /// Exposed for renderers and tests that need the backing array without
    /// allocating a span. Mutating it while instructions are being appended
    /// corrupts the logical contents.
    /// </para>
    /// </remarks>
    public Instruction[] Array => _items;

    /// <summary>Gets a read-only view of exactly the valid instructions.</summary>
    /// <value>A span over <see cref="Array"/> with length <see cref="Count"/>.</value>
    /// <remarks>
    /// The span aliases the current backing array. If a later append grows the
    /// buffer, the span keeps pointing at the old array, so a held span sees
    /// stale contents rather than the new instructions. <see cref="Optimiser.Run"/>
    /// consumes a span and must not emit into the same buffer.
    /// </remarks>
    public ReadOnlySpan<Instruction> Span => _items.AsSpan(0, _count);

    /// <summary>Gets a mutable reference to the instruction at <paramref name="index"/>.</summary>
    /// <param name="index">Zero-based index; only the array bounds are checked.</param>
    /// <value>A writable reference into the backing array.</value>
    /// <remarks>
    /// The indexer intentionally does not check against <see cref="Count"/>, so
    /// it can address stale capacity slots. The optimiser uses it to rewrite
    /// instructions in place after constants are compacted.
    /// </remarks>
    /// <exception cref="IndexOutOfRangeException">
    /// <paramref name="index"/> is outside the current array capacity.
    /// </exception>
    public ref Instruction this[int index] => ref _items[index];

    /// <summary>Removes all instructions without releasing the backing array.</summary>
    /// <remarks>
    /// Capacity and existing array contents are retained, so the next frame can
    /// reuse the storage. This is the per-frame reset for the engine's reusable
    /// buffers.
    /// </remarks>
    public void Clear() => _count = 0;

    /// <summary>Ensures the backing array can hold at least the requested count.</summary>
    /// <param name="required">Minimum number of slots needed.</param>
    /// <remarks>
    /// Grows to <c>max(required, max(16, length * 2))</c>, so amortised appends
    /// are O(1) and repeated small growth is avoided. Growing copies the valid
    /// prefix and any stale entries with <c>Array.Resize</c>.
    /// </remarks>
    public void EnsureCapacity(int required)
    {
        if (required <= _items.Length)
        {
            return;
        }

        var size = Math.Max(required, Math.Max(16, _items.Length * 2));
        System.Array.Resize(ref _items, size);
    }

    /// <summary>Appends one instruction, growing the buffer when full.</summary>
    /// <param name="instruction">Instruction to copy; passed by read-only reference.</param>
    /// <remarks>
    /// Inlined because a frame emits this method thousands of times. The
    /// instruction is a value type, so the buffer stores a copy.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(in Instruction instruction)
    {
        if (_count == _items.Length)
        {
            EnsureCapacity(_count + 1);
        }

        _items[_count++] = instruction;
    }

    /// <summary>Appends a span of instructions in order.</summary>
    /// <param name="instructions">Instructions to copy; the span may be empty.</param>
    /// <remarks>
    /// Grows once for the whole range. Used by result snapshots and other
    /// consumers that copy a contiguous run of instructions into a new buffer.
    /// </remarks>
    public void AddRange(ReadOnlySpan<Instruction> instructions)
    {
        EnsureCapacity(_count + instructions.Length);
        instructions.CopyTo(_items.AsSpan(_count));
        _count += instructions.Length;
    }

    /// <summary>Emits a rectangle fill when it can affect the frame.</summary>
    /// <param name="rect">Rectangle in absolute CSS pixels.</param>
    /// <param name="color">Packed ARGB colour.</param>
    /// <remarks>
    /// Skips empty rectangles and colours whose alpha byte is zero. The check
    /// is on the high byte, so any alpha other than zero is emitted even when
    /// it is very faint.
    /// </remarks>
    public void AddDrawRect(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color & 0xFF000000) == 0)
        {
            return;
        }

        Add(Instruction.DrawRect(rect, color));
    }

    /// <summary>Emits a rounded rectangle fill when it can affect the frame.</summary>
    /// <param name="rect">Rectangle in absolute CSS pixels.</param>
    /// <param name="color">Packed ARGB colour.</param>
    /// <param name="radiusConstant">Constant-pool index of the <see cref="CornerRadius"/>.</param>
    /// <remarks>
    /// Skips empty rectangles and fully transparent colours, like
    /// <see cref="AddDrawRect"/>.
    /// </remarks>
    public void AddDrawRoundedRect(in Rect rect, uint color, ushort radiusConstant)
    {
        if (rect.IsEmpty || (color & 0xFF000000) == 0)
        {
            return;
        }

        Add(Instruction.DrawRoundedRect(rect, color, radiusConstant));
    }

    /// <summary>Emits a rectangular clip push, including for an empty rectangle.</summary>
    /// <param name="rect">Clip rectangle in absolute CSS pixels.</param>
    /// <remarks>
    /// An empty clip still has to be pushed so the matching pop stays
    /// balanced. The optimiser removes both and culls the contents.
    /// </remarks>
    public void AddPushClip(in Rect rect)
    {
        // An empty clip still has to be pushed so the matching pop stays
        // balanced. The optimiser removes both and culls the contents.
        Add(Instruction.PushClip(rect));
    }

    /// <summary>Emits the pop that closes the most recent clip scope.</summary>
    public void AddPopClip() => Add(new Instruction(InstructionOpCode.PopClip));

    /// <summary>Emits an opacity scope when the value actually changes compositing.</summary>
    /// <param name="opacity">Opacity multiplier, expected in the range 0 to 1.</param>
    /// <remarks>
    /// Values at or above <c>0.9999</c> are treated as fully opaque and emit
    /// nothing, so callers must only emit <see cref="AddPopOpacity"/> when this
    /// method actually pushed. The threshold also guards against float noise
    /// from interpolated styles. The value is not clamped here.
    /// </remarks>
    public void AddPushOpacity(float opacity)
    {
        if (opacity < 0.9999f)
        {
            Add(Instruction.PushOpacity(opacity));
        }
    }

    /// <summary>Emits the pop that closes the most recent opacity scope.</summary>
    public void AddPopOpacity() => Add(new Instruction(InstructionOpCode.PopOpacity));

    /// <summary>Emits a transform push for a constant-pool matrix.</summary>
    /// <param name="matrixConstant">
    /// Constant-pool index of the <see cref="System.Numerics.Matrix3x2"/>.
    /// </param>
    /// <remarks>
    /// Unlike opacity and blend, no identity check is performed; the painter
    /// only calls this with a non-identity matrix.
    /// </remarks>
    public void AddPushTransform(int matrixConstant)
    {
        Add(new Instruction(InstructionOpCode.PushTransform, payload: matrixConstant));
    }

    /// <summary>Emits the pop that closes the most recent transform scope.</summary>
    public void AddPopTransform() => Add(new Instruction(InstructionOpCode.PopTransform));

    /// <summary>Emits a filter push.</summary>
    /// <param name="rect">Element border box in absolute CSS pixels.</param>
    /// <param name="filterConstant">Constant-pool index of the <see cref="FilterSpec"/>.</param>
    public void AddPushFilter(in Rect rect, int filterConstant)
    {
        Add(Instruction.PushFilter(rect, filterConstant));
    }

    /// <summary>Emits the pop that closes the most recent filter scope.</summary>
    public void AddPopFilter() => Add(new Instruction(InstructionOpCode.PopFilter));

    /// <summary>Emits a backdrop-filter push.</summary>
    /// <param name="rect">Element border box in absolute CSS pixels.</param>
    /// <param name="filterConstant">Constant-pool index of the <see cref="FilterSpec"/>.</param>
    public void AddPushBackdropFilter(in Rect rect, int filterConstant)
    {
        Add(Instruction.PushBackdropFilter(rect, filterConstant));
    }

    /// <summary>Emits the pop that closes the most recent backdrop-filter scope.</summary>
    public void AddPopBackdropFilter() => Add(new Instruction(InstructionOpCode.PopBackdropFilter));

    /// <summary>Emits a clip-path push.</summary>
    /// <param name="rect">Element border box in absolute CSS pixels.</param>
    /// <param name="clipPathConstant">
    /// Constant-pool index of the <see cref="ClipPathSpec"/>.
    /// </param>
    public void AddPushClipPath(in Rect rect, int clipPathConstant)
    {
        Add(Instruction.PushClipPath(rect, clipPathConstant));
    }

    /// <summary>Emits the pop that closes the most recent clip-path scope.</summary>
    public void AddPopClipPath() => Add(new Instruction(InstructionOpCode.PopClipPath));

    /// <summary>Emits a mask push.</summary>
    /// <param name="rect">Element border box in absolute CSS pixels.</param>
    /// <param name="maskConstant">Constant-pool index of the <see cref="MaskSpec"/>.</param>
    public void AddPushMask(in Rect rect, int maskConstant)
    {
        Add(Instruction.PushMask(rect, maskConstant));
    }

    /// <summary>Emits the pop that closes the most recent mask scope.</summary>
    public void AddPopMask() => Add(new Instruction(InstructionOpCode.PopMask));

    /// <summary>
    /// Emits a blend-mode push unless the mode is
    /// <see cref="MixBlendMode.Normal"/>.
    /// </summary>
    /// <param name="mode">Blend mode applied to enclosed draws.</param>
    /// <remarks>
    /// <see cref="MixBlendMode.Normal"/> is the identity for compositing, so it
    /// emits nothing; callers must only emit <see cref="AddPopBlendMode"/> when
    /// this method pushed.
    /// </remarks>
    public void AddPushBlendMode(MixBlendMode mode)
    {
        if (mode != MixBlendMode.Normal)
        {
            Add(Instruction.PushBlendMode(mode));
        }
    }

    /// <summary>Emits the pop that closes the most recent blend-mode scope.</summary>
    public void AddPopBlendMode() => Add(new Instruction(InstructionOpCode.PopBlendMode));

    /// <summary>Emits an optional frame-start marker for diagnostics.</summary>
    /// <remarks>
    /// The painter emits this only when <see cref="LayoutOptions.IncludeFrameMarkers"/>
    /// is set; the optimiser keeps it under the same option.
    /// </remarks>
    public void AddBeginFrame() => Add(new Instruction(InstructionOpCode.BeginFrame));

    /// <summary>Emits an optional frame-end marker for diagnostics.</summary>
    /// <remarks>
    /// Pairs with <see cref="AddBeginFrame"/> and is likewise controlled by
    /// <see cref="LayoutOptions.IncludeFrameMarkers"/>.
    /// </remarks>
    public void AddEndFrame() => Add(new Instruction(InstructionOpCode.EndFrame));
}
