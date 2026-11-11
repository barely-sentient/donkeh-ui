namespace Ignite.LayoutLab;

/// <summary>
/// The machine-independent paint instruction set produced by LayoutLab.
/// </summary>
/// <remarks>
/// <para>
/// Instructions are intentionally tiny and backend agnostic. A renderer walks
/// the stream once and can batch adjacent draws that share an opcode/material
/// without re-deriving geometry from a style tree. This is the "computed style"
/// consumed by the painting layer; LayoutLab never talks to a graphics API.
/// </para>
/// <para>
/// Coordinates are absolute CSS pixels relative to the viewport origin. Clip,
/// transform and opacity scopes are balanced and obey stack discipline.
/// </para>
/// <para>
/// Numeric values are grouped by role so classification is cheap: draws occupy
/// 1-12, scoped pushes 32-41, scoped pops 48-56, and frame/scroll hint opcodes
/// 64-81. <see cref="Nop"/> is zero. The optimiser relies on these contiguous
/// ranges (see <see cref="Instruction.IsDraw"/>), so values must not be
/// renumbered without updating both the optimiser and every renderer that
/// switches on them.
/// </para>
/// <para>
/// The enum is byte-backed so it packs into the 24-byte
/// <see cref="Instruction"/> alongside a <see cref="InstructionFlags"/> byte.
/// The meaning of the instruction's <see cref="Instruction.Payload"/> and
/// <see cref="Instruction.Aux"/> fields is defined per member below; colours
/// are always <c>0xAARRGGBB</c> reinterpreted as a signed 32-bit integer, and
/// "indexes a constant" always means a zero-based index into the frame's
/// <see cref="ConstantPool"/>.
/// </para>
/// </remarks>
/// <seealso cref="Instruction"/>
/// <seealso cref="InstructionFlags"/>
public enum InstructionOpCode : byte
{
    /// <summary>A padding/no-op instruction. Removed by the optimiser.</summary>
    Nop = 0,

    /// <summary>
    /// Fill a rectangle. Payload is a packed ARGB colour; X/Y/W/H is the rect.
    /// </summary>
    /// <remarks>
    /// The colour is <c>0xAARRGGBB</c> held in a signed payload. Alpha zero
    /// draws are skipped by <see cref="InstructionBuffer.AddDrawRect"/>;
    /// alpha 0xFF sets <see cref="InstructionFlags.Opaque"/> through
    /// <see cref="Instruction.DrawRect"/>. This is the opcode the optimiser
    /// merges when adjacent opaque fills are detected.
    /// </remarks>
    DrawRect = 1,

    /// <summary>
    /// Fill a rectangle with rounded corners. Payload is a packed ARGB colour,
    /// X/Y/W/H is the rect and the instruction's <see cref="Instruction.Aux"/>
    /// indexes the radius constant.
    /// </summary>
    /// <remarks>
    /// The painter always sets <see cref="InstructionFlags.Rounded"/>; the
    /// radius is a <see cref="CornerRadius"/> constant so all four corners can
    /// differ. The geometry is still axis-aligned; the radius only affects the
    /// corner alpha/coverage.
    /// </remarks>
    DrawRoundedRect = 2,

    /// <summary>
    /// Stroke/fill a border box. Payload indexes a <c>BorderSpec</c> constant
    /// holding the four side widths, styles, colours and corner radius.
    /// </summary>
    DrawBorder = 3,

    /// <summary>
    /// Draw an outer box shadow. Payload indexes a <c>ShadowSpec</c> constant;
    /// X/Y/W/H is the border box and the shadow extends outside it.
    /// </summary>
    /// <remarks>
    /// The painter sets <see cref="InstructionFlags.Repeatable"/> to tell the
    /// renderer that the shadow geometry may be instanced. Inset shadows use
    /// <see cref="DrawInsetShadow"/> instead.
    /// </remarks>
    DrawShadow = 4,

    /// <summary>
    /// Draw an inset box shadow. Payload indexes a <c>ShadowSpec</c> constant;
    /// X/Y/W/H is the border box and the shadow is clipped to its interior.
    /// </summary>
    DrawInsetShadow = 5,

