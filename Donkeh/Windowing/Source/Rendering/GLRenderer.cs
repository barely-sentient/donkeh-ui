using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Donkeh.LayoutLab;
using Silk.NET.OpenGL;

namespace Donkeh.Windowing;

/// <summary>
/// Executes the layout engine's instruction stream with OpenGL. Shapes and text
/// are accumulated into ordered batches; state changes only flush the batch they
/// affect, so a typical UI frame becomes a handful of draw calls.
/// </summary>
/// <remarks>
/// <para>
/// The renderer consumes the 24-byte <see cref="Instruction"/> stream that the
/// layout engine exposes through <see cref="LayoutResult"/> and translates it
/// into two GL paths. Solid shapes, borders, shadows, ellipses and scrollbars
/// become instanced rounded-box SDF quads drawn with <c>DrawArraysInstanced</c>
/// (a four-vertex triangle strip per instance); text, images and gradient
/// backgrounds become textured quads drawn with <c>DrawElements</c> from a
/// shared index buffer.
/// </para>
/// <para>
/// <b>Batching.</b> Instructions are walked in submission order and appended to
/// a <see cref="DrawRun"/>. A new run starts when the run kind switches between
/// solid and textured, when the texture changes (textured runs only), or when
/// the active clip, transform or blend mode differs from the run's recorded
/// state. Opacity deliberately does not split runs because it is baked into the
/// per-vertex and per-instance colours. Runs are never merged retroactively, so
/// interleaving two textures produces two draw calls even if the first run
/// resumes later.
/// </para>
/// <para>
/// <b>Coordinate space.</b> Rectangles are CSS pixels with the origin at the
/// top-left and Y increasing downwards. The vertex shaders apply the active
/// affine transform (CSS px in, CSS px out) and then map to clip space using
/// the logical viewport size. Scissor rectangles, by contrast, are converted to
/// framebuffer pixels by multiplying by the frame DPI, so callers must pass a
/// device-pixel framebuffer size that matches <c>viewport * dpi</c>.
/// </para>
/// <para>
/// <b>Ownership.</b> The renderer owns the two <see cref="ShaderProgram"/>
/// instances, the VAOs and VBOs it creates, and the
/// <see cref="GradientCache"/>; all of them are released by
/// <see cref="Dispose"/>. The <see cref="FontManager"/> and
/// <see cref="ImageCache"/> passed to the constructor are borrowed, and their
/// textures remain owned by those caches.
/// </para>
/// <para>
/// <b>Threading.</b> The type is not thread-safe and every member must be
/// called on the thread that owns the current GL context, typically the UI
/// thread inside the render loop. <see cref="Dispose"/> must also run while the
/// context is current, otherwise the deletes are lost or sent to the wrong
/// context.
/// </para>
/// </remarks>
/// <seealso cref="GradientCache"/>
/// <seealso cref="ShaderProgram"/>
public sealed unsafe class GLRenderer : IDisposable
{
    /// <summary>The OpenGL entry point for the context this renderer was created on.</summary>
    private readonly GL _gl;

    /// <summary>Borrowed glyph source; this renderer does not own or dispose it.</summary>
    private readonly FontManager _fonts;

    /// <summary>Borrowed bitmap source; this renderer does not own or dispose it.</summary>
    private readonly ImageCache _images;

    /// <summary>
    /// Cached 256x256 gradient textures; owned and disposed by this renderer.
    /// </summary>
    private readonly GradientCache _gradients;

    /// <summary>Program that draws instanced rounded-box SDF shapes.</summary>
    private readonly ShaderProgram _shapeProgram;

    /// <summary>Program that draws textured, coloured triangle quads.</summary>
    private readonly ShaderProgram _textureProgram;

    /// <summary>VAO binding the unit quad to attribute 0 and per-instance data.</summary>
    private readonly uint _shapeVao;

    /// <summary>Static VBO holding the four unit-quad corners used by every shape.</summary>
    private readonly uint _quadVbo;

    /// <summary>
    /// Dynamic VBO holding <see cref="ShapeInstance"/> records, re-uploaded for
    /// each solid run; its layout is configured once in
    /// <see cref="ConfigureShapeVao"/>.
    /// </summary>
    private readonly uint _instanceVbo;

    /// <summary>VAO binding the interleaved <see cref="TexturedVertex"/> layout.</summary>
    private readonly uint _textureVao;

    /// <summary>Dynamic VBO receiving the vertices of each textured run.</summary>
    private readonly uint _textureVbo;

    /// <summary>
    /// Dynamic element buffer with the triangle indices for the current
    /// textured run. It is bound inside <see cref="_textureVao"/>, so the VAO
    /// also remembers this element binding.
    /// </summary>
    private readonly uint _textureIndexVbo;

    /// <summary>
    /// Growable CPU scratch buffer for quad indices; starts at 256 indices and
    /// only ever grows.
    /// </summary>
    private uint[] _textureIndices = new uint[256];

    /// <summary>
    /// Per-frame pool of batches. Entries below <see cref="_runCount"/> are live
    /// for the current frame; entries at or above it are retained between
    /// frames so their list capacities are reused instead of reallocated.
    /// </summary>
    private readonly List<DrawRun> _runs = [];

    /// <summary>Number of runs that have been started in the current frame.</summary>
    private int _runCount;

    /// <summary>Saved clip states for nested <c>PushClip</c>/<c>PopClip</c> pairs.</summary>
    private readonly Stack<ClipState> _clips = new();

    /// <summary>Saved opacity values for nested opacity pushes.</summary>
    private readonly Stack<float> _opacities = new();

    /// <summary>Saved transforms for nested transform pushes.</summary>
    private readonly Stack<Matrix3x2> _transforms = new();

    /// <summary>
    /// The currently effective clip, always the intersection of every pushed
    /// rectangle. <see cref="ClipState.None"/> when nothing is pushed.
    /// </summary>
    private ClipState _clip = ClipState.None;

    /// <summary>Accumulated opacity multiplier in the range 0 to 1.</summary>
    private float _opacity = 1;

    /// <summary>
    /// Accumulated affine transform in CSS pixels. Pushes post-multiply the new
    /// matrix, so a parent's transform is applied before its children's.
    /// </summary>
    private Matrix3x2 _transform = Matrix3x2.Identity;

    /// <summary>
    /// Blend mode for subsequent draws. Unlike the other state, pushes replace
    /// the value rather than stacking it.
    /// </summary>
    private MixBlendMode _blend = MixBlendMode.Normal;

    /// <summary>
    /// The batch currently accepting geometry, or <see langword="null"/> when no
    /// run has been started yet in this frame.
    /// </summary>
    private DrawRun? _current;

    /// <summary>Logical viewport width in CSS pixels; used for projection.</summary>
    private int _viewportWidth = 1;

    /// <summary>Logical viewport height in CSS pixels; used for projection.</summary>
    private int _viewportHeight = 1;

    /// <summary>Framebuffer width in device pixels, used for <c>glViewport</c>.</summary>
    private int _framebufferWidth = 1;

    /// <summary>Framebuffer height in device pixels, used for <c>glViewport</c>.</summary>
    private int _framebufferHeight = 1;

    /// <summary>
    /// Device-to-CSS-pixel ratio used to convert scissor rectangles and to
    /// request glyph rasters at device resolution; never zero.
    /// </summary>
    private float _dpi = 1;

    /// <summary>Set by <see cref="Dispose"/> so a second call becomes a no-op.</summary>
    private bool _disposed;

    /// <summary>
    /// Creates the renderer, compiling its two shader programs and allocating
    /// every VAO and VBO it uses for the lifetime of the instance.
    /// </summary>
    /// <param name="gl">
    /// The Silk.NET OpenGL entry point; must wrap a current OpenGL 3.3 core
    /// context and outlive the renderer.
    /// </param>
    /// <param name="fonts">
    /// Font manager used for glyph lookups and kerning. It is borrowed, not
    /// owned, and must outlive the renderer.
    /// </param>
    /// <param name="images">
    /// Image cache supplying URL background layers and bitmap textures. It is
    /// borrowed, not owned, and must outlive the renderer.
    /// </param>
    /// <exception cref="InvalidOperationException">
    /// Thrown when either shader program fails to compile or link; the message
    /// contains the GL info log.
    /// </exception>
    /// <remarks>
    /// The constructor is fairly expensive (two shader builds and six buffer or
    /// VAO allocations), so create one renderer per context rather than one per
    /// frame. GPU resources are freed only by <see cref="Dispose"/>.
    /// </remarks>
    public GLRenderer(GL gl, FontManager fonts, ImageCache images)
    {
        _gl = gl;
        _fonts = fonts;
        _images = images;
        _gradients = new GradientCache(gl);
        _shapeProgram = new ShaderProgram(gl, ShapeVertexShader, ShapeFragmentShader);
        _textureProgram = new ShaderProgram(gl, TextureVertexShader, TextureFragmentShader);
        _shapeVao = gl.GenVertexArray();
        _quadVbo = gl.GenBuffer();
        _instanceVbo = gl.GenBuffer();
        _textureVao = gl.GenVertexArray();
        _textureVbo = gl.GenBuffer();
        _textureIndexVbo = gl.GenBuffer();
        ConfigureShapeVao();
        ConfigureTextureVao();
    }

