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
/// </remarks>
public enum InstructionOpCode : byte
{
    /// <summary>A padding/no-op instruction. Removed by the optimiser.</summary>
    Nop = 0,

    /// <summary>Fill a rectangle. Payload is a packed ARGB colour.</summary>
    DrawRect = 1,

    /// <summary>
    /// Fill a rectangle with rounded corners. Payload is a packed ARGB colour,
    /// X/Y/W/H is the rect and the instruction's <c>Aux</c> indexes the radius
    /// constant.
    /// </summary>
    DrawRoundedRect = 2,

    /// <summary>
    /// Stroke/fill a border box. Payload indexes a <c>BorderSpec</c> constant.
    /// </summary>
    DrawBorder = 3,

    /// <summary>Draw an outer box shadow. Payload indexes a <c>ShadowSpec</c> constant.</summary>
    DrawShadow = 4,

    /// <summary>Draw an inset box shadow. Payload indexes a <c>ShadowSpec</c> constant.</summary>
    DrawInsetShadow = 5,

    /// <summary>Draw a background image or gradient. Payload indexes a <c>BackgroundImageSpec</c>.</summary>
    DrawBackgroundImage = 6,

    /// <summary>
    /// Draw a laid-out text run. Payload indexes a <c>TextCommand</c> constant.
    /// X/Y/W/H is the line box.
    /// </summary>
    DrawText = 7,

    /// <summary>Draw an image. Payload indexes an <c>ImageSpec</c> constant.</summary>
    DrawImage = 8,

    /// <summary>Draw a horizontal or vertical rule. Payload is a packed ARGB colour.</summary>
    DrawLine = 9,

    /// <summary>Draw an ellipse inscribed in the rect. Payload is a packed ARGB colour.</summary>
    DrawEllipse = 10,

    /// <summary>Draw an outline around the rect. Payload indexes a <c>BorderSpec</c>.</summary>
    DrawOutline = 11,

    /// <summary>Draw a native/overlay scrollbar. Payload indexes a <c>ScrollbarSpec</c>.</summary>
    DrawScrollbar = 12,

    /// <summary>
    /// Begin a rectangular clip. X/Y/W/H is the clip rect. Nested clips always
    /// intersect, so a renderer can push the intersection directly.
    /// </summary>
    PushClipRect = 32,

    /// <summary>Begin a rounded clip. <c>Aux</c> indexes a <c>CornerRadius</c> constant.</summary>
    PushClipRoundedRect = 33,

    /// <summary>Begin a transform scope. Payload indexes a <c>Matrix3x2</c> constant.</summary>
    PushTransform = 34,

    /// <summary>Begin an opacity scope. X is the multiplier in [0, 1].</summary>
    PushOpacity = 35,

    /// <summary>Begin a stacking/batching layer. Payload is the layer id.</summary>
    PushLayer = 36,

    /// <summary>
    /// Begin a filter scope. X/Y/W/H is the element border box and Payload
    /// indexes a <c>FilterSpec</c> constant.
    /// </summary>
    PushFilter = 37,

    /// <summary>
    /// Begin a backdrop-filter scope. The element and its backdrop are filtered;
    /// Payload indexes a <c>FilterSpec</c> constant.
    /// </summary>
    PushBackdropFilter = 38,

    /// <summary>
    /// Begin a clip-path scope. X/Y/W/H is the element border box and Payload
    /// indexes a <c>ClipPathSpec</c> constant.
    /// </summary>
    PushClipPath = 39,

    /// <summary>
    /// Begin a mask scope. X/Y/W/H is the element border box and Payload indexes
    /// a <c>MaskSpec</c> constant.
    /// </summary>
    PushMask = 40,

    /// <summary>Begin a mix-blend-mode scope. Payload is a <see cref="MixBlendMode"/> value.</summary>
    PushBlendMode = 41,

    /// <summary>End the most recent clip scope.</summary>
    PopClip = 48,

    /// <summary>End the most recent transform scope.</summary>
    PopTransform = 49,

    /// <summary>End the most recent opacity scope.</summary>
    PopOpacity = 50,

    /// <summary>End the most recent layer scope.</summary>
    PopLayer = 51,

    /// <summary>End the most recent filter scope.</summary>
    PopFilter = 52,

    /// <summary>End the most recent backdrop-filter scope.</summary>
    PopBackdropFilter = 53,

    /// <summary>End the most recent clip-path scope.</summary>
    PopClipPath = 54,

    /// <summary>End the most recent mask scope.</summary>
    PopMask = 55,

    /// <summary>End the most recent mix-blend-mode scope.</summary>
    PopBlendMode = 56,

    /// <summary>
    /// Inform the renderer of a scroll offset for the enclosing clip. X/Y is the
    /// offset. The opcode is a hint for scrollbar/input handling and is not a
    /// draw call; it survives optimisation because native scroll state is not
    /// recoverable from geometry alone.
    /// </summary>
    SetScrollOffset = 64,

    /// <summary>Marks the start of a frame. Optional; useful for diagnostics.</summary>
    BeginFrame = 80,

    /// <summary>Marks the end of a frame. Optional; useful for diagnostics.</summary>
    EndFrame = 81
}

/// <summary>Per-instruction flags that keep common variants out of the opcode space.</summary>
[Flags]
public enum InstructionFlags : byte
{
    None = 0,

    /// <summary>The draw is known to be fully opaque, so it can overwrite what is behind it.</summary>
    Opaque = 1,

    /// <summary>The instruction has an elliptical/rounded geometry.</summary>
    Rounded = 2,

    /// <summary>Text is laid out right-to-left.</summary>
    RightToLeft = 4,

    /// <summary>The draw is a repeat candidate; the renderer may instance it.</summary>
    Repeatable = 8,

    /// <summary>The instruction was produced by a cache hit.</summary>
    Cached = 16
}