    /// <summary>
    /// Draw a background image or gradient. Payload indexes a
    /// <c>BackgroundImageSpec</c> constant; X/Y/W/H is the background paint area
    /// selected by <c>background-clip</c>.
    /// </summary>
    /// <remarks>
    /// The painter marks every non-URL kind
    /// <see cref="InstructionFlags.Repeatable"/>; URL images are left unflagged
    /// so the renderer can treat each texture as a separate batch.
    /// </remarks>
    DrawBackgroundImage = 6,

    /// <summary>
    /// Draw a laid-out text run. Payload indexes a <c>TextCommand</c> constant.
    /// X/Y/W/H is the line box.
    /// </summary>
    /// <remarks>
    /// One instruction is emitted per non-empty visual line. Right-to-left runs
    /// set <see cref="InstructionFlags.RightToLeft"/> so the renderer shapes and
    /// positions glyphs with the correct advance direction.
    /// </remarks>
    DrawText = 7,

    /// <summary>
    /// Draw an image. Payload indexes an <c>ImageSpec</c> constant; X/Y/W/H is
    /// the replaced-content box.
    /// </summary>
    /// <remarks>
    /// <see cref="InstructionFlags.Opaque"/> is set when the image's own opacity
    /// is essentially one, allowing the renderer to skip blending.
    /// </remarks>
    DrawImage = 8,

    /// <summary>
    /// Draw a horizontal or vertical rule. Payload is a packed ARGB colour;
    /// X/Y/W/H is the line box, so a horizontal rule uses a small height as its
    /// thickness.
    /// </summary>
    /// <remarks>
    /// Used for text decorations (<c>underline</c>, <c>overline</c>,
    /// <c>line-through</c>). The painter derives thickness from the font size
    /// and does not emit a separate decoration spec.
    /// </remarks>
    DrawLine = 9,

    /// <summary>Draw an ellipse inscribed in the rect. Payload is a packed ARGB colour.</summary>
    /// <remarks>
    /// Reserved: the current <see cref="LayoutEngine"/> painter does not emit
    /// this opcode, but custom painters may use it and renderers should support
    /// it.
    /// </remarks>
    DrawEllipse = 10,

    /// <summary>
    /// Draw an outline around the rect. Payload indexes a <c>BorderSpec</c>.
    /// </summary>
    /// <remarks>
    /// X/Y/W/H is the outline rectangle, already inflated by half the outline
    /// width plus <c>outline-offset</c>; the stroke is centred on that rect.
    /// </remarks>
    DrawOutline = 11,

    /// <summary>
    /// Draw a native/overlay scrollbar. Payload indexes a <c>ScrollbarSpec</c>;
    /// X/Y/W/H is the track rectangle.
    /// </summary>
    DrawScrollbar = 12,

    /// <summary>
    /// Begin a rectangular clip. X/Y/W/H is the clip rect. Nested clips always
    /// intersect, so a renderer can push the intersection directly.
    /// </summary>
    /// <remarks>
    /// Balanced by <see cref="PopClip"/>. An empty clip is still pushed and
    /// popped; the optimiser then removes the pair and culls the contents.
    /// </remarks>
    PushClipRect = 32,

    /// <summary>
    /// Begin a rounded clip. <see cref="Instruction.Aux"/> indexes a
    /// <c>CornerRadius</c> constant.
    /// </summary>
    /// <remarks>
    /// The painter sets <see cref="InstructionFlags.Rounded"/>. Rounded clips
    /// intersect like rectangular ones but are never treated as redundant when
    /// their bounds match the current clip, because the corners still matter.
    /// </remarks>
    PushClipRoundedRect = 33,

    /// <summary>Begin a transform scope. Payload indexes a <c>Matrix3x2</c> constant.</summary>
    /// <remarks>
    /// The matrix is already expressed in device-independent CSS pixels with the
    /// transform origin applied. Draws inside the scope are in the transformed
    /// coordinate space. Balanced by <see cref="PopTransform"/>.
    /// </remarks>
    PushTransform = 34,

    /// <summary>Begin an opacity scope. X is the multiplier in [0, 1].</summary>
    /// <remarks>
    /// No payload. The painter omits pushes at or above <c>0.9999</c>; balanced
    /// by <see cref="PopOpacity"/>.
    /// </remarks>
    PushOpacity = 35,

