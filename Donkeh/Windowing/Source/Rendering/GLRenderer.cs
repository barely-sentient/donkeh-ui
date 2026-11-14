using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Donkeh.LayoutLab;
using Silk.NET.OpenGL;

namespace Donkeh.Windowing;

/// <summary>
/// Selects how <see cref="GLRenderer"/> submits its geometry to the GPU.
/// </summary>
/// <remarks>
/// <see cref="Indirect"/> is the GPU-driven path: all geometry for a frame is
/// baked into one GPU-resident instance buffer and submitted with indirect draw
/// commands, so the CPU does not walk the scene issuing a draw call per object.
/// <see cref="Direct"/> keeps the same geometry but submits one
/// <c>glDrawArraysInstanced</c> per batch; it exists as a reference path for
/// benchmarking and as a fallback on drivers without indirect-draw extensions.
/// </remarks>
public enum RenderPath
{
    /// <summary>Use indirect submission when the driver exposes the required extensions.</summary>
    Auto,

    /// <summary>Submit each batch with a direct instanced draw call.</summary>
    Direct,

    /// <summary>Submit batches through an indirect command buffer.</summary>
    Indirect
}

/// <summary>
/// Executes the layout engine's instruction stream with OpenGL. Shapes, text and
/// images become instanced quads in a single GPU-resident buffer; indirect draw
/// commands select the visible ranges, so a frame is normally one
/// <c>glMultiDrawArraysIndirect</c> call regardless of element count.
/// </summary>
/// <remarks>
/// <para>
/// <b>GPU-driven submission.</b> <see cref="Process"/> translates every paint
/// instruction into a fixed-size <see cref="GpuInstance"/> record that carries
/// its own clip rectangle, affine transform, colour, SDF parameters and texture
/// selector. Because per-object state lives in the vertex stream, clip and
/// transform changes no longer split batches. <see cref="Flush"/> uploads the
/// instance buffer once and writes one <c>DrawArraysIndirectCommand</c> per
/// blend/texture epoch into a GPU-resident command buffer.
/// </para>
/// <para>
/// <b>Indirect path.</b> When <c>GL_ARB_draw_indirect</c> and
/// <c>GL_ARB_base_instance</c> are present (or the context is OpenGL 4.0+), the
/// command buffer is submitted with <c>glMultiDrawArraysIndirect</c> when
/// <c>GL_ARB_multi_draw_indirect</c> is available and
/// <c>glDrawArraysIndirect</c> otherwise. The vertex shader expands a four-vertex
/// triangle strip per instance, so one command can cover any number of
/// rectangles, glyphs, borders or images.
/// </para>
/// <para>
/// <b>State.</b> Clip rectangles and rounded clip radii are evaluated in the
/// fragment shader from the per-instance record instead of the scissor stack.
/// The window mask, viewport and texture-unit bindings remain uniforms or GL
/// state, because they change at most once per batch.
/// </para>
/// <para>
/// <b>Coordinate space.</b> Rectangles are CSS pixels with the origin at the
/// top-left and Y increasing downwards. The vertex shader applies the instance's
/// affine transform (CSS px in, CSS px out) and then maps to clip space using the
/// logical viewport size.
/// </para>
/// <para>
/// <b>Ownership and threading.</b> The renderer owns the shader program, the
/// VAOs, the instance/command buffers, the white fallback texture and the
/// gradient cache; <see cref="Dispose"/> releases them. The
/// <see cref="FontManager"/> and <see cref="ImageCache"/> are borrowed. The type
/// is not thread-safe and every member must run on the GL context thread.
/// </para>
/// </remarks>
/// <seealso cref="GradientCache"/>
/// <seealso cref="ShaderProgram"/>
public sealed unsafe class GLRenderer : IDisposable
{
    /// <summary>Number of texture units the unified shader can sample per batch.</summary>
    /// <remarks>
    /// OpenGL 3.3 guarantees at least sixteen fragment texture image units, so
    /// this is the largest portable fixed slot count. Exceeding it closes the
    /// current batch and starts a new texture epoch; paint order is preserved.
    /// </remarks>
    private const int MaxTextureSlots = 16;

    /// <summary>Size of one <see cref="GpuInstance"/> in bytes.</summary>
    private const int InstanceStride = 160;

    /// <summary>Number of <c>vec4</c> attributes in a <see cref="GpuInstance"/>.</summary>
    private const int InstanceAttributeCount = 10;

    /// <summary>Size of one <see cref="DrawArraysIndirectCommand"/> in bytes.</summary>
    private const int CommandStride = 16;

    /// <summary>
    /// Number of regions in a persistently mapped stream buffer. Rotating three
    /// regions lets the CPU prepare frame N+1 while the GPU still reads N.
    /// </summary>
    private const int StreamSlotCount = 3;

    /// <summary>The OpenGL entry point for the context this renderer was created on.</summary>
    private readonly GL _gl;

    /// <summary>Borrowed glyph source; this renderer does not own or dispose it.</summary>
    private readonly FontManager _fonts;

    /// <summary>Borrowed bitmap source; this renderer does not own or dispose it.</summary>
    private readonly ImageCache _images;

    /// <summary>Cached 256x256 gradient textures; owned and disposed by this renderer.</summary>
    private readonly GradientCache _gradients;

    /// <summary>The unified shader: instanced quads for shapes, text and images.</summary>
    private readonly ShaderProgram _program;

    /// <summary>VAO binding the unit quad and the ten per-instance attributes.</summary>
    private readonly uint _shapeVao;

    /// <summary>Static VBO holding the four unit-quad corners used by every instance.</summary>
    private readonly uint _quadVbo;

    /// <summary>Opaque white texture bound to unused sampler slots.</summary>
    private readonly uint _whiteTexture;

    /// <summary>Whether the driver exposes <c>GL_ARB_draw_indirect</c> (or OpenGL 4.0+).</summary>
    private readonly bool _hasDrawIndirect;

    /// <summary>Whether the driver exposes <c>GL_ARB_multi_draw_indirect</c> (or OpenGL 4.3+).</summary>
    private readonly bool _hasMultiDrawIndirect;

    /// <summary>Whether the driver supports per-command base instances.</summary>
    private readonly bool _hasBaseInstance;

    /// <summary>Whether the driver exposes <c>GL_ARB_buffer_storage</c> (or OpenGL 4.4+).</summary>
    private readonly bool _hasBufferStorage;

    /// <summary>Per-frame instance storage. Entries below the count are live.</summary>
    private readonly List<GpuInstance> _instances = [];

    /// <summary>Per-frame indirect commands, one per batch segment.</summary>
    private readonly List<DrawArraysIndirectCommand> _commands = [];

    /// <summary>Pooled batch segments; entries below the count are live.</summary>
    private readonly List<Segment> _segments = [];

    /// <summary>Number of segments started in the current frame.</summary>
    private int _segmentCount;

    /// <summary>The segment currently accepting instances, or <see langword="null"/>.</summary>
    private Segment? _current;

    /// <summary>Textures bound to the current texture epoch.</summary>
    private readonly uint[] _textureSlots = new uint[MaxTextureSlots];

    /// <summary>Number of occupied entries in <see cref="_textureSlots"/>.</summary>
    private int _textureSlotCount;

    /// <summary>Increments whenever the texture slot table is reset.</summary>
    private int _textureEpoch;

    /// <summary>Instance buffer object; recreated when it must grow.</summary>
    private uint _instanceVbo;

    /// <summary>Persistent mapping of the instance buffer, or <see langword="null"/>.</summary>
    private void* _instanceMapping;

    /// <summary>Size of the instance buffer in bytes.</summary>
    private nuint _instanceCapacity;

    /// <summary>Bytes per slot in the instance ring, or the full size when unmapped.</summary>
    private int _instanceSlotBytes;

    /// <summary>Whether the instance buffer was allocated with immutable storage.</summary>
    private bool _instanceImmutable;

    /// <summary>Indirect command buffer object; recreated when it must grow.</summary>
    private uint _commandVbo;

    /// <summary>Persistent mapping of the command buffer, or <see langword="null"/>.</summary>
    private void* _commandMapping;

    /// <summary>Size of the command buffer in bytes.</summary>
    private nuint _commandCapacity;

    /// <summary>Bytes per slot in the command ring, or the full size when unmapped.</summary>
    private int _commandSlotBytes;

    /// <summary>Whether the command buffer was allocated with immutable storage.</summary>
    private bool _commandImmutable;

    /// <summary>Index of the stream slot prepared by the current frame.</summary>
    private int _streamSlot;

    /// <summary>GPU completion fences for the three instance slots.</summary>
    private readonly IntPtr[] _instanceFences = new IntPtr[StreamSlotCount];

    /// <summary>GPU completion fences for the three command slots.</summary>
    private readonly IntPtr[] _commandFences = new IntPtr[StreamSlotCount];

    /// <summary>Saved clip states for nested <c>PushClip</c>/<c>PopClip</c> pairs.</summary>
    private readonly Stack<ClipState> _clips = new();

    /// <summary>Saved opacity values for nested opacity pushes.</summary>
    private readonly Stack<float> _opacities = new();

    /// <summary>Saved transforms for nested transform pushes.</summary>
    private readonly Stack<Matrix3x2> _transforms = new();

    /// <summary>Saved blend modes for nested blend-mode pushes.</summary>
    private readonly Stack<MixBlendMode> _blends = new();

    /// <summary>The currently effective clip, always the intersection of every pushed rectangle.</summary>
    private ClipState _clip = ClipState.None;

    /// <summary>The rounded window mask applied to every fragment in the frame.</summary>
    private WindowMaskState _windowMask = WindowMaskState.None;

    /// <summary>Accumulated opacity multiplier in the range 0 to 1.</summary>
    private float _opacity = 1;

    /// <summary>
    /// Accumulated affine transform in CSS pixels. Pushes post-multiply the new
    /// matrix, so a parent's transform is applied before its children's.
    /// </summary>
    private Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>Blend mode for subsequent draws.</summary>
    private MixBlendMode _blend = MixBlendMode.Normal;