    /// <summary>Prepares the framebuffer for a frame.</summary>
    /// <param name="framebufferWidth">
    /// Framebuffer width in device pixels; clamped to at least 1.
    /// </param>
    /// <param name="framebufferHeight">
    /// Framebuffer height in device pixels; clamped to at least 1.
    /// </param>
    /// <param name="viewportWidth">
    /// Logical viewport width in CSS pixels; clamped to at least 1 and used by
    /// the shaders to project into clip space.
    /// </param>
    /// <param name="viewportHeight">
    /// Logical viewport height in CSS pixels; clamped to at least 1 and used
    /// both for projection and to flip scissor rectangles.
    /// </param>
    /// <param name="dpi">
    /// Device-to-CSS-pixel ratio. Non-positive values fall back to 1.
    /// </param>
    /// <param name="clearColor">
    /// Straight-alpha RGBA clear colour with components in the range 0 to 1.
    /// </param>
    /// <remarks>
    /// <para>
    /// This resets all batching state: the run counter, the current run, the
    /// clip/opacity/transform stacks and the blend mode. It also sets the GL
    /// viewport to the framebuffer size, disables depth testing, face culling
    /// and scissoring, enables straight-alpha source-over blending, and clears
    /// the colour buffer (but not depth or stencil).
    /// </para>
    /// <para>
    /// The initial blend function is <c>(SrcAlpha, OneMinusSrcAlpha)</c> for
    /// RGB with <c>(One, OneMinusSrcAlpha)</c> for alpha, matching the default
    /// <see cref="MixBlendMode.Normal"/> path in <see cref="ApplyBlend"/>.
    /// </para>
    /// <para>Must be called before the first <see cref="Render(LayoutResult)"/> of a frame.</para>
    /// </remarks>
    public void BeginFrame(
        int framebufferWidth,
        int framebufferHeight,
        int viewportWidth,
        int viewportHeight,
        float dpi,
        Vector4 clearColor)
    {
        _framebufferWidth = Math.Max(1, framebufferWidth);
        _framebufferHeight = Math.Max(1, framebufferHeight);
        _viewportWidth = Math.Max(1, viewportWidth);
        _viewportHeight = Math.Max(1, viewportHeight);
        _dpi = dpi > 0 ? dpi : 1;

        _runCount = 0;
        _current = null;
        _clips.Clear();
        _opacities.Clear();
        _transforms.Clear();
        _clip = ClipState.None;
        _opacity = 1;
        _transform = Matrix3x2.Identity;
        _blend = MixBlendMode.Normal;

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
        _gl.ClearColor(clearColor.X, clearColor.Y, clearColor.Z, clearColor.W);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
    }

    /// <summary>Consumes one layout result.</summary>
    /// <param name="result">
    /// The layout output whose opcodes are batched and drawn. Its constant pool
    /// supplies colours, radii, text, images and border specs.
    /// </param>
    /// <remarks>
    /// The instruction stream is first converted into draw runs and then
    /// executed, so all geometry for this call is drawn before the method
    /// returns. The renderer does not present the frame; the host swaps the
    /// buffers after the last <c>Render</c> call of the frame.
    /// </remarks>
    public void Render(LayoutResult result)
    {
        Process(result.Opcodes, result.Constants);
        ExecuteRuns();
    }

    /// <summary>
    /// Consumes a layout result followed by an overlay stream. The overlay is
    /// batched with the frame but always paints last, so host-owned popups such
    /// as a select menu sit above every element in the tree.
    /// </summary>
    /// <param name="result">The root layout output to paint first.</param>
    /// <param name="overlay">
    /// Extra instructions appended after the root stream, in their own
    /// coordinate space. No state is reset between the streams, so the overlay
    /// inherits any clip, opacity or transform left pushed by the root.
    /// </param>
    /// <param name="overlayConstants">
    /// Constant pool that resolves the ids used by <paramref name="overlay"/>.
    /// </param>
    /// <remarks>
    /// Both streams are processed before any draw call is issued, so an
    /// overlay run that happens to match the pending run's state may join it.
    /// Because runs execute in creation order the overlay still paints after
    /// the root. Unbalanced pushes in the root stream leak into the overlay.
    /// </remarks>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants)
    {
        Process(result.Opcodes, result.Constants);
        Process(overlay, overlayConstants);
        ExecuteRuns();
    }