    /// <summary>Begin a stacking/batching layer. Payload is the layer id.</summary>
    /// <remarks>
    /// Reserved: the current <see cref="LayoutEngine"/> painter does not emit
    /// layers; <c>z-index</c> is resolved by paint order instead. The optimiser
    /// still tracks balanced <see cref="PopLayer"/> pairs for custom streams.
    /// </remarks>
    PushLayer = 36,

    /// <summary>
    /// Begin a filter scope. X/Y/W/H is the element border box and Payload
    /// indexes a <c>FilterSpec</c> constant.
    /// </summary>
    /// <remarks>
    /// The rect sizes the offscreen surface so the filter can sample outside the
    /// element. Balanced by <see cref="PopFilter"/>.
    /// </remarks>
    PushFilter = 37,

    /// <summary>
    /// Begin a backdrop-filter scope. The element and its backdrop are filtered;
    /// Payload indexes a <c>FilterSpec</c> constant.
    /// </summary>
    /// <remarks>
    /// X/Y/W/H is the element border box. The renderer filters what was already
    /// painted inside that rect before drawing the enclosed content. Balanced by
    /// <see cref="PopBackdropFilter"/>.
    /// </remarks>
    PushBackdropFilter = 38,

    /// <summary>
    /// Begin a clip-path scope. X/Y/W/H is the element border box and Payload
    /// indexes a <c>ClipPathSpec</c> constant.
    /// </summary>
    /// <remarks>
    /// The rect is the reference box for percentage/pixel coordinates in the
    /// path. Balanced by <see cref="PopClipPath"/>.
    /// </remarks>
    PushClipPath = 39,

    /// <summary>
    /// Begin a mask scope. X/Y/W/H is the element border box and Payload indexes
    /// a <c>MaskSpec</c> constant.
    /// </summary>
    /// <remarks>
    /// The rect is the reference box for mask positioning and sizing. Balanced
    /// by <see cref="PopMask"/>.
    /// </remarks>
    PushMask = 40,

    /// <summary>
    /// Begin a mix-blend-mode scope. Payload is a <see cref="MixBlendMode"/>
    /// value.
    /// </summary>
    /// <remarks>
    /// <see cref="MixBlendMode.Normal"/> pushes are suppressed by
    /// <see cref="InstructionBuffer.AddPushBlendMode"/>. Balanced by
    /// <see cref="PopBlendMode"/>.
    /// </remarks>
    PushBlendMode = 41,

    /// <summary>End the most recent clip scope.</summary>
    /// <remarks>
    /// Closes either <see cref="PushClipRect"/> or
    /// <see cref="PushClipRoundedRect"/>.
    /// </remarks>
    PopClip = 48,

    /// <summary>End the most recent transform scope.</summary>
    /// <remarks>Closes <see cref="PushTransform"/>.</remarks>
    PopTransform = 49,

    /// <summary>End the most recent opacity scope.</summary>
    /// <remarks>Closes <see cref="PushOpacity"/>.</remarks>
    PopOpacity = 50,

    /// <summary>End the most recent layer scope.</summary>
    /// <remarks>Closes <see cref="PushLayer"/>.</remarks>
    PopLayer = 51,

    /// <summary>End the most recent filter scope.</summary>
    /// <remarks>Closes <see cref="PushFilter"/>.</remarks>
    PopFilter = 52,

    /// <summary>End the most recent backdrop-filter scope.</summary>
    /// <remarks>Closes <see cref="PushBackdropFilter"/>.</remarks>
    PopBackdropFilter = 53,

    /// <summary>End the most recent clip-path scope.</summary>
    /// <remarks>Closes <see cref="PushClipPath"/>.</remarks>
    PopClipPath = 54,

    /// <summary>End the most recent mask scope.</summary>
    /// <remarks>Closes <see cref="PushMask"/>.</remarks>
    PopMask = 55,

    /// <summary>End the most recent mix-blend-mode scope.</summary>
    /// <remarks>Closes <see cref="PushBlendMode"/>.</remarks>
    PopBlendMode = 56,

