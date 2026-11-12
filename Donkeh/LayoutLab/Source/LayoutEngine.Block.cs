namespace Donkeh.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Block flow, plus inline runs when the container directly owns text or
    /// inline children. Floats are removed from the flow and registered in the
    /// box's <see cref="FloatContext"/>; following line boxes and BFC roots avoid
    /// their margin boxes.
    /// </summary>
    /// <param name="box">Block container whose children are laid out.</param>
    /// <param name="content">Content box in absolute coordinates.</param>
    /// <param name="definiteContentHeight">
    /// Known content height, passed through to inline groups; block children
    /// still resolve their own heights.
    /// </param>
    /// <returns>The used content height, including enclosed floats for a BFC root.</returns>
    /// <remarks>
    /// <para>
    /// The element's own text is laid out before its child boxes, followed by
    /// runs of adjacent inline-level children. Each inline run occupies a line
    /// group whose height advances <c>flowY</c>.
    /// </para>
    /// <para>
    /// Out-of-flow children record a static position for later absolute layout,
    /// floats are registered with the shared <see cref="FloatContext"/>, and
    /// block children collapse their vertical margins with the previous
    /// sibling's bottom margin. A trailing bottom margin is applied only when at
    /// least one flow child was seen.
    /// </para>
    /// <para>
    /// When the box establishes a block formatting context, or is the root, its
    /// used height is extended to the bottom of the tallest float so the BFC
    /// encloses its floats. A plain block does not enclose floats.
    /// </para>
    /// </remarks>
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

            LayoutBlockChild(child, content, ref flowY, ref previousMargin, floatContext, definiteContentHeight);
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

    /// <summary>
    /// Places and lays out one in-flow block-level child at the current flow
    /// position.
    /// </summary>
    /// <param name="child">Block child to lay out.</param>
    /// <param name="content">Content box of the parent block.</param>
    /// <param name="flowY">
    /// Absolute y position of the next flow edge. Advanced by the child's border
    /// box height on return.
    /// </param>
    /// <param name="previousMargin">
    /// Bottom margin of the previous in-flow sibling, used for margin collapsing.
    /// Replaced by this child's bottom margin on return.
    /// </param>
    /// <param name="floats">Float context shared with the parent block.</param>
    /// <param name="containingBlockHeight">
    /// Parent content height for percentage heights.
    /// </param>
    /// <remarks>
    /// <para>
    /// Resolving margins happens before placement because both clearance and
    /// float avoidance need the used values. The child's top margin collapses
    /// with <paramref name="previousMargin"/> and then yields to <c>clear</c>.
    /// </para>
    /// <para>
    /// A BFC root must not overlap active floats: it moves below them, and an
    /// auto-width or too-wide explicit-width box is narrowed to the current
    /// float band. Non-BFC blocks ignore floats for sizing but their line boxes
    /// still see them through the shared context.
    /// </para>
    /// <para>
    /// <c>display:contents</c> children zero their edges and contribute only the
    /// laid-out height of their descendants to flow.
    /// </para>
    /// </remarks>
    private void LayoutBlockChild(
        LayoutBox child,
        Rect content,
        ref float flowY,
        ref float previousMargin,
        FloatContext floats,
        float containingBlockHeight)
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
            containingBlockHeight,
            forcedBorderHeight: null,
            fillHeight: false,
            definiteContentHeight: null);

        flowY = borderY + height;
        previousMargin = child.Margin.Bottom;
    }

    /// <summary>Places a floated child inside the current line/block position.</summary>
    /// <param name="child">Floated child to lay out.</param>
    /// <param name="content">Content box of the containing block.</param>
    /// <param name="flowY">Current flow y position before float placement.</param>
    /// <param name="floats">Float context that receives the placed margin box.</param>
    /// <remarks>
    /// <para>
    /// The used width is shrink-to-fit against the containing block (not the
    /// current band), then placement searches for a band wide enough for the
    /// margin box. It moves below active floats up to 64 times, after which it
    /// stays at the last band.
    /// </para>
    /// <para>
    /// Left floats align to the band's left edge and right floats to its right
    /// edge. The registered rectangle includes vertical margins, so following
    /// flow content clears the full margin box. The float does not advance the
    /// parent's flow position.
    /// </para>
    /// </remarks>
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

    /// <summary>Collapses two adjoining vertical margins into one used margin.</summary>
    /// <param name="first">First margin, typically the previous sibling's bottom margin.</param>
    /// <param name="second">Second margin, typically the next sibling's top margin.</param>
    /// <returns>The collapsed margin in CSS pixels.</returns>
    /// <remarks>
    /// Two positive margins use the larger value, two negative margins use the
    /// smaller (more negative) value, and mixed signs add together. This helper
    /// models adjoining sibling margins only; parent-child and empty-block
    /// collapsing are not implemented.
    /// </remarks>
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

    /// <summary>Returns whether a node's text should produce a line box.</summary>
    /// <param name="style">Style of the node that owns the text.</param>
    /// <param name="text">Text to test.</param>
    /// <returns>
    /// <see langword="false"/> for empty text and for whitespace that the
    /// <c>white-space</c> mode collapses; otherwise <see langword="true"/>.
    /// </returns>
    /// <remarks>
    /// Preserved whitespace (<c>pre</c>, <c>pre-wrap</c>, <c>break-spaces</c>)
    /// always lays out, which keeps blank lines and indentation in code blocks.
    /// </remarks>
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

    /// <summary>Returns the usable horizontal band at a vertical position.</summary>
    /// <param name="floats">
    /// Float context to query, or <see langword="null"/> for none.
    /// </param>
    /// <param name="y">Absolute y position to query.</param>
    /// <param name="content">Content box that bounds the band horizontally.</param>
    /// <returns>
    /// The band's left and right edges, or the full content width when no float
    /// context is active.
    /// </returns>
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
    /// <param name="container">Block box that owns the text and inline children.</param>
    /// <param name="text">Text to lay out first, or <see langword="null"/> for none.</param>
    /// <param name="children">Child list containing the inline run.</param>
    /// <param name="start">Index of the first child in the run.</param>
    /// <param name="count">Number of children in the run.</param>
    /// <param name="content">Content box that bounds the line horizontally.</param>
    /// <param name="startY">Absolute y coordinate of the first line.</param>
    /// <param name="definiteContentHeight">Definite height passed to child layout.</param>
    /// <param name="floats">
    /// Active float context, or <see langword="null"/> for none.
    /// </param>
    /// <returns>The total height consumed from <paramref name="startY"/>, never negative.</returns>
    /// <remarks>
    /// <para>
    /// Text is shaped once for the available line width, or through
    /// <see cref="GetTextLayoutInBands"/> when floats are active. A multi-line
    /// text layout is stored on the container and advances the line cursor by its
    /// full height; a single-line layout becomes an inline box on the current
    /// line.
    /// </para>
    /// <para>
    /// Atomic inline children do not break internally. An auto-width child
    /// shrink-to-fits the remaining line space, while an explicitly sized child
    /// keeps its max-content width and moves to the next line when it does not
    /// fit and the line already has content. Line height is the tallest margin
    /// box on the line; vertical-align and baseline alignment are not applied.
    /// </para>
    /// </remarks>
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
        var textAlign = container.Style.TextAlign;
        var alignStart = band.Left;

        // Align the whole inline run (text plus inline children such as a
        // dropdown caret), not just the text. Centring the text on its own
        // leaves only half of the free space for the caret, so it wraps or
        // overlaps.
        if (textAlign is TextAlign.Center or TextAlign.End or TextAlign.Right)
        {
            var inlineWidth = 0f;

            if (!string.IsNullOrEmpty(text))
            {
                var measureStyle = CreateTextStyle(container.Style);
                inlineWidth += _measurer.Measure(
                    ApplyTextTransform(text, container.Style.TextTransform),
                    in measureStyle).Width;
            }

            for (var i = start; i < start + count; i++)
            {
                var inlineChild = children[i];

                if (inlineChild.IsOutOfFlow)
                {
                    continue;
                }

                var childContext = CreateContext(inlineChild.Style);
                inlineWidth += MeasureMaxContentWidth(inlineChild)
                    + inlineChild.Style.Margin.Resolve(in childContext, content.Width).Horizontal;
            }

            var available = MathF.Max(0, band.Right - band.Left);

            if (inlineWidth < available)
            {
                alignStart = textAlign == TextAlign.Center
                    ? band.Left + (available - inlineWidth) * 0.5f
                    : band.Right - inlineWidth;
                lineX = alignStart;
            }
        }

        if (!string.IsNullOrEmpty(text))
        {
            var available = MathF.Max(0, right - lineX);
            var textStyle = container.Style;

            var layout = floats is { Count: > 0 }
                ? GetTextLayoutInBands(container.Node, textStyle, text, content, lineY, floats)
                : GetTextLayout(container.Node, textStyle, text, available, out _);

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
                // Single-line text is positioned by the inline run below, which
                // has already applied text-align to the text and every inline
                // sibling that follows. Re-shape with Start so the text layout
                // does not shift the glyphs inside its line box: that shift is
                // not part of the width the next inline box advances by, so a
                // centred split-button caret would be painted over the label.
                if (textStyle.TextAlign != TextAlign.Start)
                {
                    textStyle = textStyle.Clone();
                    textStyle.TextAlign = TextAlign.Start;
                    layout = floats is { Count: > 0 }
                        ? GetTextLayoutInBands(container.Node, textStyle, text, content, lineY, floats)
                        : GetTextLayout(container.Node, textStyle, text, available, out _);
                }

                container.Text = layout;
                container.TextOrigin = new Point(lineX, lineY);

                // Advance by the shaped width, but never less than the
                // measured advance. Some glyph runs paint slightly past the
                // line-box width, which otherwise lets a following inline box
                // (a split-button caret) sit on top of the last glyph.
                var shapedWidth = layout.Size.Width;
                var measuredTextStyle = CreateTextStyle(textStyle);
                var measuredWidth = _measurer.Measure(
                    ApplyTextTransform(text, textStyle.TextTransform),
                    in measuredTextStyle).Width;
                lineX += MathF.Max(shapedWidth, measuredWidth);
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

            // A text node is a text run, not an atomic inline box. Treating it
            // as a box lets a long run consume the whole line even when only
            // part of it fits, which pushes the following inline element (for
            // example an alert link) onto a new line. Shape the run directly
            // so subsequent inline siblings continue after its last line.
            if (child.Node.IsTextNode
                && child.Children.Count == 0
                && GetNodeText(child.Node) is { Length: > 0 } textNodeText)
            {
                band = BandAt(floats, lineY, content);

                if (band.Y > lineY + 0.001f)
                {
                    lineY = band.Y;
                    lineX = band.Left;
                    lineHeight = 0;
                }

                right = band.Right;
                var textStyle = CreateTextStyle(child.Style);
                var spaceWidth = _measurer.Measure(" ", in textStyle).Width;
                var lineHasContent = lineX > band.Left + 0.001f;

                // Text shaping trims line edges, but a whitespace character at
                // the edge of one inline run is the separator before the next
                // inline element. Preserve it as a measured space so "with" and
                // a following link do not run together.
                if (lineHasContent && char.IsWhiteSpace(textNodeText[0]))
                {
                    lineX += spaceWidth;
                }

                var textAvailable = MathF.Max(0, right - lineX);
                var textLayout = floats is { Count: > 0 }
                    ? GetTextLayoutInBands(child.Node, child.Style, textNodeText, content, lineY, floats)
                    : GetTextLayout(child.Node, child.Style, textNodeText, textAvailable, out _);

                child.Text = textLayout;
                child.TextOrigin = new Point(lineX, lineY);
                child.ApplyBorderBox(lineX, lineY, textLayout.Size.Width, textLayout.Size.Height);

                if (textLayout.Lines.Length > 1)
                {
                    lineY += textLayout.Size.Height;
                    lineX = band.Left;
                    lineHeight = 0;
                }
                else
                {
                    lineX += textLayout.Size.Width;
                    lineHeight = MathF.Max(lineHeight, textLayout.Size.Height);
                }

                if (i + 1 < start + count && char.IsWhiteSpace(textNodeText[^1]))
                {
                    lineX += spaceWidth;
                }

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

            // The fit test uses the child's max-content width, not the width
            // already clamped to the remaining space. Clamping first makes a
            // too-small remainder look like an exact fit, so an inline link
            // after a text run is squeezed into a few pixels instead of moving
            // to the next line (or shrinking only when the line is empty).
            var outer = maxContent + child.Margin.Horizontal;

            if (lineX > band.Left + 0.001f && outer > available + 0.01f)
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

            if (!child.Style.VerticalAlign.IsAuto)
            {
                // Inline boxes are laid out from the line's top edge in this
                // engine. Centre a vertically aligned box in the line and then
                // apply the CSS offset (positive raises it), so a dropdown
                // caret sits on the text line instead of hugging the button
                // top.
                var line = MathF.Max(lineHeight, child.Margin.Top + height + child.Margin.Bottom);
                var targetTop = lineY
                    + MathF.Max(0, (line - child.Margin.Top - height - child.Margin.Bottom) * 0.5f);
                child.Translate(0, targetTop - child.BorderBox.Y);
            }

            lineX = borderX + child.BorderBox.Width + child.Margin.Right;
            lineHeight = MathF.Max(lineHeight, child.Margin.Top + height + child.Margin.Bottom);
        }

        return MathF.Max(0, lineY + lineHeight - startY);
    }
}