    /// <summary>
    /// Consumes the app result, host overlays and a second independent root
    /// (the inspector) in one batch, so the second root always paints last.
    /// </summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="overlay">Instructions painted above <paramref name="result"/>.</param>
    /// <param name="overlayConstants">Constant pool for <paramref name="overlay"/>.</param>
    /// <param name="second">
    /// An optional second root, such as an inspector tree, painted above the
    /// overlay. <see langword="null"/> skips it.
    /// </param>
    /// <remarks>
    /// Processing continues across all three streams without resetting state,
    /// so an unbalanced clip, opacity or transform push can affect the next
    /// stream. Draw runs are still created in order and executed in order.
    /// </remarks>
    /// <seealso cref="Render(LayoutResult, LayoutResult)"/>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants,
        LayoutResult? second)
    {
        Process(result.Opcodes, result.Constants);
        Process(overlay, overlayConstants);

        if (second is not null)
        {
            Process(second.Opcodes, second.Constants);
        }

        ExecuteRuns();
    }

    /// <summary>
    /// Consumes the app result, host overlays, a second root (the inspector)
    /// and an overlay that must paint above that second root.
    /// </summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="overlay">Instructions painted above <paramref name="result"/>.</param>
    /// <param name="overlayConstants">Constant pool for <paramref name="overlay"/>.</param>
    /// <param name="second">
    /// An optional second root painted above the first overlay.
    /// <see langword="null"/> skips it, in which case the second overlay follows
    /// the first directly.
    /// </param>
    /// <param name="secondOverlay">
    /// Instructions painted above <paramref name="second"/>, such as tooltips
    /// owned by the inspector.
    /// </param>
    /// <param name="secondOverlayConstants">
    /// Constant pool for <paramref name="secondOverlay"/>.
    /// </param>
    /// <remarks>
    /// This is the richest overload: the paint order is exactly the argument
    /// order, and all streams share one batching pass and one flush. It exists
    /// so the inspector cannot accidentally be occluded by host chrome.
    /// </remarks>
    public void Render(
        LayoutResult result,
        ReadOnlySpan<Instruction> overlay,
        ConstantPool overlayConstants,
        LayoutResult? second,
        ReadOnlySpan<Instruction> secondOverlay,
        ConstantPool secondOverlayConstants)
    {
        Process(result.Opcodes, result.Constants);
        Process(overlay, overlayConstants);

        if (second is not null)
        {
            Process(second.Opcodes, second.Constants);
        }

        Process(secondOverlay, secondOverlayConstants);
        ExecuteRuns();
    }

    /// <summary>Consumes the app result followed by a second independent root.</summary>
    /// <param name="result">The primary root layout output.</param>
    /// <param name="second">
    /// An optional second root painted above <paramref name="result"/>;
    /// <see langword="null"/> draws only the first.
    /// </param>
    /// <remarks>
    /// Convenience overload equivalent to passing an empty overlay to
    /// <see cref="Render(LayoutResult, ReadOnlySpan{Instruction}, ConstantPool, LayoutResult)"/>:
    /// both streams share one batch and one flush.
    /// </remarks>
    public void Render(LayoutResult result, LayoutResult? second)
    {
        Process(result.Opcodes, result.Constants);

        if (second is not null)
        {
            Process(second.Opcodes, second.Constants);
        }

        ExecuteRuns();
    }

    /// <summary>
    /// Translates an instruction stream into draw runs, updating the clip,
    /// opacity, transform and blend stacks as push/pop opcodes are seen.
    /// </summary>
    /// <param name="opcodes">The instructions to consume, in paint order.</param>
    /// <param name="constants">
    /// Pool used to resolve payload ids for radii, borders, shadows,
    /// backgrounds, text, images, scrollbars and transforms.
    /// </param>
    /// <remarks>
    /// <para>
    /// Nothing is drawn here; geometry is only appended to runs, which
    /// <see cref="ExecuteRuns"/> later submits. Opcodes the renderer cannot
    /// execute in this pass (<c>SetScrollOffset</c>, filters, backdrop filters,
    /// clip paths, masks, layers, <c>Nop</c> and frame markers) are intentionally
    /// ignored rather than treated as errors.
    /// </para>
    /// <para>
    /// <c>PushOpacity</c> multiplies and clamps the incoming value to 0..1;
    /// <c>PopOpacity</c>, <c>PopClip</c> and <c>PopTransform</c> silently do
    /// nothing when their stack is empty instead of throwing, so an unbalanced
    /// stream degrades paint state rather than crashing the frame.
    /// </para>
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
                    _blend = (MixBlendMode)instruction.Payload;
                    break;

                case InstructionOpCode.PopBlendMode:
                    _blend = MixBlendMode.Normal;
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

    /// <summary>Appends an axis-aligned filled rectangle to the current solid run.</summary>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <remarks>
    /// Uses SDF mode 0 (plain box) with zero radii, so no fragment discard is
    /// needed unless a clip applies. Empty rectangles and fully transparent
    /// colours are dropped before a run is created, which keeps invisible
    /// instructions from splitting batches. The accumulated opacity is folded
    /// into the alpha channel.
    /// </remarks>
    private void AddRect(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        var run = EnsureShapeRun();
        run.Shapes.Add(new ShapeInstance(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            Vector4.Zero,
            Vector4.Zero,
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height)));
    }

    /// <summary>Appends a filled rectangle with per-corner rounding (SDF mode 1).</summary>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <param name="radius">
    /// Corner radii in CSS pixels; negative values are not clamped here.
    /// </param>
    /// <remarks>
    /// The fragment shader evaluates a rounded-box distance field and applies a
    /// one-pixel smoothstep, so edges stay anti-aliased at any scale without
    /// multi-sampling. Radii are used exactly as given: values larger than half
    /// the box are not scaled down proportionally the way CSS does, so the field
    /// degenerates instead. Empty or transparent input is ignored.
    /// </remarks>
    private void AddRoundedRect(in Rect rect, uint color, CornerRadius radius)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        var radii = ToRadii(radius);
        var run = EnsureShapeRun();
        run.Shapes.Add(new ShapeInstance(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            radii,
            new Vector4(1, 0, 0, 0),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height)));
    }

    /// <summary>Appends an ellipse inscribed in <paramref name="rect"/> (SDF mode 5).</summary>
    /// <param name="rect">Bounding rectangle in CSS pixels.</param>
    /// <param name="color">Fill colour as packed <c>0xAARRGGBB</c>.</param>
    /// <remarks>
    /// The shader normalises the local quad coordinates by half the box and
    /// tests <c>length(p) - 1</c>, so the anti-aliased band is a fixed fraction
    /// of each axis rather than a fixed pixel width. On a very elongated
    /// rectangle the rim therefore looks thicker at the ends of the long axis and
    /// thinner at the ends of the short axis. Zero radii are sent, and the
    /// unused shadow parameters are left at zero.
    /// </remarks>
    private void AddEllipse(in Rect rect, uint color)
    {
        if (rect.IsEmpty || (color >> 24) == 0)
        {
            return;
        }

        var run = EnsureShapeRun();
        run.Shapes.Add(new ShapeInstance(
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
            ColorOf(color, _opacity),
            Vector4.Zero,
            new Vector4(5, 0, 0, 0),
            new Vector4(rect.X, rect.Y, rect.Width, rect.Height)));
    }

    /// <summary>Appends a border as either one SDF ring or up to four solid rectangles.</summary>
    /// <param name="rect">Border box in CSS pixels.</param>
    /// <param name="border">
    /// Per-side widths, styles, colours and the shared corner radius.
    /// <see cref="BorderStyle.None"/> and <see cref="BorderStyle.Hidden"/> sides
    /// are treated as zero width.
    /// </param>
    /// <remarks>
    /// <para>
    /// When all four widths and colours match, a single instance with SDF mode 2
    /// is emitted: the ring is carved out of the rounded box using
    /// <c>max(1, width)</c> as the thickness, which also honours
    /// <see cref="BorderSpec.Radius"/>.
    /// </para>
    /// <para>
    /// Otherwise each visible side becomes its own plain rectangle. The left and
    /// right strips are shortened by the top and bottom widths so the sides do
    /// not overlap the horizontal strips, and the rounded radius is lost. The
    /// four-rectangle fallback therefore paints square corners.
    /// </para>
    /// <para>
    /// Only <see cref="BorderStyle.None"/> and <see cref="BorderStyle.Hidden"/>
    /// suppress a side. Every other style, including dashed, dotted, double and
    /// the 3D variants, is painted solid in the side colour; only widths and
    /// colours are used.
    /// </para>
    /// </remarks>
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
            var run = EnsureShapeRun();
            run.Shapes.Add(new ShapeInstance(
                new Vector4(rect.X, rect.Y, rect.Width, rect.Height),
                ColorOf(top.Color, _opacity),
                ToRadii(border.Radius),
                new Vector4(2, 0, MathF.Max(1, topWidth), 0),
                new Vector4(rect.X, rect.Y, rect.Width, rect.Height)));
            return;
        }

        if (topWidth > 0)
        {
            AddRect(new Rect(rect.X, rect.Y, rect.Width, topWidth), top.Color);
        }

        if (bottomWidth > 0)
        {
            AddRect(new Rect(rect.X, rect.Bottom - bottomWidth, rect.Width, bottomWidth), bottom.Color);
        }

        if (leftWidth > 0)
        {
            AddRect(
                new Rect(rect.X, rect.Y + topWidth, leftWidth, MathF.Max(0, rect.Height - topWidth - bottomWidth)),
                left.Color);
        }

        if (rightWidth > 0)
        {
            AddRect(
                new Rect(rect.Right - rightWidth, rect.Y + topWidth, rightWidth, MathF.Max(0, rect.Height - topWidth - bottomWidth)),
                right.Color);
        }
    }

    /// <summary>Appends an outer or inset box shadow as a single SDF instance.</summary>
    /// <param name="rect">The element's border box in CSS pixels.</param>
    /// <param name="shadow">Colour, offset, blur radius and spread in CSS pixels.</param>
    /// <param name="inset">
    /// <see langword="true"/> for an inner shadow (SDF mode 4), which is clipped
    /// to <paramref name="rect"/>; <see langword="false"/> for an outer shadow
    /// (SDF mode 3).
    /// </param>
    /// <remarks>
    /// <para>
    /// The blur is clamped to at least 1 and the spread to at least 0, so a
    /// <c>0</c> blur still produces a softened edge rather than a hard one: a
    /// two-pixel ramp for an outer shadow and a one-pixel ramp for an inset one.
    /// The shape being shadowed is offset and inflated by the spread; the quad
    /// itself is inflated by <c>blur * 1.5</c> so the falloff fits without
    /// clipping. That is why the instance carries a separate SDF rectangle from
    /// its quad rectangle.
    /// </para>
    /// <para>
    /// An inset shadow does not translate its shape: the quad and the SDF
    /// rectangle are both the element box, the shader discards fragments outside
    /// it and fades the inside edge, and the spread only widens the corner
    /// radii. Shadows with zero alpha are dropped.
    /// </para>
    /// </remarks>
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

        var run = EnsureShapeRun();
        run.Shapes.Add(new ShapeInstance(
            new Vector4(quad.X, quad.Y, quad.Width, quad.Height),
            ColorOf(shadow.Color, _opacity),
            ToRadii(radius),
            new Vector4(inset ? 4 : 3, blur, spread, 0),
            new Vector4(shapeRect.X, shapeRect.Y, shapeRect.Width, shapeRect.Height)));
    }

    /// <summary>Appends a scrollbar track and thumb as two rounded rectangles.</summary>
    /// <param name="rect">Track rectangle in CSS pixels.</param>
    /// <param name="scrollbar">
    /// Orientation, thumb offset and length, thickness and the two colours.
    /// </param>
    /// <remarks>
    /// Both pieces use a radius of half <see cref="ScrollbarSpec.Width"/>, which
    /// produces a fully rounded (pill) bar. The thumb offset is measured from
    /// the start of the track and is applied to Y for vertical bars and to X for
    /// horizontal bars. Transparent track or thumb colours simply produce no
    /// geometry for that piece.
    /// </remarks>
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

    /// <summary>Appends a URL background or a cached gradient texture as a textured quad.</summary>
    /// <param name="spec">
    /// The parsed background layer, already normalised by the paint pass.
    /// </param>
    /// <param name="rect">Destination box in CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// <see cref="ImageKind.Url"/> layers are fetched from
    /// <see cref="ImageCache"/>. When the repeat mode includes a tiled axis the
    /// texture's wrap mode is set to <c>GL_REPEAT</c> and the UV range is scaled
    /// by <c>box size / image size</c>; otherwise the layer is stretched once.
    /// Note that scaling uses CSS pixels against image texels, so a DPI other
    /// than 1 changes the apparent tile size.
    /// </para>
    /// <para>
    /// <see cref="BackgroundRepeat.Space"/> and
    /// <see cref="BackgroundRepeat.Round"/> are not implemented and behave like
    /// <see cref="BackgroundRepeat.NoRepeat"/>. For single-axis repeats both
    /// wrap axes are still set to <c>GL_REPEAT</c>, even though only the tiled
    /// axis receives a scaled UV range, so the other axis samples exactly 0 to 1.
    /// </para>
    /// <para>
    /// Gradient layers are rasterised to a 256x256 texture by
    /// <see cref="GradientCache"/> and sampled with UVs from 0 to 1, so the
    /// gradient is stretched to the box and its stops are spaced in normalised
    /// box space.
    /// </para>
    /// <para>
    /// The wrap mode is written onto the shared cached texture and persists
    /// until another draw changes it, so the last user of a texture wins.
    /// </para>
    /// </remarks>
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
    /// <remarks>
    /// <para>
    /// <see cref="ObjectFit.Fill"/> stretches the whole texture over the box.
    /// <see cref="ObjectFit.Contain"/> picks the smaller scale,
    /// <see cref="ObjectFit.Cover"/> the larger, and
    /// <see cref="ObjectFit.ScaleDown"/> behaves like contain but never scales
    /// up; <see cref="ObjectFit.None"/> keeps natural size. Except for cover, the
    /// fitted image is centred and its size is clamped to the box, so an axis
    /// where the image overflows is rescaled rather than cropped. Cover instead
    /// keeps the box size and crops by shrinking the UV range symmetrically.
    /// </para>
    /// <para>
    /// Wrap mode is forced to <c>GL_CLAMP_TO_EDGE</c> so cropped or stretched
    /// sampling cannot repeat neighbours. The tint is multiplied with the
    /// texture and with the accumulated opacity times
    /// <see cref="ImageSpec.Opacity"/>.
    /// </para>
    /// <para>
    /// Missing sources, zero-sized textures, empty rectangles and non-positive
    /// alpha produce no geometry.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Appends one textured quad per glyph, handling shaping, kerning and
    /// spacing.
    /// </summary>
    /// <param name="rect">
    /// The text box in CSS pixels; only its left and vertical centre are used.
    /// </param>
    /// <param name="command">The string to draw and the style that selects the font.</param>
    /// <param name="flags">Per-instruction flags; currently ignored by the glyph path.</param>
    /// <remarks>
    /// <para>
    /// The baseline is derived from the style's line height (falling back to
    /// <c>1.2 * fontSize</c>) plus the font's ascent and descent measured from an
    /// empty string, which vertically centres the line box in
    /// <paramref name="rect"/>. Each rune is resolved through
    /// <see cref="FontManager"/>, which also performs font fallback; kerning is
    /// applied only when consecutive runes resolve to the same face.
    /// </para>
    /// <para>
    /// Glyph quads are snapped to whole pixels after the fractional pen position
    /// and glyph offset are added. This keeps sampling inside texel centres of
    /// the linearly filtered atlas and avoids washed-out thin stems, while the
    /// unsnapped pen still accumulates advances, kerning and letter spacing.
    /// </para>
    /// <para>
    /// Each glyph becomes a quad in an alpha-texture run, so a paragraph
    /// alternates runs whenever it mixes atlas pages, i.e. when fonts fall back
    /// or the atlas grows mid-text. Glyphs with no texture or zero size are
    /// skipped but still advance the pen.
    /// </para>
    /// </remarks>
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
                var run = EnsureTextRun(glyph.Texture, alphaTexture: true);

                // Glyph quads are sampled from a linearly filtered atlas. If a
                // quad lands on a fractional pixel the sample point can fall on
                // a texel boundary, blending the glyph with its transparent
                // atlas padding. That washes out thin stems (white-on-blue
                // small text in particular). Snapping the origin keeps the
                // sample on texel centres while the fractional pen still
                // carries kerning and advances.
                var x = MathF.Round(pen + glyph.OffsetX);
                var y = MathF.Round(baseline + glyph.OffsetY);
                AddQuadVertices(
                    run,
                    x,
                    y,
                    glyph.Width,
                    glyph.Height,
                    glyph.U0,
                    glyph.V0,
                    glyph.U1,
                    glyph.V1,
                    color);
            }

            pen += glyph.Advance + style.LetterSpacing;
            previousFace = info.Face;
            previousGlyph = info.GlyphIndex;
        }
    }

    /// <summary>
    /// Starts or extends a textured run and emits one quad for
    /// <paramref name="rect"/>.
    /// </summary>
    /// <param name="texture">GL texture name; zero produces no geometry.</param>
    /// <param name="rect">Destination rectangle in CSS pixels.</param>
    /// <param name="uv0">Top-left texture coordinate, normally (0, 0).</param>
    /// <param name="uv1">
    /// Bottom-right texture coordinate; values above 1 tile when the texture
    /// wraps.
    /// </param>
    /// <param name="color">Straight-alpha RGBA multiplied with the sampled texel.</param>
    /// <param name="alphaTexture">
    /// <see langword="true"/> for single-channel glyph atlases, where the shader
    /// uses the red channel as coverage and forces RGB to white; otherwise the
    /// texture is sampled as RGBA.
    /// </param>
    /// <remarks>
    /// Switching <paramref name="texture"/> or <paramref name="alphaTexture"/>
    /// starts a new run and therefore a new draw call. Empty rectangles and
    /// non-positive alpha are dropped.
    /// </remarks>
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

        var run = EnsureTextRun(texture, alphaTexture);
        AddQuadVertices(run, rect.X, rect.Y, rect.Width, rect.Height, uv0.X, uv0.Y, uv1.X, uv1.Y, color);
    }

    /// <summary>
    /// Appends the four vertices of an axis-aligned quad in triangle-strip
    /// order.
    /// </summary>
    /// <param name="run">The textured run that receives the vertices.</param>
    /// <param name="x">Left edge in CSS pixels.</param>
    /// <param name="y">Top edge in CSS pixels.</param>
    /// <param name="width">Quad width in CSS pixels.</param>
    /// <param name="height">Quad height in CSS pixels.</param>
    /// <param name="u0">Left texture coordinate.</param>
    /// <param name="v0">Top texture coordinate.</param>
    /// <param name="u1">Right texture coordinate.</param>
    /// <param name="v1">Bottom texture coordinate.</param>
    /// <param name="color">Straight-alpha RGBA vertex colour.</param>
    /// <remarks>
    /// The strip order is top-left, top-right, bottom-left, bottom-right, so the
    /// two triangles share the top-left to bottom-right diagonal. Textured runs
    /// are drawn with <c>DrawElements</c>, not as a strip; the matching index
    /// pattern is generated per quad in <see cref="ExecuteRuns"/>.
    /// </remarks>
    private static void AddQuadVertices(
        DrawRun run,
        float x,
        float y,
        float width,
        float height,
        float u0,
        float v0,
        float u1,
        float v1,
        Vector4 color)
    {
        var verts = run.Vertices;

        // A four-vertex triangle strip. Strip order keeps the two triangles
        // sharing the TL-BR diagonal: (TL, TR, BL) and (TR, BL, BR).
        verts.Add(new TexturedVertex(x, y, u0, v0, color));
        verts.Add(new TexturedVertex(x + width, y, u1, v0, color));
        verts.Add(new TexturedVertex(x, y + height, u0, v1, color));
        verts.Add(new TexturedVertex(x + width, y + height, u1, v1, color));
    }

    /// <summary>
    /// Intersects a new rectangle with the active clip and makes it current,
    /// saving the previous state on the clip stack.
    /// </summary>
    /// <param name="rect">The new clip rectangle in CSS pixels.</param>
    /// <param name="radii">
    /// Rounded-clip radii packed as top-right, bottom-right, top-left,
    /// bottom-left; ignored when <paramref name="rounded"/> is
    /// <see langword="false"/> and no rounded ancestor exists.
    /// </param>
    /// <param name="rounded">
    /// Whether this clip should be applied with an SDF rounded-box test in the
    /// fragment shader in addition to the scissor rectangle.
    /// </param>
    /// <remarks>
    /// <para>
    /// Rectangles always intersect, so a child clip can only shrink its parent;
    /// the saved state is what <see cref="PopClip"/> restores.
    /// </para>
    /// <para>
    /// Once any clip in the chain is rounded, all descendants report
    /// <see cref="ClipState.Rounded"/>, but the radii passed with the newest
    /// push are stored as-is. An inner rectangular clip under a rounded ancestor
    /// therefore stores zero radii and the shader's rounded test stops firing
    /// for its subtree, so the ancestor's rounding is lost from that point on.
    /// </para>
    /// <para>
    /// Scissor state is not touched here; it is applied per run from the final
    /// <see cref="ClipState"/>.
    /// </para>
    /// </remarks>
    private void PushClip(in Rect rect, Vector4 radii, bool rounded)
    {
        _clips.Push(_clip);
        var next = _clip.Rect.Intersect(rect);
        var useRounded = rounded || _clip.Rounded;
        _clip = new ClipState(
            next,
            useRounded ? radii : Vector4.Zero,
            true,
            useRounded);
    }

    /// <summary>Restores the clip saved by the matching <see cref="PushClip"/>.</summary>
    /// <remarks>
    /// An empty stack is tolerated: the call is ignored and the current clip is
    /// left untouched. Each pop closes exactly one push, so the stacks must be
    /// balanced by the instruction stream for subsequent geometry to inherit the
    /// intended clip.
    /// </remarks>
    private void PopClip()
    {
        if (_clips.Count > 0)
        {
            _clip = _clips.Pop();
        }
    }

    /// <summary>
    /// Returns a solid run that can accept another instance for the current
    /// state.
    /// </summary>
    /// <returns>
    /// The pending solid run when the current clip, transform and blend mode
    /// match; otherwise a newly started solid run.
    /// </returns>
    /// <remarks>
    /// Only the most recently started run is considered, so returning to a
    /// previous state after a state change begins a new run instead of reopening
    /// the older one. This preserves paint order at the cost of extra draw
    /// calls when state alternates.
    /// </remarks>
    private DrawRun EnsureShapeRun()
    {
        if (_current is { IsTextured: false } current && MatchesState(current))
        {
            return current;
        }

        return StartRun(textured: false, texture: 0, alphaTexture: false);
    }

    /// <summary>
    /// Returns a textured run that can accept another quad for the given
    /// texture and clip/transform/blend state.
    /// </summary>
    /// <param name="texture">The GL texture the quad samples.</param>
    /// <param name="alphaTexture">
    /// Whether the shader should read coverage from the red channel, as it does
    /// for glyph atlases.
    /// </param>
    /// <returns>
    /// The pending textured run when both the texture identity and the state
    /// match; otherwise a newly started textured run.
    /// </returns>
    /// <remarks>
    /// Texture identity is the raw GL name rather than the source URL, so
    /// different URLs that resolve to the same cached texture share a run.
    /// Alternating textures (for example an icon between two text runs) splits
    /// the run each time.
    /// </remarks>
    private DrawRun EnsureTextRun(uint texture, bool alphaTexture)
    {
        if (_current is { IsTextured: true } current
            && current.Texture == texture
            && current.AlphaTexture == alphaTexture
            && MatchesState(current))
        {
            return current;
        }

        return StartRun(textured: true, texture, alphaTexture);
    }

    /// <summary>
    /// Tests whether a run was recorded with the same clip, transform and blend
    /// mode as the current state.
    /// </summary>
    /// <param name="run">The candidate run.</param>
    /// <returns><see langword="true"/> when the run can accept more geometry.</returns>
    /// <remarks>
    /// Opacity is not compared because it is baked into colours; geometry drawn
    /// at different opacities can therefore share one run. <see cref="ClipState"/>
    /// is a record struct, so its rectangle, radii and flags are all compared by
    /// value.
    /// </remarks>
    private bool MatchesState(DrawRun run) =>
        run.Clip.Equals(_clip)
        && run.Transform.Equals(_transform)
        && run.Blend == _blend;

    /// <summary>Starts a new run with the current state and makes it current.</summary>
    /// <param name="textured">
    /// Whether the run holds textured quads instead of shape instances.
    /// </param>
    /// <param name="texture">The sampled texture for textured runs; 0 for solid runs.</param>
    /// <param name="alphaTexture">
    /// Whether textured quads read coverage from the red channel.
    /// </param>
    /// <returns>The new run, which is empty.</returns>
    /// <remarks>
    /// Runs are pooled per frame: while <see cref="_runCount"/> is below the
    /// list length, an existing <see cref="DrawRun"/> is reset and reused, which
    /// keeps its vertex and instance list capacities alive across frames. This
    /// is why steady-state rendering allocates little. The previous
    /// <see cref="_current"/> run is not explicitly closed; its recorded state
    /// and geometry are already fixed.
    /// </remarks>
    private DrawRun StartRun(bool textured, uint texture, bool alphaTexture)
    {
        DrawRun run;

        if (_runCount < _runs.Count)
        {
            run = _runs[_runCount];
            run.Reset(textured, texture, alphaTexture, _clip, _transform, _blend);
        }
        else
        {
            run = new DrawRun(textured, texture, alphaTexture, _clip, _transform, _blend);
            _runs.Add(run);
        }

        _runCount++;
        _current = run;
        return run;
    }

    /// <summary>
    /// Submits every run recorded for the frame, in creation order, applying
    /// its scissor, blend mode, program, uniforms and vertex data.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs with no geometry, or whose clip has collapsed to zero width or
    /// height, are skipped without touching GL state.
    /// </para>
    /// <para>
    /// Solid runs bind <see cref="_shapeVao"/>, upload the instance array with
    /// <c>glBufferData</c> (which orphans the previous storage, avoiding a
    /// pipeline stall) and issue one <c>DrawArraysInstanced</c> call over four
    /// vertices. Textured runs bind <see cref="_textureVao"/>, upload the
    /// vertices, regenerate the triangle index pattern for the run's quads,
    /// upload that index array and issue one <c>DrawElements</c> call.
    /// </para>
    /// <para>
    /// The index pattern is rebuilt on the CPU for every textured run; the cost
    /// is six indices per quad but no extra draw calls. Each run with geometry
    /// costs exactly one draw call, so the frame's draw-call count equals the
    /// number of batches created during <see cref="Process"/>.
    /// </para>
    /// <para>
    /// Program, texture unit, VAO and buffer bindings are left as the last run
    /// set them. Buffers are always re-uploaded rather than updated in place, so
    /// no assumption is made about the previous contents.
    /// </para>
    /// </remarks>
    private void ExecuteRuns()
    {
        for (var r = 0; r < _runCount; r++)
        {
            var run = _runs[r];

            if (run.IsTextured ? run.Vertices.Count == 0 : run.Shapes.Count == 0)
            {
                continue;
            }

            if (run.Clip.HasClip && (run.Clip.Rect.Width <= 0 || run.Clip.Rect.Height <= 0))
            {
                continue;
            }

            ApplyScissor(run.Clip);
            ApplyBlend(run.Blend);

            if (!run.IsTextured)
            {
                _shapeProgram.Use();
                SetCommonUniforms(_shapeProgram, run.Clip, run.Transform);

                _gl.BindVertexArray(_shapeVao);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _instanceVbo);
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    CollectionsMarshal.AsSpan(run.Shapes),
                    BufferUsageARB.DynamicDraw);

                _gl.DrawArraysInstanced(PrimitiveType.TriangleStrip, 0, 4, (uint)run.Shapes.Count);
            }
            else
            {
                if (run.Texture == 0 || run.Vertices.Count == 0)
                {
                    continue;
                }

                _textureProgram.Use();
                SetCommonUniforms(_textureProgram, run.Clip, run.Transform);
                _textureProgram.Uniform1("uAlphaTexture", run.AlphaTexture ? 1 : 0);
                _textureProgram.Uniform1("uTexture", 0);
                _gl.ActiveTexture(TextureUnit.Texture0);
                _gl.BindTexture(TextureTarget.Texture2D, run.Texture);

                _gl.BindVertexArray(_textureVao);
                _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _textureVbo);
                _gl.BufferData(
                    BufferTargetARB.ArrayBuffer,
                    CollectionsMarshal.AsSpan(run.Vertices),
                    BufferUsageARB.DynamicDraw);

                var quadCount = run.Vertices.Count / 4;
                var indexCount = quadCount * 6;
                EnsureTextureIndexCapacity(indexCount);

                for (var q = 0; q < quadCount; q++)
                {
                    var vertex = (uint)(q * 4);
                    var offset = q * 6;
                    _textureIndices[offset] = vertex;
                    _textureIndices[offset + 1] = vertex + 1;
                    _textureIndices[offset + 2] = vertex + 2;
                    _textureIndices[offset + 3] = vertex + 2;
                    _textureIndices[offset + 4] = vertex + 1;
                    _textureIndices[offset + 5] = vertex + 3;
                }

                _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _textureIndexVbo);
                _gl.BufferData(
                    BufferTargetARB.ElementArrayBuffer,
                    _textureIndices.AsSpan(0, indexCount),
                    BufferUsageARB.DynamicDraw);

                _gl.DrawElements(
                    PrimitiveType.Triangles,
                    (uint)indexCount,
                    DrawElementsType.UnsignedInt,
                    (void*)0);
            }
        }
    }

    /// <summary>Configures the GL scissor rectangle for a run from its CSS-pixel clip.</summary>
    /// <param name="clip">The clip recorded on the run.</param>
    /// <remarks>
    /// <para>
    /// The rectangle is scaled by <see cref="_dpi"/> and flipped because GL's
    /// scissor origin is the bottom-left of the framebuffer. The left and top
    /// edges are floored and the size is ceiled so the scissor fully covers the
    /// intended area; negative sizes clamp to zero rather than wrapping to a huge
    /// unsigned value.
    /// </para>
    /// <para>
    /// GL scissor state has no stack of its own, so this is called for every run
    /// and <c>GL_SCISSOR_TEST</c> is disabled when the run is unclipped.
    /// </para>
    /// <para>
    /// The conversion uses the logical viewport height, not the framebuffer
    /// height, and does not apply the run's affine transform, so a translated,
    /// scaled or rotated run is scissored at its untransformed position.
    /// </para>
    /// </remarks>
    private void ApplyScissor(in ClipState clip)
    {
        if (!clip.HasClip)
        {
            _gl.Disable(EnableCap.ScissorTest);
            return;
        }

        var x = (int)MathF.Floor(clip.Rect.X * _dpi);
        var y = (int)MathF.Floor((_viewportHeight - clip.Rect.Bottom) * _dpi);
        var width = (int)MathF.Ceiling(clip.Rect.Width * _dpi);
        var height = (int)MathF.Ceiling(clip.Rect.Height * _dpi);

        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x, y, (uint)Math.Max(0, width), (uint)Math.Max(0, height));
    }

    /// <summary>Applies the separable blend function that approximates a mix-blend mode.</summary>
    /// <param name="blend">The mode recorded on the run.</param>
    /// <remarks>
    /// <para>
    /// Only <see cref="MixBlendMode.Multiply"/>,
    /// <see cref="MixBlendMode.Screen"/> and
    /// <see cref="MixBlendMode.PlusLighter"/> have dedicated factors; every
    /// other mode, including <see cref="MixBlendMode.Normal"/>, falls through to
    /// source-over. The blend equation is always <c>GL_FUNC_ADD</c>; there is no
    /// support for difference, overlay or hue-style blending, and no colour
    /// space conversion.
    /// </para>
    /// <para>
    /// The RGB and alpha factors are set separately. Multiply and screen use the
    /// destination colour, so they are not the same as the CSS
    /// <c>mix-blend-mode</c> definitions, which mix on top of source-over
    /// compositing.
    /// </para>
    /// <para>
    /// The state persists until the next run changes it, so it must be applied
    /// for every run rather than only when it differs.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Uploads the viewport, affine transform and clip uniforms shared by both
    /// programs.
    /// </summary>
    /// <param name="program">
    /// The program whose uniforms are written; it must already be in use.
    /// </param>
    /// <param name="clip">The clip for the run being drawn.</param>
    /// <param name="transform">The accumulated CSS-pixel affine transform for the run.</param>
    /// <remarks>
    /// <para>
    /// The matrix is sent as two <c>vec3</c> rows: <c>(M11, M12, M31)</c> and
    /// <c>(M21, M22, M32)</c>. The shader dots the position with each pair before
    /// adding the translation and projecting through <c>uViewport</c>; there is
    /// no perspective divide beyond the fixed <c>w = 1</c>.
    /// </para>
    /// <para>
    /// An unclipped run receives a very large rectangle (±1e7) and zero radii
    /// instead of an enabled/disabled flag, so the fragment shader's clip test
    /// is always evaluated and the branches stay uniform across runs.
    /// </para>
    /// </remarks>
    private void SetCommonUniforms(ShaderProgram program, in ClipState clip, Matrix3x2 transform)
    {
        program.Uniform2("uViewport", _viewportWidth, _viewportHeight);
        program.Uniform3("uTransform0", transform.M11, transform.M12, transform.M31);
        program.Uniform3("uTransform1", transform.M21, transform.M22, transform.M32);

        if (clip.HasClip)
        {
            program.Uniform4("uClipRect", clip.Rect.X, clip.Rect.Y, clip.Rect.Width, clip.Rect.Height);
            program.Uniform4("uClipRadii", clip.Radii.X, clip.Radii.Y, clip.Radii.Z, clip.Radii.W);
        }
        else
        {
            program.Uniform4("uClipRect", -1e7f, -1e7f, 2e7f, 2e7f);
            program.Uniform4("uClipRadii", 0, 0, 0, 0);
        }
    }

    /// <summary>Unpacks a packed ARGB colour into straight-alpha RGBA components.</summary>
    /// <param name="color">Colour packed as <c>0xAARRGGBB</c>.</param>
    /// <param name="opacity">Opacity multiplier applied to the packed alpha.</param>
    /// <returns>Components in the range 0 to 1, in RGB A order.</returns>
    /// <remarks>
    /// The result is not premultiplied; the shaders multiply RGB and alpha
    /// independently and rely on the <c>SrcAlpha</c> blend factor. Opacity is not
    /// clamped, so a caller passing more than 1 can produce alpha above 1, which
    /// GL clamps when the fragment is written.
    /// </remarks>
    private static Vector4 ColorOf(uint color, float opacity)
    {
        var alpha = ((color >> 24) & 0xFF) / 255f * opacity;
        return new Vector4(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            alpha);
    }

    /// <summary>
    /// Packs corner radii into the shader's expected order: top-right,
    /// bottom-right, top-left, bottom-left.
    /// </summary>
    /// <param name="radius">The four CSS-pixel radii.</param>
    /// <returns>
    /// A vector whose XY pair holds the right-side radii and ZW the left-side
    /// radii.
    /// </returns>
    /// <remarks>
    /// <see cref="CornerRadius"/> stores the corners clockwise from top-left
    /// whereas the SDF function selects an XY pair by the sign of X and then a
    /// component by the sign of Y, hence the reordering.
    /// </remarks>
    private static Vector4 ToRadii(CornerRadius radius) => new(
        radius.TopRight,
        radius.BottomRight,
        radius.TopLeft,
        radius.BottomLeft);

    /// <summary>Binds a texture and writes its wrap mode for both axes.</summary>
    /// <param name="texture">The GL texture name to modify; 0 is not filtered here.</param>
    /// <param name="repeat">
    /// <see langword="true"/> for <c>GL_REPEAT</c>, <see langword="false"/> for
    /// <c>GL_CLAMP_TO_EDGE</c>.
    /// </param>
    /// <remarks>
    /// This mutates the texture object owned by <see cref="ImageCache"/>, not
    /// renderer state, and the binding side effect remains after the call. The
    /// mode therefore persists across draws and frames: whichever consumer last
    /// drew the texture decides how later draws sample it until someone changes
    /// it again. Minification and magnification filters are untouched.
    /// </remarks>
    private void SetWrap(uint texture, bool repeat)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        var mode = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, mode);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, mode);
    }

    /// <summary>
    /// Grows the CPU index scratch array so it can hold at least the requested
    /// count.
    /// </summary>
    /// <param name="required">The number of indices the next textured run needs.</param>
    /// <remarks>
    /// Growth is geometric (at least double, never below 256) to keep repeated
    /// runs amortised constant time. The array is never shrunk, so a frame with
    /// many quads keeps that capacity for the process lifetime. Six indices are
    /// required per quad.
    /// </remarks>
    private void EnsureTextureIndexCapacity(int required)
    {
        if (required <= _textureIndices.Length)
        {
            return;
        }

        var size = Math.Max(required, Math.Max(256, _textureIndices.Length * 2));
        Array.Resize(ref _textureIndices, size);
    }

    /// <summary>
    /// Fills <see cref="_shapeVao"/> with the unit quad at attribute 0 and the
    /// five per-instance vectors at attributes 1 to 5.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attribute 0 is a static four-vertex triangle strip covering (0,0) to
    /// (1,1) in local space, with a stride of two floats. The instance buffer
    /// uses location 1 for the rectangle, 2 for colour, 3 for radii, 4 for SDF
    /// parameters and 5 for the SDF rectangle, each a <c>vec4</c> of floats.
    /// </para>
    /// <para>
    /// The instance stride is therefore 80 bytes and every instance attribute
    /// has divisor 1, so one record is advanced per drawn instance rather than
    /// per vertex. The VAO is unbound at the end; the vertex buffer and
    /// attribute pointers remain part of its state. The instance buffer contents
    /// are not set here and are uploaded per run.
    /// </para>
    /// </remarks>
    private void ConfigureShapeVao()
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
        const uint instanceStride = 80;

        for (var i = 0; i < 5; i++)
        {
            var location = (uint)(i + 1);
            _gl.EnableVertexAttribArray(location);
            _gl.VertexAttribPointer(
                location,
                4,
                VertexAttribPointerType.Float,
                false,
                instanceStride,
                (void*)(i * 16));
            _gl.VertexAttribDivisor(location, 1);
        }

        _gl.BindVertexArray(0);
    }

    /// <summary>
    /// Fills <see cref="_textureVao"/> with the interleaved textured-vertex
    /// layout and binds its element buffer.
    /// </summary>
    /// <remarks>
    /// Each vertex is 32 bytes: position (2 floats, offset 0), UV (2 floats,
    /// offset 8) and straight-alpha RGBA colour (4 floats, offset 16). No
    /// attributes are instanced. The index buffer is bound while the VAO is
    /// active, so the element-array binding becomes part of the VAO's state and
    /// survives the final <c>glBindVertexArray(0)</c>.
    /// </remarks>
    private void ConfigureTextureVao()
    {
        _gl.BindVertexArray(_textureVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _textureVbo);
        _gl.BindBuffer(BufferTargetARB.ElementArrayBuffer, _textureIndexVbo);

        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 32, (void*)0);
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, 32, (void*)8);
        _gl.EnableVertexAttribArray(2);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, 32, (void*)16);

        _gl.BindVertexArray(0);
    }

    /// <inheritdoc/>
    /// <summary>
    /// Releases the gradient cache, both shader programs, the two VAOs and the
    /// four vertex/index buffers owned by the renderer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The method is idempotent: after the first call the renderer is inert and
    /// further <c>Render</c> calls would use deleted GL objects. Dispose it only
    /// once the context that created it is current and the frame loop has
    /// stopped.
    /// </para>
    /// <para>
    /// Borrowed dependencies are not disposed: <see cref="FontManager"/>,
    /// <see cref="ImageCache"/> and the <see cref="GL"/> entry point remain
    /// valid, because the host may share them with other renderers.
    /// </para>
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gradients.Dispose();
        _shapeProgram.Dispose();
        _textureProgram.Dispose();

        _gl.DeleteVertexArray(_shapeVao);
        _gl.DeleteVertexArray(_textureVao);
        _gl.DeleteBuffer(_quadVbo);
        _gl.DeleteBuffer(_instanceVbo);
        _gl.DeleteBuffer(_textureVbo);
        _gl.DeleteBuffer(_textureIndexVbo);
    }

    /// <summary>
    /// One batch of geometry sharing a clip, transform, blend mode and texture;
    /// it becomes exactly one draw call.
    /// </summary>
    /// <remarks>
    /// Instances are pooled by <see cref="GLRenderer"/> and reused on later
    /// frames. A run is either solid (<see cref="Shapes"/> is populated) or
    /// textured (<see cref="Vertices"/> is populated), never both. The lists are
    /// cleared by <see cref="Reset"/> but keep their capacity, which is the
    /// point of the pool.
    /// </remarks>
    private sealed class DrawRun
    {
        /// <summary>Creates an empty run with the supplied state.</summary>
        /// <param name="textured">Whether the run will hold textured quads.</param>
        /// <param name="texture">The sampled texture for textured runs.</param>
        /// <param name="alphaTexture">Whether coverage comes from a single channel.</param>
        /// <param name="clip">The clip active when the run started.</param>
        /// <param name="transform">The transform active when the run started.</param>
        /// <param name="blend">The blend mode active when the run started.</param>
        public DrawRun(
            bool textured,
            uint texture,
            bool alphaTexture,
            ClipState clip,
            Matrix3x2 transform,
            MixBlendMode blend)
        {
            Reset(textured, texture, alphaTexture, clip, transform, blend);
        }

        /// <summary>Reinitialises the run for a new batch and drops its geometry.</summary>
        /// <param name="textured">Whether the run will hold textured quads.</param>
        /// <param name="texture">The sampled texture for textured runs.</param>
        /// <param name="alphaTexture">Whether coverage comes from a single channel.</param>
        /// <param name="clip">The clip active when the run starts.</param>
        /// <param name="transform">The transform active when the run starts.</param>
        /// <param name="blend">The blend mode active when the run starts.</param>
        /// <remarks>
        /// Both geometry lists are cleared, so a reused run cannot leak vertices
        /// from the previous frame, but the backing arrays stay allocated.
        /// </remarks>
        public void Reset(
            bool textured,
            uint texture,
            bool alphaTexture,
            ClipState clip,
            Matrix3x2 transform,
            MixBlendMode blend)
        {
            IsTextured = textured;
            Texture = texture;
            AlphaTexture = alphaTexture;
            Clip = clip;
            Transform = transform;
            Blend = blend;
            Shapes.Clear();
            Vertices.Clear();
        }

        /// <summary>
        /// Gets whether this run draws textured quads rather than shape
        /// instances.
        /// </summary>
        public bool IsTextured { get; private set; }

        /// <summary>
        /// Gets the GL texture sampled by every quad in this run; 0 for solid
        /// runs.
        /// </summary>
        public uint Texture { get; private set; }

        /// <summary>
        /// Gets whether the texture's red channel is coverage (glyph atlas) rather
        /// than RGBA colour.
        /// </summary>
        public bool AlphaTexture { get; private set; }

        /// <summary>Gets the clip that was active when the run started.</summary>
        public ClipState Clip { get; private set; }

        /// <summary>
        /// Gets the CSS-pixel affine transform that was active when the run
        /// started.
        /// </summary>
        public Matrix3x2 Transform { get; private set; }

        /// <summary>Gets the blend mode that was active when the run started.</summary>
        public MixBlendMode Blend { get; private set; }

        /// <summary>
        /// Gets the instances drawn by an instanced call; empty for textured
        /// runs.
        /// </summary>
        public List<ShapeInstance> Shapes { get; } = [];

        /// <summary>
        /// Gets the raw quad vertices for a textured run, four per quad in
        /// triangle-strip order; empty for solid runs.
        /// </summary>
        public List<TexturedVertex> Vertices { get; } = [];
    }

    /// <summary>
    /// An immutable snapshot of the clipping state: the intersected rectangle,
    /// the newest radii and whether any ancestor clip was rounded.
    /// </summary>
    /// <remarks>
    /// Value equality is what lets <see cref="MatchesState"/> decide whether two
    /// batches share a clip. The rectangle is stored in CSS pixels before any
    /// transform is applied, matching the vertex shaders' clip test.
    /// </remarks>
    private readonly record struct ClipState(Rect Rect, Vector4 Radii, bool HasClip, bool Rounded)
    {
        /// <summary>
        /// The unclipped state: no scissor, no rounded test, and a sentinel
        /// rectangle far larger than any plausible viewport.
        /// </summary>
        public static readonly ClipState None = new(
            new Rect(-1e7f, -1e7f, 2e7f, 2e7f),
            Vector4.Zero,
            false,
            false);
    }

    /// <summary>
    /// The 80-byte GPU instance record consumed by the shape program: quad
    /// bounds, colour, corner radii, SDF mode parameters and SDF bounds.
    /// </summary>
    /// <remarks>
    /// The sequential layout must match the attribute configuration in
    /// <see cref="ConfigureShapeVao"/> and the attribute declarations in
    /// <see cref="ShapeVertexShader"/>. <see cref="Rect"/> is the quad in CSS
    /// pixels, while <see cref="SdfRect"/> is the shape sampled by the distance
    /// field; the two differ for shadows, where the quad is inflated to make
    /// room for the blur falloff.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ShapeInstance
    {
        /// <summary>Creates an instance from its five <c>vec4</c> slots.</summary>
        /// <param name="rect">Quad bounds in CSS pixels: X, Y, width, height.</param>
        /// <param name="color">Straight-alpha RGBA, with opacity already folded in.</param>
        /// <param name="radii">
        /// Corner radii packed as top-right, bottom-right, top-left,
        /// bottom-left.
        /// </param>
        /// <param name="parameters">
        /// X is the SDF mode (0 solid, 1 rounded rect, 2 border ring, 3 outer
        /// shadow, 4 inset shadow, 5 ellipse); Y is the blur radius used by
        /// modes 3 and 4; Z is the border thickness used by mode 2; W is unused.
        /// </param>
        /// <param name="sdfRect">
        /// Bounds of the shape the distance field is evaluated against.
        /// </param>
        public ShapeInstance(Vector4 rect, Vector4 color, Vector4 radii, Vector4 parameters, Vector4 sdfRect)
        {
            Rect = rect;
            Color = color;
            Radii = radii;
            Parameters = parameters;
            SdfRect = sdfRect;
        }

        /// <summary>Quad bounds in CSS pixels: X, Y, width, height.</summary>
        public readonly Vector4 Rect;

        /// <summary>
        /// Straight-alpha RGBA colour with the frame opacity folded into the
        /// alpha channel.
        /// </summary>
        public readonly Vector4 Color;

        /// <summary>
        /// Corner radii in CSS pixels packed as top-right, bottom-right,
        /// top-left, bottom-left; all zero for square shapes.
        /// </summary>
        public readonly Vector4 Radii;

        /// <summary>
        /// SDF selector and parameters: X is the mode, Y the blur, Z the border
        /// thickness or spread and W is unused.
        /// </summary>
        public readonly Vector4 Parameters;

        /// <summary>Rectangle the distance field is measured against, in CSS pixels.</summary>
        public readonly Vector4 SdfRect;
    }

    /// <summary>
    /// The 32-byte GPU vertex record for textured quads: position, texture
    /// coordinate and colour.
    /// </summary>
    /// <remarks>
    /// The layout is sequential and must match
    /// <see cref="ConfigureTextureVao"/> (offsets 0, 8 and 16) and the attribute
    /// declarations in <see cref="TextureVertexShader"/>. Positions are CSS
    /// pixels and are transformed in the vertex shader; colours are
    /// straight-alpha RGBA multipliers.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TexturedVertex
    {
        /// <summary>Creates a vertex from position, UV and colour.</summary>
        /// <param name="x">X position in CSS pixels.</param>
        /// <param name="y">Y position in CSS pixels.</param>
        /// <param name="u">Horizontal texture coordinate.</param>
        /// <param name="v">Vertical texture coordinate, 0 at the top.</param>
        /// <param name="color">Straight-alpha RGBA multiplier.</param>
        public TexturedVertex(float x, float y, float u, float v, Vector4 color)
        {
            X = x;
            Y = y;
            U = u;
            V = v;
            Color = color;
        }

        /// <summary>X position in CSS pixels.</summary>
        public readonly float X;

        /// <summary>Y position in CSS pixels, increasing downwards.</summary>
        public readonly float Y;

        /// <summary>Horizontal texture coordinate, 0 at the left edge.</summary>
        public readonly float U;

        /// <summary>Vertical texture coordinate, 0 at the top edge.</summary>
        public readonly float V;

        /// <summary>Straight-alpha RGBA multiplier applied to the sampled texel.</summary>
        public readonly Vector4 Color;
    }

    /// <summary>
    /// Vertex shader for instanced shapes: expands the unit quad to the instance
    /// rectangle, forwards SDF inputs and projects through the affine transform
    /// and viewport.
    /// </summary>
    /// <remarks>
    /// The local-space outputs are computed before the transform so the fragment
    /// shader can evaluate the ellipse field in normalised coordinates. The Y
    /// axis is flipped here, so screen positions increase downwards as they do in
    /// CSS pixels, which is the space the fragment shader's clip test uses.
    /// </remarks>
    private const string ShapeVertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aCorner;
        layout(location = 1) in vec4 iRect;
        layout(location = 2) in vec4 iColor;
        layout(location = 3) in vec4 iRadii;
        layout(location = 4) in vec4 iParams;
        layout(location = 5) in vec4 iSdfRect;

        uniform vec2 uViewport;
        uniform vec3 uTransform0;
        uniform vec3 uTransform1;

        out vec2 vScreen;
        out vec2 vLocal;
        out vec2 vHalf;
        out vec4 vColor;
        out vec4 vRadii;
        out vec4 vParams;
        out vec4 vSdfRect;

        void main()
        {
            vec2 p = iRect.xy + aCorner * iRect.zw;
            vScreen = p;
            vLocal = (aCorner - 0.5) * iRect.zw;
            vHalf = iRect.zw * 0.5;
            vColor = iColor;
            vRadii = iRadii;
            vParams = iParams;
            vSdfRect = iSdfRect;

            vec2 t = vec2(dot(uTransform0.xy, p) + uTransform0.z,
                          dot(uTransform1.xy, p) + uTransform1.z);
            gl_Position = vec4(t.x / uViewport.x * 2.0 - 1.0,
                               1.0 - t.y / uViewport.y * 2.0,
                               0.0, 1.0);
        }
        """;

    /// <summary>
    /// Fragment shader for shapes: discards clipped fragments, evaluates the
    /// rounded-box distance field and selects coverage by SDF mode.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mode 0 paints the box opaquely, 1 anti-aliases a rounded rectangle, 2
    /// carves a border ring using <c>vParams.z</c> as thickness, 3 fades an outer
    /// shadow over <c>vParams.y</c> and 4 does the same inside the shape, and 5
    /// evaluates an ellipse from the normalised local quad.
    /// </para>
    /// <para>
    /// Clipping happens twice: GL scissors remove fragments at the framebuffer
    /// level for rectangular clips, and this shader additionally discards against
    /// the untransformed rectangle and the optional rounded-box field. Coverage
    /// below 0.001 is discarded so fully transparent fragments never reach the
    /// blender. Output alpha is <c>vColor.a * coverage</c> with straight RGB.
    /// </para>
    /// </remarks>
    private const string ShapeFragmentShader = """
        #version 330 core
        in vec2 vScreen;
        in vec2 vLocal;
        in vec2 vHalf;
        in vec4 vColor;
        in vec4 vRadii;
        in vec4 vParams;
        in vec4 vSdfRect;

        uniform vec4 uClipRect;
        uniform vec4 uClipRadii;
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
            if (p.x < uClipRect.x || p.y < uClipRect.y ||
                p.x > uClipRect.x + uClipRect.z || p.y > uClipRect.y + uClipRect.w)
            {
                return true;
            }

            if (uClipRadii.x + uClipRadii.y + uClipRadii.z + uClipRadii.w > 0.0)
            {
                vec2 center = uClipRect.xy + uClipRect.zw * 0.5;
                if (sdRoundBox(p - center, uClipRect.zw * 0.5, uClipRadii) > 0.0)
                {
                    return true;
                }
            }

            return false;
        }

        void main()
        {
            if (clipped(vScreen))
            {
                discard;
            }

            int mode = int(vParams.x + 0.5);
            vec2 center = vSdfRect.xy + vSdfRect.zw * 0.5;
            vec2 halfSize = vSdfRect.zw * 0.5;
            float d = sdRoundBox(vScreen - center, halfSize, vRadii);
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

            fragColor = vec4(vColor.rgb, vColor.a * alpha);
        }
        """;

    /// <summary>
    /// Vertex shader for textured quads: passes position, UV and colour through
    /// after applying the affine transform.
    /// </summary>
    /// <remarks>
    /// Positions are sent in CSS pixels and copied untransformed to
    /// <c>vScreen</c> so the fragment shader can perform clip tests in the same
    /// space as shapes.
    /// </remarks>
    private const string TextureVertexShader = """
        #version 330 core
        layout(location = 0) in vec2 aPosition;
        layout(location = 1) in vec2 aUv;
        layout(location = 2) in vec4 aColor;

        uniform vec2 uViewport;
        uniform vec3 uTransform0;
        uniform vec3 uTransform1;

        out vec2 vUv;
        out vec4 vColor;
        out vec2 vScreen;

        void main()
        {
            vUv = aUv;
            vColor = aColor;
            vScreen = aPosition;

            vec2 t = vec2(dot(uTransform0.xy, aPosition) + uTransform0.z,
                          dot(uTransform1.xy, aPosition) + uTransform1.z);
            gl_Position = vec4(t.x / uViewport.x * 2.0 - 1.0,
                               1.0 - t.y / uViewport.y * 2.0,
                               0.0, 1.0);
        }
        """;

    /// <summary>
    /// Fragment shader for textured quads: samples the bound texture, optionally
    /// treats the red channel as alpha coverage, multiplies by the vertex colour
    /// and applies the SDF clip.
    /// </summary>
    /// <remarks>
    /// The result is straight-alpha RGBA (RGB untouched by alpha), so the
    /// source-over blend set in <see cref="BeginFrame"/> composites it
    /// correctly. Like the shape shader it discards clipped fragments and any
    /// fragment whose final alpha is at or below 0.001, so text and image edges
    /// never write transparent pixels that would disturb later blending.
    /// </remarks>
    private const string TextureFragmentShader = """
        #version 330 core
        in vec2 vUv;
        in vec4 vColor;
        in vec2 vScreen;

        uniform sampler2D uTexture;
        uniform int uAlphaTexture;
        uniform vec4 uClipRect;
        uniform vec4 uClipRadii;

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
            if (p.x < uClipRect.x || p.y < uClipRect.y ||
                p.x > uClipRect.x + uClipRect.z || p.y > uClipRect.y + uClipRect.w)
            {
                return true;
            }

            if (uClipRadii.x + uClipRadii.y + uClipRadii.z + uClipRadii.w > 0.0)
            {
                vec2 center = uClipRect.xy + uClipRect.zw * 0.5;
                if (sdRoundBox(p - center, uClipRect.zw * 0.5, uClipRadii) > 0.0)
                {
                    return true;
                }
            }

            return false;
        }

        void main()
        {
            if (clipped(vScreen))
            {
                discard;
            }

            vec4 texel = texture(uTexture, vUv);

            if (uAlphaTexture == 1)
            {
                texel = vec4(1.0, 1.0, 1.0, texel.r);
            }

            vec4 color = vColor * texel;

            if (color.a <= 0.001)
            {
                discard;
            }

            fragColor = color;
        }
        """;
}
