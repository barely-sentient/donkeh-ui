using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Ignite.LayoutLab;
using Silk.NET.OpenGL;

namespace Ignite.Windowing;

/// <summary>
/// Executes the layout engine's instruction stream with OpenGL. Shapes and text
/// are accumulated into ordered batches; state changes only flush the batch they
/// affect, so a typical UI frame becomes a handful of draw calls.
/// </summary>
public sealed unsafe class GLRenderer : IDisposable
{
    private readonly GL _gl;
    private readonly FontManager _fonts;
    private readonly ImageCache _images;
    private readonly GradientCache _gradients;
    private readonly ShaderProgram _shapeProgram;
    private readonly ShaderProgram _textureProgram;

    private readonly uint _shapeVao;
    private readonly uint _quadVbo;
    private readonly uint _instanceVbo;
    private readonly uint _textureVao;
    private readonly uint _textureVbo;
    private readonly uint _textureIndexVbo;
    private uint[] _textureIndices = new uint[256];

    private readonly List<DrawRun> _runs = [];
    private readonly Stack<ClipState> _clips = new();
    private readonly Stack<float> _opacities = new();
    private readonly Stack<Matrix3x2> _transforms = new();

    private ClipState _clip = ClipState.None;
    private float _opacity = 1;
    private Matrix3x2 _transform = Matrix3x2.Identity;
    private MixBlendMode _blend = MixBlendMode.Normal;
    private DrawRun? _current;

    private int _viewportWidth = 1;
    private int _viewportHeight = 1;
    private int _framebufferWidth = 1;
    private int _framebufferHeight = 1;
    private float _dpi = 1;
    private bool _disposed;

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

        _runs.Clear();
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

    /// <summary>Consumes the app result followed by a second independent root.</summary>
    public void Render(LayoutResult result, LayoutResult? second)
    {
        Process(result.Opcodes, result.Constants);

        if (second is not null)
        {
            Process(second.Opcodes, second.Constants);
        }

        ExecuteRuns();
    }

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
                var x = pen + glyph.OffsetX;
                var y = baseline + glyph.OffsetY;
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

    private void PopClip()
    {
        if (_clips.Count > 0)
        {
            _clip = _clips.Pop();
        }
    }

    private DrawRun EnsureShapeRun()
    {
        if (_current is { IsTextured: false } current && MatchesState(current))
        {
            return current;
        }

        return StartRun(textured: false, texture: 0, alphaTexture: false);
    }

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

    private bool MatchesState(DrawRun run) =>
        run.Clip.Equals(_clip)
        && run.Transform.Equals(_transform)
        && run.Blend == _blend;

    private DrawRun StartRun(bool textured, uint texture, bool alphaTexture)
    {
        var run = new DrawRun(textured, texture, alphaTexture, _clip, _transform, _blend);
        _runs.Add(run);
        _current = run;
        return run;
    }

    private void ExecuteRuns()
    {
        foreach (var run in _runs)
        {
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

    private static Vector4 ColorOf(uint color, float opacity)
    {
        var alpha = ((color >> 24) & 0xFF) / 255f * opacity;
        return new Vector4(
            ((color >> 16) & 0xFF) / 255f,
            ((color >> 8) & 0xFF) / 255f,
            (color & 0xFF) / 255f,
            alpha);
    }

    private static Vector4 ToRadii(CornerRadius radius) => new(
        radius.TopRight,
        radius.BottomRight,
        radius.TopLeft,
        radius.BottomLeft);

    private void SetWrap(uint texture, bool repeat)
    {
        _gl.BindTexture(TextureTarget.Texture2D, texture);
        var mode = (int)(repeat ? GLEnum.Repeat : GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, mode);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, mode);
    }

    private void EnsureTextureIndexCapacity(int required)
    {
        if (required <= _textureIndices.Length)
        {
            return;
        }

        var size = Math.Max(required, Math.Max(256, _textureIndices.Length * 2));
        Array.Resize(ref _textureIndices, size);
    }

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

    private sealed class DrawRun
    {
        public DrawRun(
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
        }

        public bool IsTextured { get; }

        public uint Texture { get; }

        public bool AlphaTexture { get; }

        public ClipState Clip { get; }

        public Matrix3x2 Transform { get; }

        public MixBlendMode Blend { get; }

        public List<ShapeInstance> Shapes { get; } = [];

        public List<TexturedVertex> Vertices { get; } = [];
    }

    private readonly record struct ClipState(Rect Rect, Vector4 Radii, bool HasClip, bool Rounded)
    {
        public static readonly ClipState None = new(
            new Rect(-1e7f, -1e7f, 2e7f, 2e7f),
            Vector4.Zero,
            false,
            false);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct ShapeInstance
    {
        public ShapeInstance(Vector4 rect, Vector4 color, Vector4 radii, Vector4 parameters, Vector4 sdfRect)
        {
            Rect = rect;
            Color = color;
            Radii = radii;
            Parameters = parameters;
            SdfRect = sdfRect;
        }

        public readonly Vector4 Rect;
        public readonly Vector4 Color;
        public readonly Vector4 Radii;
        public readonly Vector4 Parameters;
        public readonly Vector4 SdfRect;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct TexturedVertex
    {
        public TexturedVertex(float x, float y, float u, float v, Vector4 color)
        {
            X = x;
            Y = y;
            U = u;
            V = v;
            Color = color;
        }

        public readonly float X;
        public readonly float Y;
        public readonly float U;
        public readonly float V;
        public readonly Vector4 Color;
    }

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
