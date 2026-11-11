using System.Numerics;

namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Builds the frame's display list from a laid-out box tree, including the
    /// deferred fixed-position pass.
    /// </summary>
    /// <param name="root">Root of the laid-out box tree.</param>
    /// <param name="viewport">
    /// Viewport rectangle in CSS pixels. Currently unused: fixed boxes are
    /// anchored by their already-computed layout positions, which are
    /// viewport-relative.
    /// </param>
    /// <remarks>
    /// <para>
    /// The normal tree is painted first. Boxes with <c>position: fixed</c> are
    /// collected instead of painted, then painted in a second pass with zero
    /// ancestor translation and outside ancestor scopes, so an ancestor's scroll
    /// offset, clip, opacity, transform and filters cannot affect them. This is
    /// what keeps modals, offcanvas panels and tooltips above a scrolling page.
    /// </para>
    /// <para>
    /// If <see cref="LayoutOptions.IncludeFrameMarkers"/> is set, the stream is
    /// wrapped in <see cref="InstructionOpCode.BeginFrame"/> and
    /// <see cref="InstructionOpCode.EndFrame"/> markers for diagnostics.
    /// </para>
    /// <para>
    /// The method appends to the engine's reusable instruction buffer and does
    /// not clear it; the frame setup owns that reset. It must not be called
    /// reentrantly because <see cref="_fixedBoxes"/> and
    /// <see cref="_paintingFixed"/> are shared per-frame state.
    /// </para>
    /// </remarks>
    private void BuildDisplayList(LayoutBox root, Rect viewport)
    {
        if (_options.IncludeFrameMarkers)
        {
            _instructions.AddBeginFrame();
        }

        _fixedBoxes.Clear();
        _paintingFixed = false;
        PaintBox(root, 0, 0);

        // Fixed boxes are positioned against the viewport and must not be
        // clipped by an ancestor scroll container. Paint them after the normal
        // tree so modals, offcanvas panels and tooltips sit above the page.
        _paintingFixed = true;

        for (var i = 0; i < _fixedBoxes.Count; i++)
        {
            PaintBox(_fixedBoxes[i], 0, 0);
        }

        _paintingFixed = false;

        if (_options.IncludeFrameMarkers)
        {
            _instructions.AddEndFrame();
        }
    }

    /// <summary>
    /// Paints one box and recursively its descendants into the instruction
    /// stream.
    /// </summary>
    /// <param name="box">
    /// Box to paint; its border box is already positioned in absolute CSS pixels.
    /// </param>
    /// <param name="tx">
    /// Accumulated horizontal translation from ancestors, including their scroll
    /// offsets, in CSS pixels.
    /// </param>
    /// <param name="ty">
    /// Accumulated vertical translation from ancestors, including their scroll
    /// offsets, in CSS pixels.
    /// </param>
    /// <remarks>
    /// <para>
    /// <c>display: none</c> returns immediately and skips the whole subtree.
    /// <c>display: contents</c> skips the box's own decorations and recurses
    /// straight into its children. <c>visibility: hidden</c> or
    /// <c>collapse</c> suppresses the box's own background, border, text and
    /// replaced content but still walks the children, which may override
    /// visibility.
    /// </para>
    /// <para>
    /// Fixed boxes are deferred to <see cref="BuildDisplayList"/>'s second pass.
    /// During that pass their <paramref name="tx"/> and <paramref name="ty"/> are
    /// reset to zero and no ancestor scope is active, so they ignore ancestor
    /// scroll, clips, opacity, transforms and filters while keeping the position
    /// computed by layout.
    /// </para>
    /// <para>
    /// Paint order: outer shadows, background, border, inset shadows, outline,
    /// replaced content, then <see cref="LayoutOptions.CustomPainter"/>. Text is
    /// painted after that, at the scrolled content origin, followed by children.
    /// Scrollbars are painted last, inside the scroll container's clip.
    /// </para>
    /// <para>
    /// Scoped effects are pushed in a fixed order (opacity, transform, filter,
    /// backdrop filter, clip path, mask, blend mode) and popped in reverse, so
    /// the stream stays balanced. A transform is resolved about its configured
    /// origin, defaulting to the border-box centre, and is skipped when the
    /// resulting matrix is the identity.
    /// </para>
    /// <para>
    /// A scroll container pushes a clip on its padding box, then emits
    /// <see cref="InstructionOpCode.SetScrollOffset"/> and translates text and
    /// children by <c>-scroll</c>; its own border and background stay at the
    /// unscrolled position. The child translation is applied here, so the
    /// renderer must not apply the scroll offset a second time.
    /// </para>
    /// </remarks>
    private void PaintBox(LayoutBox box, float tx, float ty)
    {
        if (box.Style.Display == Display.None)
        {
            return;
        }

        if (box.IsFixed)
        {
            if (!_paintingFixed)
            {
                _fixedBoxes.Add(box);
                return;
            }

            // Fixed boxes are anchored to the viewport and ignore ancestor scroll
            // translations. They are painted in the fixed pass, outside ancestor
            // clips, so a modal is never cut off by a scrolling page.
            tx = 0;
            ty = 0;
        }

        if (box.IsContents)
        {
            PaintChildren(box, tx, ty);
            return;
        }

        var style = box.Style;
        var rect = box.BorderBox.Translate(tx, ty);
        var visible = style.Visibility is not (Visibility.Hidden or Visibility.Collapse);
        var opacity = Math.Clamp(style.Opacity, 0, 1);
        var hasOpacity = opacity < 0.9999f;
        var transformConstant = -1;
        var filterConstant = -1;
        var backdropConstant = -1;
        var clipPathConstant = -1;
        var maskConstant = -1;
        var hasBlend = style.MixBlendMode != MixBlendMode.Normal;

        if (style.Filter is { IsNone: false } filter)
        {
            filterConstant = _constants.AddFilter(filter);
        }

        if (style.BackdropFilter is { IsNone: false } backdrop)
        {
            backdropConstant = _constants.AddFilter(backdrop);
        }

        if (style.ClipPath is { IsNone: false } clipPath)
        {
            clipPathConstant = _constants.AddClipPath(clipPath);
        }

        if (style.Mask is { IsNone: false } mask)
        {
            maskConstant = _constants.AddMask(mask);
        }

        if (style.Transform is { } transform)
        {
            var context = CreateContext(style);
            var matrix = transform.Resolve(rect.Width, rect.Height, in context);

            if (!matrix.IsIdentity)
            {
                var originX = rect.X + style.TransformOriginX.ResolveOr(in context, rect.Width, rect.Width * 0.5f);
                var originY = rect.Y + style.TransformOriginY.ResolveOr(in context, rect.Height, rect.Height * 0.5f);
                var centered = Matrix3x2.CreateTranslation(-originX, -originY)
                    * matrix
                    * Matrix3x2.CreateTranslation(originX, originY);

                transformConstant = _constants.AddTransform(centered);
            }
        }

        if (hasOpacity)
        {
            _instructions.AddPushOpacity(opacity);
        }

        if (transformConstant >= 0)
        {
            _instructions.AddPushTransform(transformConstant);
        }

        if (filterConstant >= 0)
        {
            _instructions.AddPushFilter(rect, filterConstant);
        }

        if (backdropConstant >= 0)
        {
            _instructions.AddPushBackdropFilter(rect, backdropConstant);
        }

        if (clipPathConstant >= 0)
        {
            _instructions.AddPushClipPath(rect, clipPathConstant);
        }

        if (maskConstant >= 0)
        {
            _instructions.AddPushMask(rect, maskConstant);
        }

        if (hasBlend)
        {
            _instructions.AddPushBlendMode(style.MixBlendMode);
        }

        if (visible)
        {
            PaintOuterShadows(box, rect);
            PaintBackground(box, rect);
            PaintBorder(box, rect);
            PaintInsetShadows(box, rect);
            PaintOutline(box, rect);
            PaintReplacedContent(box, rect);
            _options.CustomPainter?.Invoke(box, _instructions, _constants);
        }

        var scrollX = box.ScrollOffsetX;
        var scrollY = box.ScrollOffsetY;
        var clipContents = style.IsScrollContainer;

        if (clipContents)
        {
            var clipRect = box.PaddingBox.Translate(tx, ty);

            if (box.Radius.IsZero)
            {
                _instructions.AddPushClip(clipRect);
            }
            else
            {
                var radius = _constants.AddRadius(box.Radius);
                _instructions.Add(Instruction.PushClipRounded(clipRect, checked((ushort)radius)));
            }
        }

        if (scrollX != 0 || scrollY != 0)
        {
            _instructions.Add(new Instruction(InstructionOpCode.SetScrollOffset, scrollX, scrollY));
        }

        var contentTx = tx - scrollX;
        var contentTy = ty - scrollY;

        if (visible)
        {
            PaintText(box, contentTx, contentTy);
        }

        PaintChildren(box, contentTx, contentTy);

        if (clipContents)
        {
            if (visible)
            {
                PaintScrollbars(box, tx, ty);
            }

            _instructions.AddPopClip();
        }

        if (hasBlend)
        {
            _instructions.AddPopBlendMode();
        }

        if (maskConstant >= 0)
        {
            _instructions.AddPopMask();
        }

        if (clipPathConstant >= 0)
        {
            _instructions.AddPopClipPath();
        }

        if (backdropConstant >= 0)
        {
            _instructions.AddPopBackdropFilter();
        }

        if (filterConstant >= 0)
        {
            _instructions.AddPopFilter();
        }

        if (transformConstant >= 0)
        {
            _instructions.AddPopTransform();
        }

        if (hasOpacity)
        {
            _instructions.AddPopOpacity();
        }
    }

    /// <summary>
    /// Paints a box's children in three z-index passes: negative, zero, then
    /// positive.
    /// </summary>
    /// <param name="box">Parent whose <see cref="LayoutBox.Children"/> are visited.</param>
    /// <param name="tx">Accumulated horizontal translation in CSS pixels.</param>
    /// <param name="ty">Accumulated vertical translation in CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// Three separate scans keep the common <c>z-index: 0</c> path allocation
    /// free while still preserving document order within each layer. This is a
    /// simplified stacking model: it orders a box's direct children by their
    /// effective z-index but does not create stacking contexts, so a descendant
    /// is always painted inside its parent's subtree and cannot rise above a
    /// later sibling of that parent regardless of its own z-index.
    /// </para>
    /// <para>
    /// Children are visited in list order regardless of their own
    /// <see cref="LayoutBox.EffectiveZIndex"/> when it is zero, so equal-z
    /// siblings paint back-to-front as laid out.
    /// </para>
    /// </remarks>
    private void PaintChildren(LayoutBox box, float tx, float ty)
    {
        var children = box.Children;

        // Three passes keep the common z-index == 0 path allocation free while
        // still painting negative and positive positioned layers in order.
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].EffectiveZIndex < 0)
            {
                PaintBox(children[i], tx, ty);
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].EffectiveZIndex == 0)
            {
                PaintBox(children[i], tx, ty);
            }
        }

        for (var i = 0; i < children.Count; i++)
        {
            if (children[i].EffectiveZIndex > 0)
            {
                PaintBox(children[i], tx, ty);
            }
        }
    }

    /// <summary>Emits one draw per non-inset box shadow.</summary>
    /// <param name="box">Box whose <see cref="ComputedStyle.BoxShadows"/> are read.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// The shadow colour is resolved against the box's <c>color</c> so
    /// <c>currentColor</c> works, then interned in the constant pool. Outer
    /// shadows are marked <see cref="InstructionFlags.Repeatable"/>; the renderer
    /// derives the blurred shape from the <c>ShadowSpec</c> and may instance it.
    /// </remarks>
    private void PaintOuterShadows(LayoutBox box, in Rect rect)
    {
        var shadows = box.Style.BoxShadows;

        if (shadows is null || shadows.Length == 0)
        {
            return;
        }

        for (var i = 0; i < shadows.Length; i++)
        {
            var shadow = shadows[i];

            if (shadow.Inset)
            {
                continue;
            }

            var id = _constants.AddShadow(shadow with { Color = ResolveColor(box.Style, shadow.Color) });
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawShadow,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id,
                flags: InstructionFlags.Repeatable));
        }
    }

    /// <summary>Emits one draw per inset box shadow.</summary>
    /// <param name="box">Box whose <see cref="ComputedStyle.BoxShadows"/> are read.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// Emitted after the border so the shadow can darken the padding area, and
    /// with no flags: inset shadows need the element's interior as a mask and
    /// are not batched as repeatable draws.
    /// </remarks>
    private void PaintInsetShadows(LayoutBox box, in Rect rect)
    {
        var shadows = box.Style.BoxShadows;

        if (shadows is null || shadows.Length == 0)
        {
            return;
        }

        for (var i = 0; i < shadows.Length; i++)
        {
            var shadow = shadows[i];

            if (!shadow.Inset)
            {
                continue;
            }

            var id = _constants.AddShadow(shadow with { Color = ResolveColor(box.Style, shadow.Color) });
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawInsetShadow,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id));
        }
    }

    /// <summary>Emits the background colour and background images for a box.</summary>
    /// <param name="box">Box whose background is painted.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// <para>
    /// The colour fill comes first and is clipped to the area selected by
    /// <c>background-clip</c> via <see cref="BackgroundArea"/>. A fully
    /// transparent colour is skipped; a box with zero radius uses a plain rect,
    /// otherwise a rounded rect and a corner-radius constant.
    /// </para>
    /// <para>
    /// Background images reuse <see cref="ComputedStyle.CachedBackgroundSpecs"/>,
    /// which merges the parsed image list with the container-level repeat, clip,
    /// origin and attachment properties. The specs are built once per style and
    /// cached, so later frames only re-intern the resulting ids. Gradient draws
    /// are marked <see cref="InstructionFlags.Repeatable"/>; URL images are not.
    /// </para>
    /// </remarks>
    private void PaintBackground(LayoutBox box, in Rect rect)
    {
        var style = box.Style;
        var color = ResolveColor(style, style.BackgroundColor);
        var area = BackgroundArea(box, rect);

        if ((color & 0xFF000000) != 0)
        {
            if (box.Radius.IsZero)
            {
                _instructions.AddDrawRect(area, color);
            }
            else
            {
                var radius = _constants.AddRadius(box.Radius);
                _instructions.AddDrawRoundedRect(area, color, checked((ushort)radius));
            }
        }

        var images = style.BackgroundImages;

        if (images is null || images.Length == 0)
        {
            return;
        }

        var specs = style.CachedBackgroundSpecs;

        if (specs is null)
        {
            specs = new BackgroundImageSpec[images.Length];

            for (var i = 0; i < images.Length; i++)
            {
                var source = images[i];
                specs[i] = new BackgroundImageSpec
                {
                    Kind = source.Kind,
                    Url = source.Url,
                    GradientKind = source.GradientKind,
                    Angle = source.Angle,
                    Center = source.Center,
                    Radius = source.Radius,
                    Stops = source.Stops,
                    Repeat = style.BackgroundRepeat,
                    Clip = style.BackgroundClip,
                    Origin = style.BackgroundOrigin,
                    Fixed = style.BackgroundFixed
                };
            }

            style.CachedBackgroundSpecs = specs;
        }

        for (var i = 0; i < specs.Length; i++)
        {
            var source = images[i];
            var spec = specs[i];

            if (spec.Kind == ImageKind.None)
            {
                continue;
            }

            var id = _constants.AddBackground(spec);
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawBackgroundImage,
                area.X,
                area.Y,
                area.Width,
                area.Height,
                id,
                flags: source.Kind == ImageKind.Url ? InstructionFlags.None : InstructionFlags.Repeatable));
        }
    }

    /// <summary>Emits a replaced-content image when a provider supplies one.</summary>
    /// <param name="box">Box that may have replaced content, such as an image or canvas.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// <see cref="LayoutOptions.ImageProvider"/> decides whether the box has an
    /// image; returning <see langword="null"/> skips the draw. The image is
    /// interned in the constant pool, and the draw is marked
    /// <see cref="InstructionFlags.Opaque"/> when the image's own opacity is
    /// essentially one.
    /// </remarks>
    private void PaintReplacedContent(LayoutBox box, in Rect rect)
    {
        if (_options.ImageProvider?.Invoke(box) is not { } image)
        {
            return;
        }

        var id = _constants.AddImage(image);
        var flags = image.Opacity < 0.9999f
            ? InstructionFlags.None
            : InstructionFlags.Opaque;

        _instructions.Add(new Instruction(
            InstructionOpCode.DrawImage,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            id,
            flags: flags));
    }

    /// <summary>Selects the rectangle a background is painted into.</summary>
    /// <param name="box">Box whose background-clip and box metrics are read.</param>
    /// <param name="rect">Translated border box in current paint coordinates.</param>
    /// <returns>
    /// The translated content, padding or border box depending on
    /// <c>background-clip</c>; the border box for the default
    /// <see cref="BackgroundBox.BorderBox"/>.
    /// </returns>
    /// <remarks>
    /// The box metrics are stored in absolute unscrolled coordinates while
    /// <paramref name="rect"/> may include an ancestor's scroll translation, so
    /// the delta <c>rect - box.BorderBox</c> is applied to keep the chosen area
    /// aligned with the translated border box. <see cref="BackgroundBox.Text"/>
    /// is not handled specially and falls back to the border box.
    /// </remarks>
    private static Rect BackgroundArea(LayoutBox box, in Rect rect)
    {
        return box.Style.BackgroundClip switch
        {
            BackgroundBox.ContentBox => box.ContentBox.Translate(rect.X - box.BorderBox.X, rect.Y - box.BorderBox.Y),
            BackgroundBox.PaddingBox => box.PaddingBox.Translate(rect.X - box.BorderBox.X, rect.Y - box.BorderBox.Y),
            _ => rect
        };
    }

    /// <summary>Emits the four-sided border for a box.</summary>
    /// <param name="box">Box whose border widths, styles, colours and radius are read.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// <para>
    /// Returns early when all four computed widths are non-positive. Percentage
    /// or font-relative widths are resolved against the border-box width. A side
    /// whose style is <see cref="BorderStyle.None"/> or
    /// <see cref="BorderStyle.Hidden"/> resolves to zero width even if a width
    /// was specified.
    /// </para>
    /// <para>
    /// The four resolved sides and the corner radius form one
    /// <see cref="BorderSpec"/>; <see cref="BorderSpec.IsEmpty"/> suppresses
    /// degenerate specs. The draw itself carries no colour: everything lives in
    /// the constant pool so identical borders can be shared.
    /// </para>
    /// </remarks>
    private void PaintBorder(LayoutBox box, in Rect rect)
    {
        var style = box.Style;

        if (style.BorderWidth.Top.Value <= 0
            && style.BorderWidth.Right.Value <= 0
            && style.BorderWidth.Bottom.Value <= 0
            && style.BorderWidth.Left.Value <= 0)
        {
            return;
        }

        var context = CreateContext(style);
        var widths = style.BorderWidth.Resolve(in context, rect.Width);

        var spec = new BorderSpec(
            new BorderSide(
                style.BorderStyleTop is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Top,
                style.BorderStyleTop,
                ResolveColor(style, style.BorderColorTop)),
            new BorderSide(
                style.BorderStyleRight is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Right,
                style.BorderStyleRight,
                ResolveColor(style, style.BorderColorRight)),
            new BorderSide(
                style.BorderStyleBottom is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Bottom,
                style.BorderStyleBottom,
                ResolveColor(style, style.BorderColorBottom)),
            new BorderSide(
                style.BorderStyleLeft is BorderStyle.None or BorderStyle.Hidden ? 0 : widths.Left,
                style.BorderStyleLeft,
                ResolveColor(style, style.BorderColorLeft)),
            box.Radius);

        if (spec.IsEmpty)
        {
            return;
        }

        var id = _constants.AddBorder(spec);
        _instructions.Add(Instruction.DrawBorder(rect, id));
    }

    /// <summary>Emits the outline drawn just outside the border box.</summary>
    /// <param name="box">Box whose outline properties are read.</param>
    /// <param name="rect">Border box in current paint coordinates.</param>
    /// <remarks>
    /// Skips <see cref="BorderStyle.None"/> and <see cref="BorderStyle.Hidden"/>
    /// and non-positive resolved widths. The rect is inflated by half the outline
    /// width plus <c>outline-offset</c>, because the stroke is centred on the
    /// resulting edge; with an offset of zero the inner edge sits flush against
    /// the border box. All four sides share one style, colour and width, and the
    /// element's corner radius is reused.
    /// </remarks>
    private void PaintOutline(LayoutBox box, in Rect rect)
    {
        var style = box.Style;

        if (style.OutlineStyle is BorderStyle.None or BorderStyle.Hidden)
        {
            return;
        }

        var context = CreateContext(style);
        var width = style.OutlineWidth.ResolveOr(in context, rect.Width, 0);

        if (width <= 0)
        {
            return;
        }

        var offset = style.OutlineOffset.ResolveOr(in context, rect.Width, 0);
        var outlineRect = rect.Inflate(width * 0.5f + offset);
        var side = new BorderSide(width, style.OutlineStyle, style.ResolveOutlineColor());
        var spec = new BorderSpec(side, side, side, side, box.Radius);
        var id = _constants.AddBorder(spec);
        _instructions.Add(new Instruction(
            InstructionOpCode.DrawOutline,
            outlineRect.X,
            outlineRect.Y,
            outlineRect.Width,
            outlineRect.Height,
            id));
    }

    /// <summary>Emits one draw per non-empty visual text line, plus decorations.</summary>
    /// <param name="box">Box whose laid-out text is painted.</param>
    /// <param name="tx">Horizontal translation, already including scroll, in CSS pixels.</param>
    /// <param name="ty">Vertical translation, already including scroll, in CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// Lines are positioned from <see cref="LayoutBox.TextOrigin"/> plus the
    /// line's own offset, so wrapping and alignment are already baked in. Empty
    /// lines and lines with zero width are skipped.
    /// </para>
    /// <para>
    /// Each line interns its text and style in the constant pool, then emits a
    /// <see cref="InstructionOpCode.DrawText"/> with the line box as its rect.
    /// The <see cref="InstructionFlags.RightToLeft"/> flag comes from the text
    /// style's direction. Decorations are emitted immediately after their line,
    /// so they paint over the glyphs.
    /// </para>
    /// </remarks>
    private void PaintText(LayoutBox box, float tx, float ty)
    {
        var text = box.Text;

        if (text is null || text.Lines.Length == 0)
        {
            return;
        }

        var style = text.Style;
        var rtl = style.Direction == Direction.RightToLeft;

        for (var i = 0; i < text.Lines.Length; i++)
        {
            var line = text.Lines[i];

            if (line.Text.Length == 0 || line.Width <= 0)
            {
                continue;
            }

            var x = box.TextOrigin.X + line.X + tx;
            var y = box.TextOrigin.Y + line.Y + ty;
            var id = _constants.AddText(line.Text, style);
            var flags = rtl ? InstructionFlags.RightToLeft : InstructionFlags.None;

            _instructions.Add(Instruction.DrawText(
                new Rect(x, y, line.Width, line.Height),
                id,
                flags));

            PaintTextDecoration(style, x, y, line);
        }
    }

    /// <summary>Emits the enabled text-decoration lines for one laid-out line.</summary>
    /// <param name="style">Text style carrying decoration flags, colour and font size.</param>
    /// <param name="x">Left edge of the line in paint coordinates.</param>
    /// <param name="y">Top edge of the line in paint coordinates.</param>
    /// <param name="line">Line metrics used for the underline and strike positions.</param>
    /// <remarks>
    /// <para>
    /// Thickness is <c>max(1, fontSize * 0.06)</c> CSS pixels. The underline is
    /// placed below the baseline but clamped inside the line box; the overline
    /// sits at the line top; the strike-through is placed at 60% of the baseline
    /// offset.
    /// </para>
    /// <para>
    /// Each enabled decoration is a separate <see cref="InstructionOpCode.DrawLine"/>
    /// with the ARGB colour in its payload, so a line with underline and
    /// line-through emits two instructions.
    /// </para>
    /// </remarks>
    private void PaintTextDecoration(TextStyle style, float x, float y, TextLine line)
    {
        if (style.Decoration == TextDecoration.None)
        {
            return;
        }

        var color = style.Color;
        var thickness = MathF.Max(1, style.FontSize * 0.06f);

        if ((style.Decoration & TextDecoration.Underline) != 0)
        {
            var underlineY = y + MathF.Min(line.Height - thickness, line.Baseline + thickness);
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawLine,
                x,
                underlineY,
                line.Width,
                thickness,
                unchecked((int)color)));
        }

        if ((style.Decoration & TextDecoration.Overline) != 0)
        {
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawLine,
                x,
                y,
                line.Width,
                thickness,
                unchecked((int)color)));
        }

        if ((style.Decoration & TextDecoration.LineThrough) != 0)
        {
            var middle = y + line.Baseline * 0.6f;
            _instructions.Add(new Instruction(
                InstructionOpCode.DrawLine,
                x,
                middle,
                line.Width,
                thickness,
                unchecked((int)color)));
        }
    }

    /// <summary>Emits scrollbar thumbs/tracks for a scroll container.</summary>
    /// <param name="box">Scroll container whose scroll metrics and colours are read.</param>
    /// <param name="tx">Horizontal translation in CSS pixels.</param>
    /// <param name="ty">Vertical translation in CSS pixels.</param>
    /// <remarks>
    /// <para>
    /// <see cref="ScrollbarLayout"/> reports a bar only when the scrollable
    /// extent exceeds the client size by more than half a pixel and the style
    /// asks for one, so hidden-overflow boxes do not get a bar by accident.
    /// </para>
    /// <para>
    /// Tracks are stored unscrolled and translated like other box geometry, but
    /// the call is made after the children are painted and before the clip pop,
    /// so scrollbars always paint above content and are clipped to the padding
    /// box. Each bar becomes one <see cref="InstructionOpCode.DrawScrollbar"/>
    /// whose payload indexes a <see cref="ScrollbarSpec"/>.
    /// </para>
    /// </remarks>
    private void PaintScrollbars(LayoutBox box, float tx, float ty)
    {
        var style = box.Style;

        if (ScrollbarLayout.TryGetVertical(box, out var vertical))
        {
            var spec = new ScrollbarSpec(
                Vertical: true,
                ThumbOffset: vertical.ThumbOffset,
                ThumbLength: vertical.ThumbLength,
                TrackLength: vertical.TrackLength,
                TrackColor: style.ScrollbarTrackColor,
                ThumbColor: style.ScrollbarThumbColor,
                Width: ScrollbarLayout.Thickness);

            var id = _constants.AddScrollbar(spec);
            var rect = vertical.Track.Translate(tx, ty);

            _instructions.Add(new Instruction(
                InstructionOpCode.DrawScrollbar,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id));
        }

        if (ScrollbarLayout.TryGetHorizontal(box, out var horizontal))
        {
            var spec = new ScrollbarSpec(
                Vertical: false,
                ThumbOffset: horizontal.ThumbOffset,
                ThumbLength: horizontal.ThumbLength,
                TrackLength: horizontal.TrackLength,
                TrackColor: style.ScrollbarTrackColor,
                ThumbColor: style.ScrollbarThumbColor,
                Width: ScrollbarLayout.Thickness);

            var id = _constants.AddScrollbar(spec);
            var rect = horizontal.Track.Translate(tx, ty);

            _instructions.Add(new Instruction(
                InstructionOpCode.DrawScrollbar,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id));
        }
    }

    /// <summary>Replaces the <c>currentColor</c> sentinel with the box's resolved colour.</summary>
    /// <param name="style">Style supplying the <c>color</c> value.</param>
    /// <param name="color">
    /// Packed ARGB colour, possibly <see cref="ComputedStyle.CurrentColor"/>.
    /// </param>
    /// <returns>
    /// <paramref name="color"/> unchanged unless it is the sentinel, in which case
    /// <see cref="ComputedStyle.Color"/> is returned.
    /// </returns>
    /// <remarks>
    /// The sentinel lets parsed colours stay shared while still resolving against
    /// the element that uses them; it is applied at paint time, not style time.
    /// </remarks>
    private static uint ResolveColor(ComputedStyle style, uint color)
    {
        return color == ComputedStyle.CurrentColor ? style.Color : color;
    }
}