    /// <summary>Logical viewport width in CSS pixels.</summary>
    private int _viewportWidth = 1;

    /// <summary>Logical viewport height in CSS pixels.</summary>
    private int _viewportHeight = 1;

    /// <summary>Framebuffer width in device pixels, used for <c>glViewport</c>.</summary>
    private int _framebufferWidth = 1;

    /// <summary>Framebuffer height in device pixels, used for <c>glViewport</c>.</summary>
    private int _framebufferHeight = 1;

    /// <summary>Device-to-CSS-pixel ratio; never zero.</summary>
    private float _dpi = 1;

    /// <summary>Set by <see cref="Dispose"/> so a second call becomes a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates the renderer, probes the driver's indirect-draw capabilities and
    /// allocates the shader, VAO, stream buffers and fallback texture.
    /// </summary>
    /// <param name="gl">The Silk.NET OpenGL entry point; must wrap a current context.</param>
    /// <param name="fonts">Borrowed glyph source that must outlive the renderer.</param>
    /// <param name="images">Borrowed bitmap source that must outlive the renderer.</param>
    /// <exception cref="InvalidOperationException">Thrown when the shader fails to build.</exception>
    public GLRenderer(GL gl, FontManager fonts, ImageCache images)
    {
        _gl = gl;
        _fonts = fonts;
        _images = images;
        _gradients = new GradientCache(gl);

        var (major, minor) = ParseGlVersion();
        var extensions = ReadExtensions();
        _hasDrawIndirect = major > 4 || (major == 4) || extensions.Contains("GL_ARB_draw_indirect");
        _hasMultiDrawIndirect = major > 4 || (major == 4 && minor >= 3) || extensions.Contains("GL_ARB_multi_draw_indirect");
        _hasBaseInstance = major > 4 || (major == 4 && minor >= 2) || extensions.Contains("GL_ARB_base_instance");
        _hasBufferStorage = major > 4 || (major == 4 && minor >= 4) || extensions.Contains("GL_ARB_buffer_storage");

        _program = new ShaderProgram(gl, InstanceVertexShader, InstanceFragmentShader);
        _shapeVao = gl.GenVertexArray();
        _quadVbo = gl.GenBuffer();
        _whiteTexture = CreateWhiteTexture();

        EnsureInstanceCapacity((int)Math.Max(64 * 1024, InstanceStride * 64));
        EnsureCommandCapacity(CommandStride * 32);
        BindSamplerUnits();
    }

    /// <summary>Gets or sets the submission strategy; defaults to <see cref="RenderPath.Auto"/>.</summary>
    /// <remarks>
    /// <see cref="EffectivePath"/> reports what the next <see cref="Flush"/> will
    /// actually use. Forcing <see cref="RenderPath.Indirect"/> on a driver
    /// without indirect-draw support degrades to <see cref="RenderPath.Direct"/>.
    /// </remarks>
    public RenderPath Path { get; set; } = RenderPath.Auto;

    /// <summary>Gets the strategy the renderer will use for the next flush.</summary>
    public RenderPath EffectivePath =>
        Path == RenderPath.Direct || !_hasDrawIndirect || !_hasBaseInstance
            ? RenderPath.Direct
            : RenderPath.Indirect;

    /// <summary>Gets the number of draw calls issued by the last <see cref="Flush"/>.</summary>
    public int LastDrawCalls { get; private set; }

    /// <summary>Gets the number of indirect commands issued by the last flush.</summary>
    public int LastCommandCount { get; private set; }

    /// <summary>Gets the number of instances submitted by the last flush.</summary>
    public int LastInstanceCount { get; private set; }

    /// <summary>Gets the number of batches recorded in the last frame.</summary>
    public int LastBatchCount { get; private set; }

    /// <summary>Gets the CPU time spent submitting the last frame, in milliseconds.</summary>
    public double LastSubmissionMilliseconds { get; private set; }

    /// <summary>Prepares the framebuffer for a frame.</summary>
    /// <param name="framebufferWidth">Framebuffer width in device pixels; clamped to at least 1.</param>
    /// <param name="framebufferHeight">Framebuffer height in device pixels; clamped to at least 1.</param>
    /// <param name="viewportWidth">Logical viewport width in CSS pixels; clamped to at least 1.</param>
    /// <param name="viewportHeight">Logical viewport height in CSS pixels; clamped to at least 1.</param>
    /// <param name="dpi">Device-to-CSS-pixel ratio; non-positive values fall back to 1.</param>
    /// <param name="clearColor">Straight-alpha RGBA clear colour, components in 0..1.</param>
    /// <param name="windowCorners">Corner radii that mask the native window, in CSS pixels.</param>
    /// <remarks>
    /// Resets all batching state and clears the colour buffer. Rounded windows
    /// clear to transparent and mask every fragment in the shader.
    /// </remarks>
    public void BeginFrame(
        int framebufferWidth,
        int framebufferHeight,
        int viewportWidth,
        int viewportHeight,
        float dpi,
        Vector4 clearColor,
        CornerRadius windowCorners = default)
    {
        _framebufferWidth = Math.Max(1, framebufferWidth);
        _framebufferHeight = Math.Max(1, framebufferHeight);
        _viewportWidth = Math.Max(1, viewportWidth);
        _viewportHeight = Math.Max(1, viewportHeight);
        _dpi = dpi > 0 ? dpi : 1;

        _instances.Clear();
        _commands.Clear();
        _segmentCount = 0;
        _current = null;
        _textureSlotCount = 0;
        _textureEpoch = 0;
        _clips.Clear();
        _opacities.Clear();
        _transforms.Clear();
        _blends.Clear();
        _clip = ClipState.None;
        _windowMask = WindowMaskState.None;
        _opacity = 1;
        _transform = Matrix3x2.Identity;
        _blend = MixBlendMode.Normal;
        LastDrawCalls = 0;
        LastCommandCount = 0;
        LastInstanceCount = 0;
        LastBatchCount = 0;
        LastSubmissionMilliseconds = 0;

        _gl.Viewport(0, 0, (uint)_framebufferWidth, (uint)_framebufferHeight);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Enable(EnableCap.Blend);
        _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
        _gl.BlendFuncSeparate(
            BlendingFactor.SrcAlpha,
            BlendingFactor.OneMinusSrcAlpha,
            BlendingFactor.One,
            BlendingFactor.OneMinusSrcAlpha);

        var maxRadius = MathF.Min(_viewportWidth, _viewportHeight) * 0.5f;
        var corners = new CornerRadius(
            Math.Clamp(windowCorners.TopLeft, 0, maxRadius),
            Math.Clamp(windowCorners.TopRight, 0, maxRadius),
            Math.Clamp(windowCorners.BottomRight, 0, maxRadius),
            Math.Clamp(windowCorners.BottomLeft, 0, maxRadius));

        if (corners.IsZero)
        {
            _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
            _gl.Clear(ClearBufferMask.ColorBufferBit);
            return;
        }

        _gl.ClearColor(0, 0, 0, 0);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _windowMask = new WindowMaskState(
            new Rect(0, 0, _viewportWidth, _viewportHeight),
            ToRadii(corners),
            true);
    }

    /// <summary>Consumes one layout result and draws it immediately.</summary>
    /// <param name="result">The layout output to paint.</param>
    public void Render(LayoutResult result)
    {
        Submit(result);
        Flush();
    }

    /// <summary>Consumes a layout result and an overlay, then draws them.</summary>
    /// <param name="result">The root layout output to paint first.</param>
    /// <param name="overlay">Instructions painted above the root.</param>
    /// <param name="overlayConstants">Constant pool for <paramref name="overlay"/>.</param>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants)
    {
        Submit(result.Opcodes, result.Constants);
        Submit(overlay, overlayConstants);
        Flush();
    }

