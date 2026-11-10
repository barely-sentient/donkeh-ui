using System.Numerics;

namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    private void BuildDisplayList(LayoutBox root, Rect viewport)
    {
        if (_options.IncludeFrameMarkers)
        {
            _instructions.AddBeginFrame();
        }

        PaintBox(root, 0, 0);

        if (_options.IncludeFrameMarkers)
        {
            _instructions.AddEndFrame();
        }
    }

    private void PaintBox(LayoutBox box, float tx, float ty)
    {
        if (box.Style.Display == Display.None)
        {
            return;
        }

        if (box.IsFixed)
        {
            // Fixed boxes are anchored to the viewport and ignore ancestor scroll
            // translations. Ancestor clips are intentionally not cancelled here;
            // a renderer that supports fixed stacking contexts can lift them.
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

        for (var i = 0; i < images.Length; i++)
        {
            var source = images[i];

            if (source.Kind == ImageKind.None)
            {
                continue;
            }

            var spec = new BackgroundImageSpec
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

    private static Rect BackgroundArea(LayoutBox box, in Rect rect)
    {
        return box.Style.BackgroundClip switch
        {
            BackgroundBox.ContentBox => box.ContentBox.Translate(rect.X - box.BorderBox.X, rect.Y - box.BorderBox.Y),
            BackgroundBox.PaddingBox => box.PaddingBox.Translate(rect.X - box.BorderBox.X, rect.Y - box.BorderBox.Y),
            _ => rect
        };
    }

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

    private void PaintScrollbars(LayoutBox box, float tx, float ty)
    {
        var style = box.Style;
        var showVertical = box.ScrollHeight > box.ClientHeight + 0.5f
            && (style.ShowScrollbars || style.OverflowY is Overflow.Scroll or Overflow.Auto);

        var showHorizontal = box.ScrollWidth > box.ClientWidth + 0.5f
            && (style.ShowScrollbars || style.OverflowX is Overflow.Scroll or Overflow.Auto);

        if (box.ScrollHeight > box.ClientHeight + 0.5f || box.ScrollWidth > box.ClientWidth + 0.5f)
        {
            Console.WriteLine($"scrollbox [{string.Join(' ', box.Node.ClassName)}] h={box.ScrollHeight}/{box.ClientHeight} w={box.ScrollWidth}/{box.ClientWidth} showV={showVertical} showH={showHorizontal} overflow={style.OverflowX}/{style.OverflowY} rect={box.PaddingBox}");
        }

        if (!showVertical && !showHorizontal)
        {
            return;
        }

        const float thickness = 10;

        if (showVertical)
        {
            var trackLength = MathF.Max(0, box.PaddingBox.Height);
            var client = MathF.Max(1, box.ClientHeight);
            var thumbLength = MathF.Max(12, trackLength * (client / MathF.Max(client, box.ScrollHeight)));
            var maxScroll = MathF.Max(1, box.ScrollHeight - client);
            var thumbOffset = Math.Clamp(box.ScrollOffsetY / maxScroll, 0, 1) * (trackLength - thumbLength);

            var spec = new ScrollbarSpec(
                Vertical: true,
                ThumbOffset: thumbOffset,
                ThumbLength: thumbLength,
                TrackLength: trackLength,
                TrackColor: style.ScrollbarTrackColor,
                ThumbColor: style.ScrollbarThumbColor,
                Width: thickness);

            var id = _constants.AddScrollbar(spec);
            var rect = new Rect(
                box.PaddingBox.Right - thickness + tx,
                box.PaddingBox.Y + ty,
                thickness,
                trackLength);

            _instructions.Add(new Instruction(
                InstructionOpCode.DrawScrollbar,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id));
        }

        if (showHorizontal)
        {
            var trackLength = MathF.Max(0, box.PaddingBox.Width);
            var client = MathF.Max(1, box.ClientWidth);
            var thumbLength = MathF.Max(12, trackLength * (client / MathF.Max(client, box.ScrollWidth)));
            var maxScroll = MathF.Max(1, box.ScrollWidth - client);
            var thumbOffset = Math.Clamp(box.ScrollOffsetX / maxScroll, 0, 1) * (trackLength - thumbLength);

            var spec = new ScrollbarSpec(
                Vertical: false,
                ThumbOffset: thumbOffset,
                ThumbLength: thumbLength,
                TrackLength: trackLength,
                TrackColor: style.ScrollbarTrackColor,
                ThumbColor: style.ScrollbarThumbColor,
                Width: thickness);

            var id = _constants.AddScrollbar(spec);
            var rect = new Rect(
                box.PaddingBox.X + tx,
                box.PaddingBox.Bottom - thickness + ty,
                trackLength,
                thickness);

            _instructions.Add(new Instruction(
                InstructionOpCode.DrawScrollbar,
                rect.X,
                rect.Y,
                rect.Width,
                rect.Height,
                id));
        }
    }

    private static uint ResolveColor(ComputedStyle style, uint color)
    {
        return color == ComputedStyle.CurrentColor ? style.Color : color;
    }
}