    /// <summary>
    /// Inform the renderer of a scroll offset for the enclosing clip. X/Y is the
    /// offset. The opcode is a hint for scrollbar/input handling and is not a
    /// draw call; it survives optimisation because native scroll state is not
    /// recoverable from geometry alone.
    /// </summary>
    /// <remarks>
    /// The painter emits this immediately after the scroll container's clip push
    /// and already applies the same offset to child geometry, so a renderer must
    /// not translate draws a second time. X and Y are positive scroll distances
    /// matching <see cref="LayoutBox.ScrollOffsetX"/> and
    /// <see cref="LayoutBox.ScrollOffsetY"/>, in CSS pixels.
    /// </remarks>
    SetScrollOffset = 64,

    /// <summary>Marks the start of a frame. Optional; useful for diagnostics.</summary>
    /// <remarks>
    /// Emitted only when <see cref="LayoutOptions.IncludeFrameMarkers"/> is set;
    /// it is the first instruction of the stream. Pairs with
    /// <see cref="EndFrame"/>.
    /// </remarks>
    BeginFrame = 80,

    /// <summary>Marks the end of a frame. Optional; useful for diagnostics.</summary>
    /// <remarks>
    /// Emitted only when <see cref="LayoutOptions.IncludeFrameMarkers"/> is set;
    /// it is the last instruction of the stream. Pairs with
    /// <see cref="BeginFrame"/>.
    /// </remarks>
    EndFrame = 81
}

/// <summary>Per-instruction flags that keep common variants out of the opcode space.</summary>
/// <remarks>
/// <para>
/// Flags are a byte-sized <see cref="FlagsAttribute"/> bit mask: combine them
/// with bitwise OR and test with <c>&amp;</c>. They qualify an opcode without
/// changing its payload layout, so a renderer must apply the opcode contract
/// first and treat these bits as modifiers.
/// </para>
/// <list type="bullet">
/// <item><description><b>0x01</b> - <see cref="Opaque"/></description></item>
/// <item><description><b>0x02</b> - <see cref="Rounded"/></description></item>
/// <item><description><b>0x04</b> - <see cref="RightToLeft"/></description></item>
/// <item><description><b>0x08</b> - <see cref="Repeatable"/></description></item>
/// <item><description><b>0x10</b> - <see cref="Cached"/></description></item>
/// </list>
/// <para>
/// Flags are advisory: ignoring one may cost performance or batching quality,
/// but the opcode and payload still describe what to draw.
/// </para>
/// </remarks>
/// <seealso cref="Instruction"/>
/// <seealso cref="InstructionOpCode"/>
[Flags]
public enum InstructionFlags : byte
{
    /// <summary>No modifier flags. Value <c>0x00</c>.</summary>
    None = 0,

    /// <summary>
    /// The draw is known to be fully opaque, so it can overwrite what is behind
    /// it.
    /// </summary>
    /// <remarks>
    /// Bit <c>0x01</c>. Set for an opaque <c>DrawRect</c> fill and for images
    /// whose own opacity is essentially one. The optimiser only merges opaque
    /// rectangle fills.
    /// </remarks>
    Opaque = 1,

    /// <summary>The instruction has an elliptical/rounded geometry.</summary>
    /// <remarks>
    /// Bit <c>0x02</c>. Set by the rounded rect and rounded clip factories; the
    /// radius itself is in <see cref="Instruction.Aux"/>.
    /// </remarks>
    Rounded = 2,

    /// <summary>Text is laid out right-to-left.</summary>
    /// <remarks>
    /// Bit <c>0x04</c>. Applies to <see cref="InstructionOpCode.DrawText"/>
    /// runs.
    /// </remarks>
    RightToLeft = 4,

    /// <summary>The draw is a repeat candidate; the renderer may instance it.</summary>
    /// <remarks>
    /// Bit <c>0x08</c>. The painter sets it for outer shadows and gradient
    /// backgrounds, whose repeated geometry can be instanced in one batch.
    /// </remarks>
    Repeatable = 8,

    /// <summary>The instruction was produced by a cache hit.</summary>
    /// <remarks>
    /// Bit <c>0x10</c>. Reserved for replaying cached display-list fragments;
    /// the current painter does not set it.
    /// </remarks>
    Cached = 16
}
