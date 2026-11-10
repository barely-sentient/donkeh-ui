namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Block flow, plus inline runs when the container directly owns text or
    /// inline children. Floats are removed from the flow and registered in the
    /// box's <see cref="FloatContext"/>; following line boxes and BFC roots avoid
    /// their margin boxes.
    /// </summary>
    private float LayoutBlockChildren(LayoutBox box, Rect content, float definiteContentHeight)
    {
        var children = box.Children;
        var text = GetNodeText(box.Node);
        var hasText = !string.IsNullOrEmpty(text) && ShouldLayoutText(box.Style, text);
        var textConsumed = !hasText;
        var floatContext = box.FloatContext ?? new FloatContext();

        var flowY = content.Y;
        var previousMargin = 0f;
        var hasFlow = false;
        var index = 0;

        while (true)
        {
            if (!textConsumed)
            {
                // The element's own text comes before its children. Inline
                // children that follow start on the same line when they fit.
                var inlineStart = index;

                while (index < children.Count
                    && !children[index].IsOutOfFlow
                    && children[index].Style.Float == Float.None
                    && IsInlineLevel(children[index]))
                {
                    index++;
                }

                var height = LayoutInlineGroup(
                    box,
                    text,
                    children,
                    inlineStart,
                    index - inlineStart,
                    content,
                    flowY,
                    definiteContentHeight,
                    floatContext);

                flowY += height;
                previousMargin = 0;
                hasFlow = true;
                textConsumed = true;
                continue;
            }

            if (index >= children.Count)
            {
                break;
            }

            var child = children[index];

            if (child.IsOutOfFlow)
            {
                child.StaticPosition = new Point(content.X + child.Margin.Left, flowY);
                index++;
                continue;
            }

            if (child.Style.Float != Float.None)
            {
                LayoutFloatChild(child, content, flowY, floatContext);
                hasFlow = true;
                index++;
                continue;
            }

            if (IsInlineLevel(child))
            {
                var inlineStart = index;

                while (index < children.Count
                    && !children[index].IsOutOfFlow
                    && children[index].Style.Float == Float.None
                    && IsInlineLevel(children[index]))
                {
                    index++;
                }

                var height = LayoutInlineGroup(
                    box,
                    null,
                    children,
                    inlineStart,
                    index - inlineStart,
                    content,
                    flowY,
                    definiteContentHeight,
                    floatContext);

                flowY += height;
                previousMargin = 0;
                hasFlow = true;
                continue;
            }

            LayoutBlockChild(child, content, ref flowY, ref previousMargin, floatContext);
            hasFlow = true;
            index++;
        }

        if (hasFlow)
        {
            flowY += previousMargin;
        }

        // A block formatting context encloses its floats; a plain block does not.
        if (EstablishesBlockFormattingContext(box.Style) || ReferenceEquals(box, _root))
        {
            var floatBottom = floatContext.MaxBottom();

            if (float.IsFinite(floatBottom))
            {
                flowY = MathF.Max(flowY, floatBottom);
            }
        }

        return MathF.Max(0, flowY - content.Y);
    }

    private void LayoutBlockChild(
        LayoutBox child,
        Rect content,
        ref float flowY,
        ref float previousMargin,
        FloatContext floats)
    {
        // Resolve margins first: clearance and float avoidance both need them.
        PrepareBox(child, content.Width);

        if (child.IsContents)
        {
            // display: contents generates no box, so its own margin, border and
            // padding never affect placement or the parent's flow position.
            child.Margin = Thickness.Zero;
            child.Border = Thickness.Zero;
            child.Padding = Thickness.Zero;
        }

        var bfc = EstablishesBlockFormattingContext(child.Style);
        var top = flowY + CollapseMargins(previousMargin, child.Margin.Top);
        var clearY = floats.ClearanceY(child.Style.Clear, top, content.X, content.Right);

        if (clearY > top + 0.001f)
        {
            top = clearY;
            previousMargin = 0;
        }

        var band = floats.BandAt(top, content.X, content.Right);

        if (bfc && band.Y > top + 0.001f)
        {
            top = band.Y;
            band = floats.BandAt(top, content.X, content.Right);
        }

        // A BFC root's border box may not overlap a float. If it has an
        // explicit width that does not fit in the current band, it moves below
        // the float just like an auto-width box would.
        if (bfc && !child.Style.Width.IsAuto && !child.IsContents)
        {
            var needed = MeasureMaxContentWidth(child) + child.Margin.Horizontal;

            if (band.Width + 0.01f < needed)
            {
                var below = floats.ClearanceY(Clear.Both, band.Y, content.X, content.Right);

                if (below > top + 0.001f)
                {
                    top = below;
                    band = floats.BandAt(top, content.X, content.Right);
                }
            }
        }

        var availableBand = bfc ? band.Width : content.Width;
        float? forcedWidth = null;

        if (bfc && child.Style.Width.IsAuto && !child.IsContents && availableBand < content.Width - 0.001f)
        {
            forcedWidth = MathF.Max(0, availableBand - child.Margin.Horizontal);
        }

        var width = PrepareAndMeasureWidth(
            child,
            content.Width,
            forcedWidth,
            fillWidth: !child.IsContents && !bfc,
            shrinkToFit: false);

        var left = bfc && !band.IsEmpty ? band.Left : content.X;
        var borderX = left + child.Margin.Left;
        var borderY = top;

        if (child.IsContents)
        {
            child.Margin = Thickness.Zero;
            child.Border = Thickness.Zero;
            child.Padding = Thickness.Zero;
            child.ApplyBorderBox(borderX, borderY, width, 0);

            var used = LayoutContent(child, child.ContentBox, float.NaN);
            child.ApplyBorderBox(borderX, borderY, width, used);
            child.Baseline = BaselineOf(child);
            UpdateScrollMetrics(child);

            flowY = borderY + used;
            previousMargin = 0;
            return;
        }

        var height = LayoutPreparedBox(
            child,
            borderX,
            borderY,
            content.Width,
            _options.ViewportHeight,
            forcedBorderHeight: null,
            fillHeight: false,
            definiteContentHeight: null);

        flowY = borderY + height;
        previousMargin = child.Margin.Bottom;
    }

    /// <summary>Places a floated child inside the current line/block position.</summary>
    private void LayoutFloatChild(
        LayoutBox child,
        Rect content,
        float flowY,
        FloatContext floats)
    {
        PrepareBox(child, content.Width);

        var y = flowY;
        var clearY = floats.ClearanceY(child.Style.Clear, y, content.X, content.Right);

        if (clearY > y + 0.001f)
        {
            y = clearY;
        }

        var maxContent = MeasureMaxContentWidth(child);
        var marginWidth = child.Margin.Horizontal;

        // The used width of a float is shrink-to-fit against the containing
        // block, not against the current band. Placement then either fits beside
        // the active floats or moves down until it does.
        var usedBorderWidth = child.Style.Width.IsAuto
            ? MathF.Min(maxContent, MathF.Max(0, content.Width - marginWidth))
            : maxContent;
        var outerWidth = usedBorderWidth + marginWidth;

        FloatBand band = default;

        for (var guard = 0; guard < 64; guard++)
        {
            band = floats.BandAt(y, content.X, content.Right);
            y = band.Y;

            if (band.Width + 0.01f >= outerWidth)
            {
                break;
            }

            // The float cannot fit beside the active floats; move below them.
            var bottom = floats.ClearanceY(Clear.Both, y, content.X, content.Right);

            if (bottom > y + 0.001f)
            {
                y = bottom;
                continue;
            }

            break;
        }

        var borderWidth = PrepareAndMeasureWidth(
            child,
            content.Width,
            forcedBorderWidth: usedBorderWidth,
            fillWidth: false,
            shrinkToFit: false);

        outerWidth = borderWidth + marginWidth;
        var marginX = child.Style.Float == Float.Left
            ? band.Left
            : MathF.Max(band.Left, band.Right - outerWidth);

        var borderX = marginX + child.Margin.Left;
        var borderY = y + child.Margin.Top;

        var height = LayoutPreparedBox(
            child,
            borderX,
            borderY,
            content.Width,
            _options.ViewportHeight,
            forcedBorderHeight: null,
            fillHeight: false,
            definiteContentHeight: null);

        child.StaticPosition = new Point(borderX, borderY);
        floats.Add(
            new Rect(marginX, y, outerWidth, height + child.Margin.Vertical),
            child.Style.Float);
    }

    private static float CollapseMargins(float first, float second)
    {
        if (first >= 0 && second >= 0)
        {
            return MathF.Max(first, second);
        }

        if (first <= 0 && second <= 0)
        {
            return MathF.Min(first, second);
        }

        return first + second;
    }

    private static bool ShouldLayoutText(ComputedStyle style, string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        if (style.WhiteSpace is WhiteSpace.Pre or WhiteSpace.PreWrap or WhiteSpace.BreakSpaces)
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(text);
    }

    private static FloatBand BandAt(FloatContext? floats, float y, in Rect content)
    {
        return floats is null
            ? new FloatBand(y, content.X, content.Right)
            : floats.BandAt(y, content.X, content.Right);
    }

    /// <summary>
    /// Lays out a text run and/or a run of inline-level children on lines that
    /// start at <paramref name="startY"/>. Atomic inline boxes are shrink-to-fit
    /// and wrap as a unit; text wraps through <see cref="TextLayoutEngine"/>.
    /// Lines avoid any active floats.
    /// </summary>
    private float LayoutInlineGroup(
        LayoutBox container,
        string? text,
        List<LayoutBox> children,
        int start,
        int count,
        Rect content,
        float startY,
        float definiteContentHeight,
        FloatContext? floats)
    {
        var band = BandAt(floats, startY, content);
        var lineX = band.Left;
        var lineY = band.Y;
        var lineHeight = 0f;
        var right = band.Right;

        if (!string.IsNullOrEmpty(text))
        {
            var available = MathF.Max(0, right - lineX);
            var layout = floats is { Count: > 0 }
                ? GetTextLayoutInBands(container.Style, text, content, lineY, floats)
                : GetTextLayout(container.Style, text, available, out _);

            if (layout.Lines.Length > 1)
            {
                container.Text = layout;
                container.TextOrigin = new Point(content.X, lineY);
                lineY += layout.Size.Height;
                lineX = content.X;
                lineHeight = 0;
            }
            else
            {
                container.Text = layout;
                container.TextOrigin = new Point(content.X, lineY);
                lineX = band.Left + layout.Size.Width;
                lineHeight = MathF.Max(lineHeight, layout.Size.Height);
            }
        }

        for (var i = start; i < start + count; i++)
        {
            var child = children[i];

            if (child.IsOutOfFlow)
            {
                child.StaticPosition = new Point(lineX, lineY);
                continue;
            }

            band = BandAt(floats, lineY, content);

            if (band.Y > lineY + 0.001f)
            {
                lineY = band.Y;
                lineX = band.Left;
                lineHeight = 0;
            }

            lineX = MathF.Max(lineX, band.Left);
            right = band.Right;

            var available = MathF.Max(0, right - lineX);
            var maxContent = MeasureMaxContentWidth(child);

            // Re-resolve margins before checking the fit; percentages use the
            // container's content width as the basis.
            PrepareBox(child, content.Width);
            var autoWidth = child.Style.Width.IsAuto;
            var outer = (autoWidth ? MathF.Min(maxContent, available) : maxContent)
                + child.Margin.Horizontal;

            if (lineX > band.Left + 0.001f && outer > available)
            {
                lineY += lineHeight;
                lineHeight = 0;
                band = BandAt(floats, lineY, content);
                lineY = band.Y;
                lineX = band.Left;
                right = band.Right;
                available = MathF.Max(0, right - lineX);
            }

            // An explicitly sized inline box wraps rather than shrinking. An
            // auto-width box (inline-block with text) shrink-to-fits.
            var contentAvailable = MathF.Max(0, available - child.Margin.Horizontal);
            var width = autoWidth
                ? MathF.Min(maxContent, contentAvailable)
                : maxContent;

            PrepareAndMeasureWidth(
                child,
                content.Width,
                forcedBorderWidth: width,
                fillWidth: false,
                shrinkToFit: false,
                allowAutoMargins: false);

            var borderX = lineX + child.Margin.Left;
            var borderY = lineY + child.Margin.Top;

            var height = LayoutPreparedBox(
                child,
                borderX,
                borderY,
                content.Width,
                definiteContentHeight,
                forcedBorderHeight: null,
                fillHeight: false,
                definiteContentHeight: null);

            lineX = borderX + child.BorderBox.Width + child.Margin.Right;
            lineHeight = MathF.Max(lineHeight, child.Margin.Top + height + child.Margin.Bottom);
        }

        return MathF.Max(0, lineY + lineHeight - startY);
    }
}