    /// <summary>Consumes two independent roots and draws them in order.</summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="overlay">Instructions painted above <paramref name="result"/>.</param>
    /// <param name="overlayConstants">Constant pool for <paramref name="overlay"/>.</param>
    /// <param name="second">An optional second root painted above the overlay.</param>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants,
        LayoutResult? second)
    {
        Submit(result);
        Submit(overlay, overlayConstants);

        if (second is not null)
        {
            Submit(second);
        }

        Flush();
    }

    /// <summary>Consumes four streams and draws them in argument order.</summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="overlay">Instructions painted above <paramref name="result"/>.</param>
    /// <param name="overlayConstants">Constant pool for <paramref name="overlay"/>.</param>
    /// <param name="second">An optional second root painted above the first overlay.</param>
    /// <param name="secondOverlay">Instructions painted above <paramref name="second"/>.</param>
    /// <param name="secondOverlayConstants">Constant pool for <paramref name="secondOverlay"/>.</param>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants,
        LayoutResult? second,
        ReadOnlySpan<Instruction> secondOverlay,
        ConstantPool secondOverlayConstants)
    {
        Submit(result);
        Submit(overlay, overlayConstants);

        if (second is not null)
        {
            Submit(second);
        }

        Submit(secondOverlay, secondOverlayConstants);
        Flush();
    }

    /// <summary>Consumes two independent roots and draws them in order.</summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="second">An optional second root painted above <paramref name="result"/>.</param>
    public void Render(LayoutResult result, LayoutResult? second)
    {
        Submit(result);

        if (second is not null)
        {
            Submit(second);
        }

        Flush();
    }

    /// <summary>
    /// Appends one layout result to the current scene without submitting it.
    /// </summary>
    /// <param name="result">The layout output to append.</param>
    /// <remarks>
    /// Call <see cref="Flush"/> after the last <c>Submit</c> of the frame. This
    /// split lets host-owned overlays and canvas paint lists join the same
    /// indirect batch as the main display list.
    /// </remarks>
    public void Submit(LayoutResult result) => Process(result.Opcodes, result.Constants);

    /// <summary>Appends an instruction stream to the current scene.</summary>
    /// <param name="opcodes">Instructions in paint order.</param>
    /// <param name="constants">Pool resolving the payload ids.</param>
    public void Submit(ReadOnlySpan<Instruction> opcodes, ConstantPool constants) =>
        Process(opcodes, constants);

    /// <summary>
    /// Submits every recorded instance and resets the per-frame counters.
    /// </summary>
    /// <remarks>
    /// This is where indirect rendering happens. The CPU builds one command per
    /// batch segment, uploads the instance and command buffers, then lets the
    /// GPU expand the instances. No work is done per object beyond appending its
    /// 160-byte record during <see cref="Process"/>.
    /// </remarks>
    public void Flush()
    {
        CloseSegment();
        var start = Stopwatch.GetTimestamp();
        LastInstanceCount = _instances.Count;
        LastCommandCount = _segmentCount;
        LastBatchCount = 0;

        if (_instances.Count == 0)
        {
            LastDrawCalls = 0;
            LastSubmissionMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return;
        }

        _program.Use();
        SetCommonUniforms();
        EnsureInstanceCapacity(_instances.Count * InstanceStride);
        _gl.BindVertexArray(_shapeVao);
        WaitForSlot(_instanceFences, _instanceMapping != null);
        WaitForSlot(_commandFences, _commandMapping != null);
        if (EffectivePath == RenderPath.Indirect)
        {
            ExecuteIndirect();
        }
        else
        {
            ExecuteDirect();
        }

        RecordFence(_instanceFences, _instanceMapping != null);

        if (EffectivePath == RenderPath.Indirect)
        {
            RecordFence(_commandFences, _commandMapping != null);
        }
        _streamSlot = (_streamSlot + 1) % StreamSlotCount;
        LastSubmissionMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    /// <summary>Translates an instruction stream into GPU instance records.</summary>
    /// <param name="opcodes">The instructions to consume, in paint order.</param>
    /// <param name="constants">Pool used to resolve payload ids.</param>
    /// <remarks>
    /// Clip, opacity, transform and blend opcodes update the paint state; every
    /// draw opcode appends one or more <see cref="GpuInstance"/> records that
    /// snapshot the current state. Nothing is uploaded or drawn here.
    /// </remarks>
    private void Process(ReadOnlySpan<Instruction> opcodes, ConstantPool constants)
    {
        for (var i = 0; i < opcodes.Length; i++)
        {
            var instruction = opcodes[i];

            switch (instruction.OpCode)
            {
                case InstructionOpCode.DrawRect:
                    AddRect(instruction.Rect, (uint)instruction.Payload);
                    break;

                case InstructionOpCode.DrawRoundedRect:
                {
                    var radius = constants.GetRadius(instruction.Aux);
                    AddRoundedRect(instruction.Rect, (uint)instruction.Payload, radius);
                    break;
                }

                case InstructionOpCode.DrawBorder:
                case InstructionOpCode.DrawOutline:
                    AddBorder(instruction.Rect, constants.GetBorder(instruction.Payload));
                    break;

                case InstructionOpCode.DrawShadow:
                    AddShadow(instruction.Rect, constants.GetShadow(instruction.Payload), inset: false);
                    break;

                case InstructionOpCode.DrawInsetShadow:
                    AddShadow(instruction.Rect, constants.GetShadow(instruction.Payload), inset: true);
                    break;

                case InstructionOpCode.DrawBackgroundImage:
                    AddBackground(constants.GetBackground(instruction.Payload), instruction.Rect);
                    break;

                case InstructionOpCode.DrawText:
                    AddText(instruction.Rect, constants.GetText(instruction.Payload), instruction.Flags);
                    break;

                case InstructionOpCode.DrawImage:
                    AddImage(constants.GetImage(instruction.Payload), instruction.Rect);
                    break;

                case InstructionOpCode.DrawLine:
                    AddRect(instruction.Rect, (uint)instruction.Payload);
                    break;

                case InstructionOpCode.DrawEllipse:
                    AddEllipse(instruction.Rect, (uint)instruction.Payload);
                    break;

                case InstructionOpCode.DrawScrollbar:
                    AddScrollbar(instruction.Rect, constants.GetScrollbar(instruction.Payload));
                    break;

                case InstructionOpCode.PushClipRect:
                    PushClip(instruction.Rect, Vector4.Zero, rounded: false);
                    break;

                case InstructionOpCode.PushClipRoundedRect:
                {
                    var radius = constants.GetRadius(instruction.Aux);
                    PushClip(instruction.Rect, ToRadii(radius), rounded: true);
                    break;
                }

                case InstructionOpCode.PopClip:
                    PopClip();
                    break;

                case InstructionOpCode.PushOpacity:
                    _opacities.Push(_opacity);
                    _opacity *= Math.Clamp(instruction.X, 0, 1);
                    break;

                case InstructionOpCode.PopOpacity:
                    if (_opacities.Count > 0)
                    {
                        _opacity = _opacities.Pop();
                    }

                    break;

                case InstructionOpCode.PushTransform:
                    _transforms.Push(_transform);
                    _transform = Matrix3x2.Multiply(_transform, constants.GetTransform(instruction.Payload));
                    break;

                case InstructionOpCode.PopTransform:
                    if (_transforms.Count > 0)
                    {
                        _transform = _transforms.Pop();
                    }

                    break;

                case InstructionOpCode.PushBlendMode:
                    _blends.Push(_blend);
                    SetBlend((MixBlendMode)instruction.Payload);
                    break;

                case InstructionOpCode.PopBlendMode:
                    SetBlend(_blends.Count > 0 ? _blends.Pop() : MixBlendMode.Normal);
                    break;

                case InstructionOpCode.SetScrollOffset:
                case InstructionOpCode.PushFilter:
                case InstructionOpCode.PushBackdropFilter:
                case InstructionOpCode.PushClipPath:
                case InstructionOpCode.PushMask:
                case InstructionOpCode.PushLayer:
                case InstructionOpCode.PopLayer:
                case InstructionOpCode.PopFilter:
                case InstructionOpCode.PopBackdropFilter:
                case InstructionOpCode.PopClipPath:
                case InstructionOpCode.PopMask:
                case InstructionOpCode.Nop:
                case InstructionOpCode.BeginFrame:
                case InstructionOpCode.EndFrame:
                    break;
            }
        }
    }

    /// <summary>Appends an axis-aligned filled rectangle.</summary>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    private void AddRect(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        Append(Build(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            Vector4.Zero,
            Vector4.Zero,
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            Vector4.Zero));
    }

    /// <summary>Appends a filled rectangle with per-corner rounding (SDF mode 1).</summary>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <param name="radius">Corner radii in CSS pixels.</param>
    private void AddRoundedRect(in Rect rect, uint color, CornerRadius radius)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        Append(Build(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            ToRadii(radius),
            new Vector4(1, 0, 0, 0),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            Vector4.Zero));
    }

    /// <summary>Appends an ellipse inscribed in <paramref name="rect"/> (SDF mode 5).</summary>
    /// <param name="rect">Bounding rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    private void AddEllipse(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        Append(Build(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            Vector4.Zero,
            new Vector4(5, 0, 0, 0),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            Vector4.Zero));
    }

    /// <summary>Appends a border as either one SDF ring or up to four rectangles.</summary>
    /// <param name="rect">Border box in CSS pixels.</param>
    /// <param name="border">Per-side widths, styles, colours and the shared radius.</param>
    private void AddBorder(in Rect rect, in BorderSpec border)
    {
        if (border.IsEmpty || rect.IsEmpty)
        {
            return;
        }

        var top = border.Top;
        var right = border.Right;
        var bottom = border.Bottom;
        var left = border.Left;
        var topWidth = top.Style is BorderStyle.None or BorderStyle.Hidden ? 0 : top.Width;
        var rightWidth = right.Style is BorderStyle.None or BorderStyle.Hidden ? 0 : right.Width;
        var bottomWidth = bottom.Style is BorderStyle.None or BorderStyle.Hidden ? 0 : bottom.Width;
        var leftWidth = left.Style is BorderStyle.None or BorderStyle.Hidden ? 0 : left.Width;

        if (topWidth <= 0 && rightWidth <= 0 && bottomWidth <= 0 && leftWidth <= 0)
        {
            return;
        }

        if (topWidth == rightWidth
            && topWidth == bottomWidth
            && topWidth == leftWidth
            && top.Color == right.Color
            && top.Color == bottom.Color
            && top.Color == left.Color)
        {
            Append(Build(
                new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
                ColorOf(top.Color, _opacity),
                ToRadii(border.Radius),
                new Vector4(2, 0, MathF.Max(1, topWidth), GpuInstance.AllBorderSides),
                new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
                Vector4.Zero));
            return;
        }

        if (!border.Radius.IsZero)
        {
            AddRoundedSide(rect, border, top, topWidth, 0);
            AddRoundedSide(rect, border, right, rightWidth, 1);
            AddRoundedSide(rect, border, bottom, bottomWidth, 2);
            AddRoundedSide(rect, border, left, leftWidth, 3);
            return;
        }

        if (topWidth > 0)
        {
            AddRoundedRect(
                new Rect(rect.X, rect.Y, rect.Width, topWidth),
                top.Color,
                CornerRadius.Zero);
        }

        if (bottomWidth > 0)
        {
            AddRoundedRect(
                new Rect(rect.X, rect.Bottom - bottomWidth, rect.Width, bottomWidth),
                bottom.Color,
                CornerRadius.Zero);
        }

        if (leftWidth > 0)
        {
            AddRoundedRect(
                new Rect(rect.X, rect.Y + topWidth, leftWidth, MathF.Max(0, rect.Height - topWidth - bottomWidth)),
                left.Color,
                CornerRadius.Zero);
        }

        if (rightWidth > 0)
        {
            AddRoundedRect(
                new Rect(rect.Right - rightWidth, rect.Y + topWidth, rightWidth, MathF.Max(0, rect.Height - topWidth - bottomWidth)),
                right.Color,
                CornerRadius.Zero);
        }
    }

    /// <summary>Adds one coloured sector of a rounded border ring.</summary>
    /// <param name="rect">Border box in CSS pixels.</param>
    /// <param name="border">Full border specification providing the radii.</param>
    /// <param name="side">Side whose width and colour are used.</param>
    /// <param name="width">Resolved side width in CSS pixels.</param>
    /// <param name="sector">Sector index: 0 top, 1 right, 2 bottom, 3 left.</param>
    private void AddRoundedSide(in Rect rect, in BorderSpec border, in BorderSide side, float width, int sector)
    {
        if (width <= 0 || (side.Color >> 24) == 0)
        {
            return;
        }

        Append(Build(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(side.Color, _opacity),
            ToRadii(border.Radius),
            new Vector4(2, 0, width, sector),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            Vector4.Zero));
    }

    /// <summary>Appends an outer or inset box shadow as a single SDF instance.</summary>
    /// <param name="rect">The element's border box in CSS pixels.</param>
    /// <param name="shadow">Colour, offset, blur radius and spread in CSS pixels.</param>
    /// <param name="inset">Whether this is an inset shadow (SDF mode 4).</param>
    private void AddShadow(in Rect rect, in ShadowSpec shadow, bool inset)
    {
        if ((shadow.Color >> 24) == 0 || rect.IsEmpty)
        {
            return;
        }

        var blur = MathF.Max(1, shadow.Blur);
        var spread = MathF.Max(0, shadow.Spread);
        var radius = new CornerRadius(
            MathF.Max(0, spread),
            MathF.Max(0, spread),
            MathF.Max(0, spread),
            MathF.Max(0, spread));
        var shapeRect = inset
            ? rect
            : rect.Translate(shadow.OffsetX, shadow.OffsetY).Inflate(spread);
        var quad = inset ? rect : shapeRect.Inflate(blur * 1.5f);

        Append(Build(
            new Vector4(quad.X, quad.Y, quad.Width, quad.Height),
            ColorOf(shadow.Color, _opacity),
            ToRadii(radius),
            new Vector4(inset ? 4 : 3, blur, spread, 0),
            new Vector4(shapeRect.X, shapeRect.Y, shapeRect.Width, shapeRect.Height),
            Vector4.Zero));
    }

    /// <summary>Appends a scrollbar track and thumb as two rounded rectangles.</summary>
    /// <param name="rect">Track rectangle in CSS pixels.</param>
    /// <param name="scrollbar">Orientation, thumb offset and length, thickness and colours.</param>
    private void AddScrollbar(in Rect rect, in ScrollbarSpec scrollbar)
    {
        if (rect.IsEmpty)
        {
            return;
        }

        var radius = CornerRadius.All(scrollbar.Width * 0.5f);
        AddRoundedRect(rect, scrollbar.TrackColor, radius);

        if (scrollbar.Vertical)
        {
            AddRoundedRect(
                new Rect(rect.X, rect.Y + scrollbar.ThumbOffset, rect.Width, scrollbar.ThumbLength),
                scrollbar.ThumbColor,
                radius);
        }
        else
        {
            AddRoundedRect(
                new Rect(rect.X + scrollbar.ThumbOffset, rect.Y, scrollbar.ThumbLength, rect.Height),
                scrollbar.ThumbColor,
                radius);
        }
    }

    /// <summary>Appends a URL background or a cached gradient texture.</summary>
    /// <param name="spec">The parsed background layer.</param>
    /// <param name="rect">Destination box in CSS pixels.</param>
    private void AddBackground(in BackgroundImageSpec spec, in Rect rect)
    {
        if (rect.IsEmpty || spec.Kind == ImageKind.None)
        {
            return;
        }

        if (spec.Kind == ImageKind.Url)
        {
            if (string.IsNullOrEmpty(spec.Url))
            {
                return;
            }

            var texture = _images.GetTexture(spec.Url, out var imageWidth, out var imageHeight, out _);

            if (texture == 0)
            {
                return;
            }

            var repeat = spec.Repeat is BackgroundRepeat.Repeat or BackgroundRepeat.RepeatX or BackgroundRepeat.RepeatY;
            SetWrap(texture, repeat);

            var uvX = spec.Repeat is BackgroundRepeat.Repeat or BackgroundRepeat.RepeatX && imageWidth > 0
                ? rect.Width / imageWidth
                : 1;
            var uvY = spec.Repeat is BackgroundRepeat.Repeat or BackgroundRepeat.RepeatY && imageHeight > 0
                ? rect.Height / imageHeight
                : 1;

            AddTexturedQuad(texture, rect, Vector2.Zero, new Vector2(uvX, uvY), Vector4.One, alphaTexture: false);
            return;
        }

        var gradient = _gradients.GetTexture(spec);

        if (gradient != 0)
        {
            AddTexturedQuad(gradient, rect, Vector2.Zero, Vector2.One, Vector4.One, alphaTexture: false);
        }
    }

    /// <summary>Appends a bitmap image, applying its object-fit rule and tint.</summary>
    /// <param name="spec">Image source, fit mode, opacity and tint.</param>
    /// <param name="rect">Content box in CSS pixels.</param>
    private void AddImage(in ImageSpec spec, in Rect rect)
    {
        if (rect.IsEmpty || string.IsNullOrEmpty(spec.Source))
        {
            return;
        }

        var texture = _images.GetTexture(spec.Source, out var imageWidth, out var imageHeight, out _);

        if (texture == 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return;
        }

        SetWrap(texture, false);
        var uv0 = Vector2.Zero;
        var uv1 = Vector2.One;

        if (spec.Fit != ObjectFit.Fill)
        {
            var scaleX = rect.Width / imageWidth;
            var scaleY = rect.Height / imageHeight;
            var fit = spec.Fit switch
            {
                ObjectFit.Contain => MathF.Min(scaleX, scaleY),
                ObjectFit.Cover => MathF.Max(scaleX, scaleY),
                ObjectFit.None => 1,
                ObjectFit.ScaleDown => MathF.Min(1, MathF.Min(scaleX, scaleY)),
                _ => MathF.Min(scaleX, scaleY)
            };

            var drawWidth = imageWidth * fit;
            var drawHeight = imageHeight * fit;

            if (spec.Fit == ObjectFit.Cover)
            {
                var visibleX = MathF.Min(1, rect.Width / MathF.Max(0.0001f, drawWidth));
                var visibleY = MathF.Min(1, rect.Height / MathF.Max(0.0001f, drawHeight));
                uv0 = new Vector2((1 - visibleX) * 0.5f, (1 - visibleY) * 0.5f);
                uv1 = new Vector2(1 - uv0.X, 1 - uv0.Y);
            }

            var target = new Rect(
                rect.X + (rect.Width - MathF.Min(drawWidth, rect.Width)) * 0.5f,
                rect.Y + (rect.Height - MathF.Min(drawHeight, rect.Height)) * 0.5f,
                MathF.Min(drawWidth, rect.Width),
                MathF.Min(drawHeight, rect.Height));

            AddTexturedQuad(texture, target, uv0, uv1, ColorOf(spec.Tint, _opacity * spec.Opacity), alphaTexture: false);
            return;
        }

        AddTexturedQuad(texture, rect, uv0, uv1, ColorOf(spec.Tint, _opacity * spec.Opacity), alphaTexture: false);
    }

    /// <summary>Appends one textured quad per glyph, handling shaping and kerning.</summary>
    /// <param name="rect">The text box in CSS pixels.</param>
    /// <param name="command">The string to draw and the style that selects the font.</param>
    /// <param name="flags">Per-instruction flags; currently ignored by the glyph path.</param>
    private void AddText(in Rect rect, in TextCommand command, InstructionFlags flags)
    {
        var text = command.Text;

        if (string.IsNullOrEmpty(text) || rect.IsEmpty)
        {
            return;
        }

        var style = command.Style;
        var key = _fonts.CreateKey(in style);
        var lineHeight = style.LineHeight > 0 ? style.LineHeight : style.FontSize * 1.2f;
        var metrics = _fonts.Measure(string.Empty, in style);
        var baseline = rect.Y + (lineHeight + metrics.Ascent - metrics.Descent) * 0.5f;
        var pen = rect.X;
        var color = ColorOf(style.Color, _opacity);
        var previousFace = (FontFace?)null;
        var previousGlyph = 0;

        foreach (var rune in text.EnumerateRunes())
        {
            var info = _fonts.GetGlyph(key, rune, style.FontSize, _dpi);
            var glyph = info.Glyph;

            if (previousFace is not null && ReferenceEquals(previousFace, info.Face))
            {
                pen += _fonts.Kerning(info.Face, previousGlyph, info.GlyphIndex, style.FontSize);
            }

            if (glyph.Texture != 0 && glyph.Width > 0 && glyph.Height > 0)
            {
                var x = MathF.Round(pen + glyph.OffsetX);
                var y = MathF.Round(baseline + glyph.OffsetY);
                AddTexturedQuad(
                    glyph.Texture,
                    new Rect(x, y, glyph.Width, glyph.Height),
                    new Vector2(glyph.U0, glyph.V0),
                    new Vector2(glyph.U1, glyph.V1),
                    color,
                    alphaTexture: true);
            }

            pen += glyph.Advance + style.LetterSpacing;
            previousFace = info.Face;
            previousGlyph = info.GlyphIndex;
        }
    }

    /// <summary>Appends one textured quad for a cached texture.</summary>
    /// <param name="texture">GL texture name; zero produces no geometry.</param>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="uv0">Top-left texture coordinate.</param>
    /// <param name="uv1">Bottom-right texture coordinate.</param>
    /// <param name="color">Straight-alpha RGBA multiplied with the sampled texel.</param>
    /// <param name="alphaTexture">Whether coverage comes from the red channel.</param>
    private void AddTexturedQuad(
        uint texture,
        in Rect rect,
        Vector2 uv0,
        Vector2 uv1,
        Vector4 color,
        bool alphaTexture)
    {
        if (rect.IsEmpty || texture == 0 || color.W <= 0)
        {
            return;
        }

        var slot = EnsureTextureSlot(texture);
        Append(Build(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            color,
            Vector4.Zero,
            new Vector4(6, slot, alphaTexture ? 1 : 0, 0),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            new Vector4(uv0.X, uv0.Y, uv1.X, uv1.Y)));
    }

    /// <summary>Builds an instance snapshot of the current paint state.</summary>
    /// <param name="rect">Quad bounds in CSS pixels.</param>
    /// <param name="color">Straight-alpha RGBA with opacity already folded in.</param>
    /// <param name="radii">Corner radii for the SDF, or zero.</param>
    /// <param name="parameters">SDF mode and mode-specific parameters.</param>
    /// <param name="sdfRect">Rectangle the distance field is measured against.</param>
    /// <param name="uv">Texture coordinates, or zero for solid geometry.</param>
    /// <returns>The record to append to the frame's instance buffer.</returns>
    private GpuInstance Build(
        Vector4 rect,
        Vector4 color,
        Vector4 radii,
        Vector4 parameters,
        Vector4 sdfRect,
        Vector4 uv)
    {
        var clipRect = _clip.HasClip
            ? new Vector4(_clip.Rect.X, _clip.Rect.Y, _clip.Rect.Width, _clip.Rect.Height)
            : NoClipRect;
        var clipRadii = _clip.HasClip ? _clip.Radii : Vector4.Zero;

        return new GpuInstance(
            rect,
            color,
            radii,
            parameters,
            sdfRect,
            clipRect,
            clipRadii,
            new Vector4(_transform.M11, _transform.M21, _transform.M31, 0),
            new Vector4(_transform.M12, _transform.M22, _transform.M32, 0),
            uv);
    }

    /// <summary>Appends an instance to the open segment, starting one if needed.</summary>
    /// <param name="instance">The record to append.</param>
    private void Append(in GpuInstance instance)
    {
        if (_current is null)
        {
            StartSegment();
        }

        _instances.Add(instance);
        _current!.InstanceCount++;
    }

    /// <summary>Starts a new batch segment at the current instance position.</summary>
    private void StartSegment()
    {
        Segment segment;
        if (_segmentCount < _segments.Count)
        {
            segment = _segments[_segmentCount];
        }
        else
        {
            segment = new Segment();
            _segments.Add(segment);
        }

        segment.Reset(_instances.Count, _blend, _textureEpoch);
        _segmentCount++;
        _current = segment;
    }

    /// <summary>Finalises the open segment and records its texture bindings.</summary>
    private void CloseSegment()
    {
        if (_current is not { } segment)
        {
            return;
        }

        segment.Capture(_textureSlots, _textureSlotCount);
        _current = null;
    }

    /// <summary>Changes the blend mode and closes the current batch if it differs.</summary>
    /// <param name="blend">The new blend mode.</param>
    private void SetBlend(MixBlendMode blend)
    {
        if (blend == _blend)
        {
            return;
        }

        CloseSegment();
        _blend = blend;
    }

    /// <summary>Finds or assigns a texture unit for the current texture epoch.</summary>
    /// <param name="texture">The GL texture to sample.</param>
    /// <returns>The texture unit index, in <c>0..MaxTextureSlots-1</c>.</returns>
    /// <remarks>
    /// A new epoch starts when all slots are occupied. Closing the segment at
    /// that point is what preserves paint order across a texture-set change.
    /// </remarks>
    private int EnsureTextureSlot(uint texture)
    {
        for (var i = 0; i < _textureSlotCount; i++)
        {
            if (_textureSlots[i] == texture)
            {
                return i;
            }
        }

        if (_textureSlotCount == MaxTextureSlots)
        {
            CloseSegment();
            _textureSlotCount = 0;
            _textureEpoch++;
        }

        _textureSlots[_textureSlotCount] = texture;
        return _textureSlotCount++;
    }

    /// <summary>Submits the recorded segments through indirect draw commands.</summary>
    private void ExecuteIndirect()
    {
        UploadInstances(CollectionsMarshal.AsSpan(_instances));
        var instanceBase = InstanceSlotBase();
        BuildCommands(instanceBase);
        EnsureCommandCapacity(_commands.Count * CommandStride);
        UploadCommands(CollectionsMarshal.AsSpan(_commands));
        _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commandVbo);
        var commandOffset = _commandMapping != null ? _streamSlot * _commandSlotBytes : 0;

        var calls = 0;
        var i = 0;
        while (i < _segmentCount)
        {
            var blend = _segments[i].Blend;
            var epoch = _segments[i].Epoch;
            var end = i + 1;

            while (end < _segmentCount
                && _segments[end].Blend == blend
                && _segments[end].Epoch == epoch)
            {
                end++;
            }

            ApplyBlend(blend);
            BindTextureTable(_segments[end - 1], _segments[end - 1].SlotCount);

            if (_hasMultiDrawIndirect)
            {
                _gl.MultiDrawArraysIndirect(
                    PrimitiveType.TriangleStrip,
                    (void*)(nint)(commandOffset + i * CommandStride),
                    (uint)(end - i),
                    CommandStride);
            }
            else
            {
                for (var command = i; command < end; command++)
                {
                    _gl.DrawArraysIndirect(
                        PrimitiveType.TriangleStrip,
                        (void*)(nint)(commandOffset + command * CommandStride));
                }
            }

            calls++;
            i = end;
        }

        LastDrawCalls = calls;
        LastBatchCount = calls;
    }

    /// <summary>Submits the recorded segments with one direct instanced call each.</summary>
    private void ExecuteDirect()
    {
        UploadInstances(CollectionsMarshal.AsSpan(_instances));
        var calls = 0;
        var instanceBase = InstanceSlotBase();

        for (var i = 0; i < _segmentCount; i++)
        {
            var segment = _segments[i];

            if (segment.InstanceCount == 0)
            {
                continue;
            }

            ApplyBlend(segment.Blend);
            BindTextureTable(segment, segment.SlotCount);

            if (_hasBaseInstance)
            {
                _gl.DrawArraysInstancedBaseInstance(
                    PrimitiveType.TriangleStrip,
                    0,
                    4,
                    (uint)segment.InstanceCount,
                    (uint)(instanceBase + segment.FirstInstance));
            }
            else
            {
                SetInstanceAttributeBase(instanceBase + segment.FirstInstance);
                _gl.DrawArraysInstanced(
                    PrimitiveType.TriangleStrip,
                    0,
                    4,
                    (uint)segment.InstanceCount);
            }

            calls++;
        }

        if (!_hasBaseInstance)
        {
            SetInstanceAttributeBase(0);
        }

        LastDrawCalls = calls;
        LastBatchCount = calls;
        LastCommandCount = 0;
    }

    /// <summary>Writes one indirect command per live segment.</summary>
    /// <param name="instanceBase">First instance of the active stream slot.</param>
    private void BuildCommands(int instanceBase)
    {
        _commands.Clear();

        for (var i = 0; i < _segmentCount; i++)
        {
            var segment = _segments[i];

            _commands.Add(new DrawArraysIndirectCommand(
                4,
                (uint)segment.InstanceCount,
                0,
                (uint)(instanceBase + segment.FirstInstance)));
        }

        LastCommandCount = _commands.Count;
    }

    /// <summary>Gets the first instance index of the active stream slot.</summary>
    private int InstanceSlotBase() =>
        _instanceMapping != null ? _streamSlot * _instanceSlotBytes / InstanceStride : 0;

    /// <summary>Uploads the instance list as raw bytes.</summary>
    /// <param name="instances">Records to upload.</param>
    private void UploadInstances(ReadOnlySpan<GpuInstance> instances)
    {
        var bytes = MemoryMarshal.AsBytes(instances);

        if (_instanceMapping != null)
        {
            var target = new Span<byte>(
                (byte*)_instanceMapping + _streamSlot * _instanceSlotBytes,
                bytes.Length);
            bytes.CopyTo(target);
            return;
        }

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);

        if (_instanceImmutable)
        {
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, bytes);
        }
        else
        {
            _gl.BufferData(BufferTargetARB.ArrayBuffer, bytes, BufferUsageARB.DynamicDraw);
        }
    }

    /// <summary>Uploads the indirect command list.</summary>
    /// <param name="commands">Commands to upload.</param>
    private void UploadCommands(ReadOnlySpan<DrawArraysIndirectCommand> commands)
    {
        var bytes = MemoryMarshal.AsBytes(commands);

        if (_commandMapping != null)
        {
            var target = new Span<byte>(
                (byte*)_commandMapping + _streamSlot * _commandSlotBytes,
                bytes.Length);
            bytes.CopyTo(target);
            return;
        }

        _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commandVbo);

        if (_commandImmutable)
        {
            _gl.BufferSubData(BufferTargetARB.DrawIndirectBuffer, 0, bytes);
        }
        else
        {
            _gl.BufferData(BufferTargetARB.DrawIndirectBuffer, bytes, BufferUsageARB.DynamicDraw);
        }
    }

    /// <summary>Binds a segment's texture table to consecutive texture units.</summary>
    /// <param name="segment">Segment whose captured table is used.</param>
    /// <param name="count">Number of occupied slots.</param>
    private void BindTextureTable(Segment segment, int count)
    {
        for (var i = 0; i < count; i++)
        {
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i));
            _gl.BindTexture(TextureTarget.Texture2D, segment.Slots[i]);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Applies the separable blend function for a mix-blend mode.</summary>
    /// <param name="blend">The mode recorded on the batch.</param>
    private void ApplyBlend(MixBlendMode blend)
    {
        switch (blend)
        {
            case MixBlendMode.Multiply:
                _gl.BlendFuncSeparate(
                    BlendingFactor.DstColor,
                    BlendingFactor.OneMinusSrcAlpha,
                    BlendingFactor.One,
                    BlendingFactor.OneMinusSrcAlpha);
                break;

            case MixBlendMode.Screen:
                _gl.BlendFuncSeparate(
                    BlendingFactor.One,
                    BlendingFactor.OneMinusSrcColor,
                    BlendingFactor.One,
                    BlendingFactor.OneMinusSrcAlpha);
                break;

            case MixBlendMode.PlusLighter:
                _gl.BlendFuncSeparate(
                    BlendingFactor.One,
                    BlendingFactor.One,
                    BlendingFactor.One,
                    BlendingFactor.One);
                break;

            default:
                _gl.BlendFuncSeparate(
                    BlendingFactor.SrcAlpha,
                    BlendingFactor.OneMinusSrcAlpha,
                    BlendingFactor.One,
                    BlendingFactor.OneMinusSrcAlpha);
                break;
        }
    }

    /// <summary>Writes the per-frame uniforms shared by every batch.</summary>
    private void SetCommonUniforms()
    {
        _program.Uniform2("uViewport", _viewportWidth, _viewportHeight);

        if (_windowMask.HasMask)
        {
            _program.Uniform4(
                "uWindowRect",
                _windowMask.Rect.X,
                _windowMask.Rect.Y,
                _windowMask.Rect.Width,
                _windowMask.Rect.Height);
            _program.Uniform4(
                "uWindowRadii",
                _windowMask.Radii.X,
                _windowMask.Radii.Y,
                _windowMask.Radii.Z,
                _windowMask.Radii.W);
        }
        else
        {
            _program.Uniform4("uWindowRect", 0, 0, _viewportWidth, _viewportHeight);
            _program.Uniform4("uWindowRadii", 0, 0, 0, 0);
        }
    }

    /// <summary>Unpacks a packed ARGB colour into straight-alpha RGBA components.</summary>
    /// <param name="color">Colour packed as <c>0xAARRGGBB</c>.</param>
    /// <param name="opacity">Opacity multiplier applied to the packed alpha.</param>
    /// <returns>Components in the range 0 to 1, in RGBA order.</returns>
    private static Vector4 ColorOf(uint color, float opacity)
    {
        var alpha = Math.Clamp(((color >> 24) & 0xFF) / 255f * opacity, 0, 1);
        return new Vector4(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            alpha);
    }

    /// <summary>Packs corner radii into the shader's expected order.</summary>
    /// <param name="radius">The four CSS-pixel radii.</param>
    /// <returns>Top-right, bottom-right, top-left, bottom-left.</returns>
    private static Vector4 ToRadii(CornerRadius radius) => new(
        radius.TopRight,
        radius.BottomRight,
        radius.TopLeft,
        radius.BottomLeft);

    /// <summary>Binds a texture and writes its wrap mode for both axes.</summary>
    /// <param name="texture">The GL texture name to modify.</param>
    /// <param name="repeat">Whether to use <c>GL_REPEAT</c> instead of clamp-to-edge.</param>
    private void SetWrap(uint texture, bool repeat)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        var mode = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, mode);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, mode);
        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Creates the 1x1 opaque white texture used for unused samplers.</summary>
    /// <returns>The new texture name, owned by the renderer.</returns>
    private uint CreateWhiteTexture()
    {
        var texture = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        ReadOnlySpan<byte> white = [255, 255, 255, 255];
        _gl.TexImage2D(
            TextureTarget.Texture2D,
            0,
            InternalFormat.Rgba8,
            1,
            1,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            white);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        return texture;
    }

    /// <summary>Binds the white texture to every sampler unit once.</summary>
    private void BindSamplerUnits()
    {
        _program.Use();

        for (var i = 0; i < MaxTextureSlots; i++)
        {
            _program.Uniform1($"uTexture{i}", i);
            _gl.ActiveTexture((TextureUnit)((int)TextureUnit.Texture0 + i));
            _gl.BindTexture(TextureTarget.Texture2D, _whiteTexture);
        }

        _gl.ActiveTexture(TextureUnit.Texture0);
    }

    /// <summary>Ensures the instance buffer can hold at least the requested bytes.</summary>
    /// <param name="bytes">Required capacity in bytes.</param>
    private void EnsureInstanceCapacity(int bytes)
    {
        var required = _hasBufferStorage ? bytes * StreamSlotCount : bytes;

        if (required <= (int)_instanceCapacity)
        {
            return;
        }

        var capacity = Math.Max(required, Math.Max(64 * 1024, (int)_instanceCapacity * 2));

        if (_instanceVbo != 0)
        {
            DeleteFences(_instanceFences);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);

            if (_instanceMapping != null)
            {
                _gl.UnmapBuffer(BufferTargetARB.ArrayBuffer);
                _instanceMapping = null;
            }

            _gl.DeleteBuffer(_instanceVbo);
        }

        _instanceVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        Allocate(BufferTargetARB.ArrayBuffer, capacity, ref _instanceMapping);
        _instanceImmutable = _instanceMapping != null || _hasBufferStorage;
        _instanceCapacity = (nuint)capacity;
        _instanceSlotBytes = _instanceMapping != null
            ? capacity / StreamSlotCount / InstanceStride * InstanceStride
            : capacity;
        ConfigureInstanceVao();
    }

    /// <summary>Ensures the indirect command buffer can hold at least the requested bytes.</summary>
    /// <param name="bytes">Required capacity in bytes.</param>
    private void EnsureCommandCapacity(int bytes)
    {
        var required = _hasBufferStorage ? bytes * StreamSlotCount : bytes;

        if (required <= (int)_commandCapacity)
        {
            return;
        }

        var capacity = Math.Max(required, Math.Max(4096, (int)_commandCapacity * 2));

        if (_commandVbo != 0)
        {
            DeleteFences(_commandFences);
            _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commandVbo);

            if (_commandMapping != null)
            {
                _gl.UnmapBuffer(BufferTargetARB.DrawIndirectBuffer);
                _commandMapping = null;
            }

            _gl.DeleteBuffer(_commandVbo);
        }

        _commandVbo = _gl.GenBuffer();
        _gl.BindBuffer(BufferTargetARB.DrawIndirectBuffer, _commandVbo);
        Allocate(BufferTargetARB.DrawIndirectBuffer, capacity, ref _commandMapping);
        _commandImmutable = _commandMapping != null || _hasBufferStorage;
        _commandCapacity = (nuint)capacity;
        _commandSlotBytes = _commandMapping != null
            ? capacity / StreamSlotCount / CommandStride * CommandStride
            : capacity;
    }

    /// <summary>Waits for the fence guarding the stream slot about to be reused.</summary>
    /// <param name="fences">Fence array for the buffer kind.</param>
    /// <param name="enabled">Whether the buffer is persistently mapped.</param>
    /// <remarks>
    /// Three slots give the GPU two full frames of headroom, so this normally
    /// returns immediately. Waiting is the only correct way to overwrite a
    /// coherent persistent mapping that the GPU may still be reading.
    /// </remarks>
    private void WaitForSlot(IntPtr[] fences, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        var fence = fences[_streamSlot];

        if (fence == IntPtr.Zero)
        {
            return;
        }

        _gl.ClientWaitSync(fence, SyncObjectMask.Bit, ulong.MaxValue);
        _gl.DeleteSync(fence);
        fences[_streamSlot] = IntPtr.Zero;
    }

    /// <summary>Records a GPU completion fence for the stream slot just submitted.</summary>
    /// <param name="fences">Fence array for the buffer kind.</param>
    /// <param name="enabled">Whether the buffer is persistently mapped.</param>
    private void RecordFence(IntPtr[] fences, bool enabled)
    {
        if (!enabled)
        {
            return;
        }

        fences[_streamSlot] = _gl.FenceSync(SyncCondition.SyncGpuCommandsComplete, 0u);
    }

    /// <summary>Deletes every outstanding fence in an array.</summary>
    /// <param name="fences">Fence array to clear.</param>
    private void DeleteFences(IntPtr[] fences)
    {
        for (var i = 0; i < fences.Length; i++)
        {
            if (fences[i] != IntPtr.Zero)
            {
                _gl.DeleteSync(fences[i]);
                fences[i] = IntPtr.Zero;
            }
        }
    }

    /// <summary>Allocates immutable storage and maps it persistently when supported.</summary>
    /// <param name="bufferTarget">The buffer binding point.</param>
    /// <param name="bytes">Storage size in bytes.</param>
    /// <param name="mapping">Receives the persistent mapping, if any.</param>
    /// <returns><see langword="true"/> when immutable storage was allocated.</returns>
    private bool Allocate(BufferTargetARB bufferTarget, int bytes, ref void* mapping)
    {
        if (_hasBufferStorage)
        {
            _gl.BufferStorage(
                (GLEnum)bufferTarget,
                (nuint)bytes,
                (void*)0,
                (uint)(BufferStorageMask.DynamicStorageBit
                | BufferStorageMask.MapReadBit
                | BufferStorageMask.MapWriteBit
                | BufferStorageMask.MapPersistentBit
                | BufferStorageMask.MapCoherentBit));

            mapping = _gl.MapBufferRange(
                bufferTarget,
                0,
                (nuint)bytes,
                MapBufferAccessMask.ReadBit
                | MapBufferAccessMask.WriteBit
                | MapBufferAccessMask.PersistentBit
                | MapBufferAccessMask.CoherentBit);

            return true;
        }

        _gl.BufferData(bufferTarget, (nuint)bytes, (void*)0, BufferUsageARB.DynamicDraw);
        mapping = null;
        return false;
    }

    /// <summary>Configures the instance VAO against the current instance buffer.</summary>
    /// <remarks>
    /// Attribute 0 is the static unit quad; attributes 1 to 10 are the ten
    /// <c>vec4</c> slots of <see cref="GpuInstance"/>, all with divisor 1.
    /// </remarks>
    private void ConfigureInstanceVao()
    {
        _gl.BindVertexArray(_shapeVao);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _quadVbo);
        ReadOnlySpan<float> quad =
        [
            0, 0,
            1, 0,
            0, 1,
            1, 1
        ];
        _gl.BufferData(BufferTargetARB.ArrayBuffer, quad, BufferUsageARB.StaticDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        SetInstanceAttributePointers(0);
        _gl.BindVertexArray(0);
    }

    /// <summary>Points the ten instance attributes at a base instance offset.</summary>
    /// <param name="baseInstance">First instance the next draw should read.</param>
    /// <remarks>
    /// Drivers without <c>GL_ARB_base_instance</c> use this to emulate a base
    /// instance by offsetting the attribute pointers instead.
    /// </remarks>
    private void SetInstanceAttributePointers(int baseInstance)
    {
        var offset = (void*)(nint)(baseInstance * InstanceStride);

        for (var i = 0; i < InstanceAttributeCount; i++)
        {
            var location = (uint)(i + 1);
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribPointer(
                location,
                4,
                VertexAttribPointerType.Float,
                false,
                InstanceStride,
                (void*)((byte*)offset + i * 16));
            _gl.VertexAttribDivisor(location, 1);
        }
    }

    /// <summary>Sets the attribute base for the direct fallback path.</summary>
    /// <param name="baseInstance">First instance to read.</param>
    private void SetInstanceAttributeBase(int baseInstance)
    {
        _gl.BindVertexArray(_shapeVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
        SetInstanceAttributePointers(baseInstance);
    }

    /// <summary>Parses the leading major/minor pair from <c>GL_VERSION</c>.</summary>
    /// <returns>The context version, or (3, 3) when the string is unparseable.</returns>
    private (int Major, int Minor) ParseGlVersion()
    {
        var text = _gl.GetStringS(StringName.Version);
        var token = text.Split(' ', 2)[0];
        var parts = token.Split('.');

        return parts.Length >= 2
            && int.TryParse(parts[0], out var major)
            && int.TryParse(parts[1], out var minor)
            ? (major, minor)
            : (3, 3);
    }

    /// <summary>Reads the context's extension list.</summary>
    /// <returns>A case-sensitive set of extension names.</returns>
    private HashSet<string> ReadExtensions()
    {
        var count = _gl.GetInteger(GetPName.NumExtensions);
        var extensions = new HashSet<string>(StringComparer.Ordinal);

        for (uint i = 0; i < count; i++)
        {
            extensions.Add(_gl.GetStringS(StringName.Extensions, i));
        }

        return extensions;
    }

    /// <summary>Intersects a new rectangle with the active clip and makes it current.</summary>
    /// <param name="rect">The new clip rectangle in CSS pixels.</param>
    /// <param name="radii">Rounded-clip radii; ignored when <paramref name="rounded"/> is false.</param>
    /// <param name="rounded">Whether to apply an SDF rounded-box test.</param>
    private void PushClip(in Rect rect, Vector4 radii, bool rounded)
    {
        _clips.Push(_clip);
        var next = _clip.Rect.Intersect(rect);
        var useRounded = rounded || _clip.Rounded;
        var effectiveRadii = rounded
            ? radii
            : (useRounded ? _clip.Radii : Vector4.Zero);
        _clip = new ClipState(next, effectiveRadii, true, useRounded);
    }

    /// <summary>Restores the clip saved by the matching <see cref="PushClip"/>.</summary>
    private void PopClip()
    {
        if (_clips.Count > 0)
        {
            _clip = _clips.Pop();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gradients.Dispose();
        _program.Dispose();
        DeleteFences(_instanceFences);
        DeleteFences(_commandFences);

        if (_instanceMapping != null)
        {
            _gl.UnmapBuffer(BufferTargetARB.ArrayBuffer);
            _instanceMapping = null;
        }

        if (_commandMapping != null)
        {
            _gl.UnmapBuffer(BufferTargetARB.DrawIndirectBuffer);
            _commandMapping = null;
        }

        _gl.DeleteVertexArray(_shapeVao);
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteBuffer(_instanceVbo);
        _gl.DeleteBuffer(_commandVbo);
        _gl.DeleteTexture(_whiteTexture);
    }

    // ---------------------------------------------------------------------
    // Canvas integration
    // ---------------------------------------------------------------------

    /// <summary>Pushes a rectangular clip for a batched canvas pass.</summary>
    /// <param name="rect">Clip rectangle in absolute CSS pixels.</param>
    internal void PushCanvasClip(in Rect rect) => PushClip(rect, Vector4.Zero, rounded: false);

    /// <summary>Pops a clip pushed by <see cref="PushCanvasClip"/>.</summary>
    internal void PopCanvasClip() => PopClip();

    /// <summary>Replaces the current canvas transform and returns the previous one.</summary>
    /// <param name="transform">The absolute canvas-to-screen transform.</param>
    /// <returns>The transform that was active before the call.</returns>
    /// <remarks>
    /// Canvas recording owns the transform for the duration of one canvas, so it
    /// can compose the element translation as the outermost transform instead of
    /// relying on the instruction stream's nesting order.
    /// </remarks>
    internal Matrix3x2 SetCanvasTransform(Matrix3x2 transform)
    {
        var previous = _transform;
        _transform = transform;
        return previous;
    }

    /// <summary>Pushes an opacity multiplier for a batched canvas pass.</summary>
    /// <param name="opacity">Multiplier clamped to the 0..1 range.</param>
    internal void PushCanvasOpacity(float opacity)
    {
        _opacities.Push(_opacity);
        _opacity *= Math.Clamp(opacity, 0, 1);
    }

    /// <summary>Pops an opacity pushed by <see cref="PushCanvasOpacity"/>.</summary>
    internal void PopCanvasOpacity()
    {
        if (_opacities.Count > 0)
        {
            _opacity = _opacities.Pop();
        }
    }

    /// <summary>Appends a canvas rectangle to the current frame batch.</summary>
    /// <param name="rect">Destination rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    internal void AddCanvasRect(in Rect rect, uint color) => AddRect(rect, color);

    /// <summary>Appends a rounded canvas rectangle to the current frame batch.</summary>
    /// <param name="rect">Destination rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <param name="radius">Corner radii in CSS pixels.</param>
    internal void AddCanvasRoundedRect(in Rect rect, uint color, CornerRadius radius) =>
        AddRoundedRect(rect, color, radius);

    /// <summary>Appends a canvas ellipse to the current frame batch.</summary>
    /// <param name="rect">Bounding rectangle in canvas-local CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    internal void AddCanvasEllipse(in Rect rect, uint color) => AddEllipse(rect, color);

    /// <summary>Appends a canvas text run to the current frame batch.</summary>
    /// <param name="rect">Text box in canvas-local CSS pixels.</param>
    /// <param name="command">Text and style to draw.</param>
    internal void AddCanvasText(in Rect rect, in TextCommand command) =>
        AddText(rect, command, InstructionFlags.None);

    /// <summary>Appends a canvas image to the current frame batch.</summary>
    /// <param name="spec">Image source, fit, tint and opacity.</param>
    /// <param name="rect">Destination rectangle in canvas-local CSS pixels.</param>
    internal void AddCanvasImage(in ImageSpec spec, in Rect rect) => AddImage(spec, rect);

    // ---------------------------------------------------------------------
    // Nested types and shaders
    // ---------------------------------------------------------------------

    /// <summary>Clipping state snapshotted into an instance.</summary>
    /// <param name="Rect">Intersected clip rectangle in CSS pixels.</param>
    /// <param name="Radii">Rounded clip radii in shader order.</param>
    /// <param name="HasClip">Whether a clip is active.</param>
    /// <param name="Rounded">Whether the rounded test is active.</param>
    private readonly record struct ClipState(Rect Rect, Vector4 Radii, bool HasClip, bool Rounded)
    {
        /// <summary>The unclipped state: a sentinel rectangle and zero radii.</summary>
        public static readonly ClipState None = new(
            new Rect(-1e7f, -1e7f, 2e7f, 2e7f),
            Vector4.Zero,
            false,
            false);
    }

    /// <summary>The rounded window shape applied to every fragment.</summary>
    /// <param name="Rect">Window rectangle in CSS pixels.</param>
    /// <param name="Radii">Corner radii in shader order.</param>
    /// <param name="HasMask">Whether masking is active.</param>
    private readonly record struct WindowMaskState(Rect Rect, Vector4 Radii, bool HasMask)
    {
        /// <summary>The square, unmasked state.</summary>
        public static readonly WindowMaskState None = new(
            new Rect(0, 0, 0, 0),
            Vector4.Zero,
            false);
    }

    /// <summary>The clip sentinel written to unclipped instances.</summary>
    private static readonly Vector4 NoClipRect = new(-1e7f, -1e7f, 2e7f, 2e7f);

    /// <summary>
    /// The 160-byte GPU record for one quad: geometry, colour, SDF parameters,
    /// clip, transform and texture selection.
    /// </summary>
    /// <remarks>
    /// The sequential layout matches <see cref="ConfigureInstanceVao"/> and the
    /// attribute declarations in <see cref="InstanceVertexShader"/>.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct GpuInstance
    {
        /// <summary>Creates an instance from its ten <c>vec4</c> slots.</summary>
        public GpuInstance(
            Vector4 rect,
            Vector4 color,
            Vector4 radii,
            Vector4 parameters,
            Vector4 sdfRect,
            Vector4 clipRect,
            Vector4 clipRadii,
            Vector4 transform0,
            Vector4 transform1,
            Vector4 uv)
        {
            Rect = rect;
            Color = color;
            Radii = radii;
            Parameters = parameters;
            SdfRect = sdfRect;
            ClipRect = clipRect;
            ClipRadii = clipRadii;
            Transform0 = transform0;
            Transform1 = transform1;
            Uv = uv;
        }

        public readonly Vector4 Rect;
        public readonly Vector4 Color;
        public readonly Vector4 Radii;
        public readonly Vector4 Parameters;
        public readonly Vector4 SdfRect;
        public readonly Vector4 ClipRect;
        public readonly Vector4 ClipRadii;
        public readonly Vector4 Transform0;
        public readonly Vector4 Transform1;
        public readonly Vector4 Uv;

        /// <summary>Parameters W value that paints the whole border ring.</summary>
        public const float AllBorderSides = -1;
    }

    /// <summary>The four-uint command consumed by <c>glDrawArraysIndirect</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct DrawArraysIndirectCommand
    {
        public DrawArraysIndirectCommand(uint count, uint instanceCount, uint first, uint baseInstance)
        {
            Count = count;
            InstanceCount = instanceCount;
            First = first;
            BaseInstance = baseInstance;
        }

        public readonly uint Count;
        public readonly uint InstanceCount;
        public readonly uint First;
        public readonly uint BaseInstance;
    }

    /// <summary>
    /// One contiguous run of instances sharing a blend mode and texture epoch.
    /// </summary>
    /// <remarks>
    /// Segments are pooled across frames. A segment's slot table is captured when
    /// it closes because later segments in the same epoch may append more slots.
    /// </remarks>
    private sealed class Segment
    {
        /// <summary>Creates a segment with an empty slot table.</summary>
        public Segment()
        {
            Slots = new uint[MaxTextureSlots];
        }

        /// <summary>Gets the texture units captured for this segment.</summary>
        public uint[] Slots { get; }

        /// <summary>Gets the number of occupied slots.</summary>
        public int SlotCount { get; private set; }

        /// <summary>Gets the first instance index.</summary>
        public int FirstInstance { get; private set; }

        /// <summary>Gets or sets the number of instances in the segment.</summary>
        public int InstanceCount { get; set; }

        /// <summary>Gets the blend mode.</summary>
        public MixBlendMode Blend { get; private set; }

        /// <summary>Gets the texture epoch.</summary>
        public int Epoch { get; private set; }

        /// <summary>Resets the segment for a new range.</summary>
        /// <param name="firstInstance">First instance index.</param>
        /// <param name="blend">Blend mode for the run.</param>
        /// <param name="epoch">Texture epoch for the run.</param>
        public void Reset(int firstInstance, MixBlendMode blend, int epoch)
        {
            FirstInstance = firstInstance;
            InstanceCount = 0;
            Blend = blend;
            Epoch = epoch;
            SlotCount = 0;
        }

        /// <summary>Snapshots the texture units used by the segment.</summary>
        /// <param name="textures">Current slot table.</param>
        /// <param name="count">Number of occupied slots.</param>
        public void Capture(uint[] textures, int count)
        {
            SlotCount = count;
            Array.Copy(textures, Slots, count);
        }
    }

    /// <summary>
    /// Vertex shader: expands the unit quad to the instance rectangle, forwards
    /// the per-instance clip and SDF inputs, and projects through the affine
    /// transform and viewport.
    /// </summary>
    private const string InstanceVertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aCorner;
        layout(location = 1) in vec4 iRect;
        layout(location = 2) in vec4 iColor;
        layout(location = 3) in vec4 iRadii;
        layout(location = 4) in vec4 iParams;
        layout(location = 5) in vec4 iSdfRect;
        layout(location = 6) in vec4 iClipRect;
        layout(location = 7) in vec4 iClipRadii;
        layout(location = 8) in vec4 iTransform0;
        layout(location = 9) in vec4 iTransform1;
        layout(location = 10) in vec4 iUv;

        uniform vec2 uViewport;

        out vec2 vScreen;
        out vec2 vLocal;
        out vec2 vHalf;
        out vec2 vSdfOffset;
        out vec4 vColor;
        out vec4 vRadii;
        out vec4 vParams;
        out vec4 vSdfRect;
        out vec4 vClipRect;
        out vec4 vClipRadii;
        out vec2 vUv;

        void main()
        {
            vec2 p = iRect.xy + aCorner * iRect.zw;
            vLocal = (aCorner - 0.5) * iRect.zw;
            vHalf = iRect.zw * 0.5;
            vColor = iColor;
            vRadii = iRadii;
            vParams = iParams;
            vSdfRect = iSdfRect;
            vClipRect = iClipRect;
            vClipRadii = iClipRadii;
            vUv = mix(iUv.xy, iUv.zw, aCorner);

            vSdfOffset = (iSdfRect.xy + iSdfRect.zw * 0.5) - (iRect.xy + iRect.zw * 0.5);

            vec2 t = vec2(dot(iTransform0.xy, p) + iTransform0.z,
                          dot(iTransform1.xy, p) + iTransform1.z);

            vScreen = t;

            gl_Position = vec4(t.x / uViewport.x * 2.0 - 1.0,
                               1.0 - t.y / uViewport.y * 2.0,
                               0.0, 1.0);
        }
        """;

    /// <summary>
    /// Fragment shader: clip test, window mask, SDF shape coverage or texture
    /// sampling. Mode 6 selects a texture unit and treats the red channel as
    /// coverage when the alpha flag is set.
    /// </summary>
    private const string InstanceFragmentShader = """
        #version 330 core
        in vec2 vScreen;
        in vec2 vLocal;
        in vec2 vHalf;
        in vec2 vSdfOffset;
        in vec4 vColor;
        in vec4 vRadii;
        in vec4 vParams;
        in vec4 vSdfRect;
        in vec4 vClipRect;
        in vec4 vClipRadii;
        in vec2 vUv;

        uniform sampler2D uTexture0;
        uniform sampler2D uTexture1;
        uniform sampler2D uTexture2;
        uniform sampler2D uTexture3;
        uniform sampler2D uTexture4;
        uniform sampler2D uTexture5;
        uniform sampler2D uTexture6;
        uniform sampler2D uTexture7;
        uniform sampler2D uTexture8;
        uniform sampler2D uTexture9;
        uniform sampler2D uTexture10;
        uniform sampler2D uTexture11;
        uniform sampler2D uTexture12;
        uniform sampler2D uTexture13;
        uniform sampler2D uTexture14;
        uniform sampler2D uTexture15;
        uniform vec4 uWindowRect;
        uniform vec4 uWindowRadii;

        out vec4 fragColor;

        float sdRoundBox(vec2 p, vec2 b, vec4 r)
        {
            vec2 rr = (p.x > 0.0) ? r.xy : r.zw;
            float rad = (p.y > 0.0) ? rr.x : rr.y;
            vec2 q = abs(p) - b + rad;
            return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - rad;
        }

        bool clipped(vec2 p)
        {
            if (p.x < vClipRect.x || p.y < vClipRect.y ||
                p.x > vClipRect.x + vClipRect.z || p.y > vClipRect.y + vClipRect.w)
            {
                return true;
            }

            if (vClipRadii.x + vClipRadii.y + vClipRadii.z + vClipRadii.w > 0.0)
            {
                vec2 center = vClipRect.xy + vClipRect.zw * 0.5;
                if (sdRoundBox(p - center, vClipRect.zw * 0.5, vClipRadii) > 0.0)
                {
                    return true;
                }
            }

            return false;
        }

        float windowCoverage(vec2 p)
        {
            if (uWindowRadii.x + uWindowRadii.y + uWindowRadii.z + uWindowRadii.w <= 0.0)
            {
                return 1.0;
            }

            vec2 center = uWindowRect.xy + uWindowRect.zw * 0.5;
            float d = sdRoundBox(p - center, uWindowRect.zw * 0.5, uWindowRadii);
            return 1.0 - smoothstep(-1.0, 1.0, d);
        }

        vec4 sampleSlot(int slot, vec2 uv)
        {
            if (slot == 0) return texture(uTexture0, uv);
            if (slot == 1) return texture(uTexture1, uv);
            if (slot == 2) return texture(uTexture2, uv);
            if (slot == 3) return texture(uTexture3, uv);
            if (slot == 4) return texture(uTexture4, uv);
            if (slot == 5) return texture(uTexture5, uv);
            if (slot == 6) return texture(uTexture6, uv);
            if (slot == 7) return texture(uTexture7, uv);
            if (slot == 8) return texture(uTexture8, uv);
            if (slot == 9) return texture(uTexture9, uv);
            if (slot == 10) return texture(uTexture10, uv);
            if (slot == 11) return texture(uTexture11, uv);
            if (slot == 12) return texture(uTexture12, uv);
            if (slot == 13) return texture(uTexture13, uv);
            if (slot == 14) return texture(uTexture14, uv);
            return texture(uTexture15, uv);
        }

        void main()
        {
            if (clipped(vScreen))
            {
                discard;
            }

            float windowAlpha = windowCoverage(vScreen);

            if (windowAlpha <= 0.001)
            {
                discard;
            }

            int mode = int(vParams.x + 0.5);

            if (mode == 6)
            {
                vec4 texel = sampleSlot(int(vParams.y + 0.5), vUv);

                if (vParams.z > 0.5)
                {
                    texel = vec4(1.0, 1.0, 1.0, texel.r);
                }

                vec4 color = vColor * texel;

                if (color.a <= 0.001)
                {
                    discard;
                }

                fragColor = vec4(color.rgb, color.a * windowAlpha);
                return;
            }

            float d = sdRoundBox(vLocal - vSdfOffset, vSdfRect.zw * 0.5, vRadii);
            float alpha = 1.0;

            if (mode == 0)
            {
                alpha = 1.0;
            }
            else if (mode == 1)
            {
                alpha = 1.0 - smoothstep(-1.0, 1.0, d);
            }
            else if (mode == 2)
            {
                if (vParams.w >= 0.0)
                {
                    vec2 n = (vLocal - vSdfOffset) / max(vSdfRect.zw * 0.5, vec2(0.001));
                    int side = (abs(n.x) > abs(n.y)) ? (n.x > 0.0 ? 1 : 3) : (n.y > 0.0 ? 2 : 0);

                    if (float(side) != vParams.w)
                    {
                        discard;
                    }
                }

                float w = max(1.0, vParams.z);
                alpha = smoothstep(-w - 1.0, -w + 1.0, d) * (1.0 - smoothstep(-1.0, 1.0, d));
            }
            else if (mode == 3)
            {
                float blur = max(1.0, vParams.y);
                alpha = 1.0 - smoothstep(-blur, blur, d);
            }
            else if (mode == 4)
            {
                if (d > 0.0)
                {
                    discard;
                }

                float blur = max(1.0, vParams.y);
                alpha = 1.0 - smoothstep(0.0, blur, -d);
            }
            else if (mode == 5)
            {
                vec2 p = vLocal / max(vHalf, vec2(0.001));
                float e = length(p) - 1.0;
                alpha = 1.0 - smoothstep(-0.02, 0.02, e);
            }

            if (alpha <= 0.001)
            {
                discard;
            }

            fragColor = vec4(vColor.rgb, vColor.a * alpha * windowAlpha);
        }
        """;
}
