namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// A single-line flex layout with wrapping, grow/shrink, gap, justify and
    /// cross-axis alignment. The implementation follows the CSS order of
    /// operations closely enough that common toolbars, cards and sidebars lay out
    /// as authored without a second pass over the DOM.
    /// </summary>
    private float LayoutFlex(LayoutBox box, Rect content, float? definiteContentHeight)
    {
        var style = box.Style;
        var row = style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse;
        var reverse = style.FlexDirection is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

        var items = CollectFlowChildren(box);
        StableSortByOrder(items);

        var count = items.Count;

        if (count == 0)
        {
            return 0;
        }

        var context = CreateContext(style);
        var mainGap = ResolveGap(row ? style.ColumnGap : style.RowGap, in context, row ? content.Width : content.Height);
        var crossGap = ResolveGap(row ? style.RowGap : style.ColumnGap, in context, row ? content.Height : content.Width);

        // Column containers with an auto height grow to their items instead of
        // distributing free space.
        var mainAvailable = row
            ? content.Width
            : (definiteContentHeight is { } height ? height : float.PositiveInfinity);

        var bases = new float[count];
        var mains = new float[count];
        var minMains = new float[count];
        var mainMargins = new float[count];
        var lineOf = new int[count];
        var lineStarts = new List<int>(4) { 0 };

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            bases[i] = ResolveFlexBase(item, row, content.Width, content.Height, in context);
            mainMargins[i] = row ? item.Margin.Horizontal : item.Margin.Vertical;

            // CSS gives a flex item with an automatic minimum size and visible
            // overflow a min-content floor on the main axis. Without it buttons,
            // inputs and cards get squashed by a busy column container.
            if (!row && item.Style.MinHeight.IsAuto && !item.Style.IsScrollContainer)
            {
                minMains[i] = MeasureMaxContentHeight(item, content.Width);
            }

            else if (row
                && item.Style.MinWidth.IsAuto
                && item.Style.Width.IsAuto
                && !item.Style.IsScrollContainer)
            {
                minMains[i] = MeasureMaxContentWidth(item);
            }

            if (style.FlexWrap != FlexWrap.NoWrap
                && i > lineStarts[^1]
                && float.IsFinite(mainAvailable))
            {
                var lineStart = lineStarts[^1];
                var used = 0f;

                for (var j = lineStart; j < i; j++)
                {
                    used += bases[j] + mainMargins[j];

                    if (j > lineStart)
                    {
                        used += mainGap;
                    }
                }

                if (used + mainGap + bases[i] + mainMargins[i] > mainAvailable)
                {
                    lineStarts.Add(i);
                }
            }

            lineOf[i] = lineStarts.Count - 1;
        }

        lineStarts.Add(count);

        var lineCross = new float[lineStarts.Count - 1];
        var crossSizes = new float[count];

        if (!float.IsFinite(mainAvailable))
        {
            mainAvailable = 0;

            for (var i = 0; i < count; i++)
            {
                mainAvailable += bases[i] + mainMargins[i];

                if (i > 0)
                {
                    mainAvailable += mainGap;
                }
            }
        }

        // Resolve flexible lengths per line.
        for (var line = 0; line < lineStarts.Count - 1; line++)
        {
            var start = lineStarts[line];
            var end = lineStarts[line + 1];
            var used = 0f;

            for (var i = start; i < end; i++)
            {
                used += bases[i] + mainMargins[i];
            }

            used += mainGap * Math.Max(0, end - start - 1);
            var free = mainAvailable - used;

            if (free > 0)
            {
                var grow = 0f;

                for (var i = start; i < end; i++)
                {
                    grow += MathF.Max(0, items[i].Style.FlexGrow);
                }

                if (grow > 0)
                {
                    for (var i = start; i < end; i++)
                    {
                        mains[i] = bases[i] + free * (MathF.Max(0, items[i].Style.FlexGrow) / grow);
                    }
                }
                else
                {
                    for (var i = start; i < end; i++)
                    {
                        mains[i] = bases[i];
                    }
                }
            }
            else if (free < 0)
            {
                var shrink = 0f;

                for (var i = start; i < end; i++)
                {
                    shrink += MathF.Max(0, items[i].Style.FlexShrink) * bases[i];
                }

                if (shrink > 0)
                {
                    for (var i = start; i < end; i++)
                    {
                        var factor = MathF.Max(0, items[i].Style.FlexShrink) * bases[i] / shrink;
                        mains[i] = MathF.Max(minMains[i], MathF.Max(0, bases[i] + free * factor));
                    }
                }
                else
                {
                    for (var i = start; i < end; i++)
                    {
                        mains[i] = bases[i];
                    }
                }

                // If clamping to a min-content floor left the line larger than
                // the container, keep shrinking the remaining flexible items.
                for (var pass = 0; pass < 4; pass++)
                {
                    var overflow = 0f;

                    for (var i = start; i < end; i++)
                    {
                        overflow += mains[i] + mainMargins[i];
                    }

                    overflow += mainGap * Math.Max(0, end - start - 1);
                    overflow -= mainAvailable;

                    if (overflow <= 0.01f)
                    {
                        break;
                    }

                    var capacity = 0f;

                    for (var i = start; i < end; i++)
                    {
                        if (items[i].Style.FlexShrink > 0)
                        {
                            capacity += MathF.Max(0, mains[i] - minMains[i]);
                        }
                    }

                    if (capacity <= 0.01f)
                    {
                        break;
                    }

                    var remove = MathF.Min(overflow, capacity);

                    for (var i = start; i < end; i++)
                    {
                        if (items[i].Style.FlexShrink <= 0)
                        {
                            continue;
                        }

                        var room = MathF.Max(0, mains[i] - minMains[i]);
                        mains[i] -= remove * (room / capacity);
                    }
                }
            }
            else
            {
                for (var i = start; i < end; i++)
                {
                    mains[i] = bases[i];
                }
            }
        }

        // Measure each item at its main size to discover the cross size.
        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            var align = EffectiveAlignSelf(item.Style, style);
            var stretch = align == AlignItems.Stretch && IsCrossSizeAuto(item.Style, row);

            if (row)
            {
                PrepareAndMeasureWidth(item, content.Width, mains[i], false, false, false);
                LayoutPreparedBox(
                    item,
                    0,
                    0,
                    content.Width,
                    definiteContentHeight ?? float.NaN,
                    forcedBorderHeight: null,
                    fillHeight: false,
                    definiteContentHeight: null);

                // A stretched item can be taller than the tentative line cross
                // size when a descendant uses a percentage height. Grow the line
                // to the item's max-content height so cards and scroll panels do
                // not clip their own content.
                crossSizes[i] = MathF.Max(
                    item.BorderBox.Height,
                    MeasureMaxContentHeight(item, content.Width));
            }
            else
            {
                var maxContent = MeasureMaxContentWidth(item);
                var available = MathF.Max(0, content.Width - item.Margin.Horizontal);
                var width = stretch
                    ? available
                    : MathF.Min(maxContent, available);

                PrepareAndMeasureWidth(item, content.Width, width, false, false, false);
                LayoutPreparedBox(
                    item,
                    0,
                    0,
                    content.Width,
                    definiteContentHeight ?? content.Height,
                    forcedBorderHeight: mains[i],
                    fillHeight: false,
                    definiteContentHeight: null);

                crossSizes[i] = item.BorderBox.Width;
            }

            var outerCross = crossSizes[i]
                + (row ? item.Margin.Vertical : item.Margin.Horizontal);

            lineCross[lineOf[i]] = MathF.Max(lineCross[lineOf[i]], outerCross);
        }

        // Position lines on the cross axis.
        var crossAvailable = row
            ? definiteContentHeight ?? float.NaN
            : content.Width;

        // A single line fills a definite cross size. With several lines,
        // align-content: stretch distributes the free space equally, which is
        // also what makes align-items: stretch reach the container edge.
        if (float.IsFinite(crossAvailable) && lineCross.Length > 0)
        {
            if (lineCross.Length == 1)
            {
                // A single flex line fills the container's definite cross size.
                // Items that establish their own scroll container then overflow
                // internally instead of stretching the line past the container.
                lineCross[0] = MathF.Max(0, crossAvailable);
            }
            else if (style.AlignContent == AlignContent.Stretch)
            {
                var usedCrossSpace = crossGap * (lineCross.Length - 1);

                for (var line = 0; line < lineCross.Length; line++)
                {
                    usedCrossSpace += lineCross[line];
                }

                var extra = crossAvailable - usedCrossSpace;

                if (extra > 0)
                {
                    var share = extra / lineCross.Length;

                    for (var line = 0; line < lineCross.Length; line++)
                    {
                        lineCross[line] += share;
                    }
                }
            }
        }

        var totalCross = 0f;

        for (var line = 0; line < lineCross.Length; line++)
        {
            totalCross += lineCross[line];
        }

        if (lineCross.Length > 1)
        {
            totalCross += crossGap * (lineCross.Length - 1);
        }

        var crossStart = 0f;
        var crossBetween = 0f;

        if (float.IsFinite(crossAvailable) && crossAvailable > totalCross)
        {
            ComputeJustify(
                style.AlignContent switch
                {
                    AlignContent.FlexEnd or AlignContent.End => JustifyContent.FlexEnd,
                    AlignContent.Center => JustifyContent.Center,
                    AlignContent.SpaceBetween => JustifyContent.SpaceBetween,
                    AlignContent.SpaceAround => JustifyContent.SpaceAround,
                    AlignContent.SpaceEvenly => JustifyContent.SpaceEvenly,
                    _ => JustifyContent.FlexStart
                },
                crossAvailable - totalCross,
                lineCross.Length,
                out crossStart,
                out crossBetween);
        }

        var lineCrossStart = new float[lineCross.Length];
        var cursorCross = crossStart;

        for (var line = 0; line < lineCross.Length; line++)
        {
            lineCrossStart[line] = cursorCross;
            cursorCross += lineCross[line] + crossGap + crossBetween;
        }

        // Final pass: place every item.
        var usedCross = 0f;

        for (var line = 0; line < lineStarts.Count - 1; line++)
        {
            var start = lineStarts[line];
            var end = lineStarts[line + 1];
            var used = 0f;

            for (var i = start; i < end; i++)
            {
                used += mains[i] + mainMargins[i];
            }

            used += mainGap * Math.Max(0, end - start - 1);
            var free = MathF.Max(0, mainAvailable - used);

            // Auto margins absorb free space before justify-content runs. This
            // is what pushes Bootstrap's `.me-auto` nav and trailing form to
            // opposite ends of a navbar.
            if (free > 0)
            {
                var autoSlots = 0;

                for (var i = start; i < end; i++)
                {
                    var margin = items[i].Style.Margin;

                    if (row)
                    {
                        autoSlots += margin.Left.IsAuto ? 1 : 0;
                        autoSlots += margin.Right.IsAuto ? 1 : 0;
                    }
                    else
                    {
                        autoSlots += margin.Top.IsAuto ? 1 : 0;
                        autoSlots += margin.Bottom.IsAuto ? 1 : 0;
                    }
                }

                if (autoSlots > 0)
                {
                    var share = free / autoSlots;

                    for (var i = start; i < end; i++)
                    {
                        var item = items[i];
                        var margin = item.Margin;
                        var styleMargin = item.Style.Margin;

                        if (row)
                        {
                            if (styleMargin.Left.IsAuto)
                            {
                                margin = margin with { Left = share };
                                mainMargins[i] += share;
                            }

                            if (styleMargin.Right.IsAuto)
                            {
                                margin = margin with { Right = share };
                                mainMargins[i] += share;
                            }
                        }
                        else
                        {
                            if (styleMargin.Top.IsAuto)
                            {
                                margin = margin with { Top = share };
                                mainMargins[i] += share;
                            }

                            if (styleMargin.Bottom.IsAuto)
                            {
                                margin = margin with { Bottom = share };
                                mainMargins[i] += share;
                            }
                        }

                        item.Margin = margin;
                    }

                    free = 0;
                }
            }

            ComputeJustify(style.JustifyContent, free, end - start, out var justifyStart, out var justifyBetween);
            var position = justifyStart;

            for (var i = start; i < end; i++)
            {
                var item = items[i];
                var mainSize = mains[i];
                var outerMainSize = mainSize + mainMargins[i];
                var physical = reverse
                    ? mainAvailable - position - outerMainSize
                    : position;

                var align = EffectiveAlignSelf(item.Style, style);
                var crossSpace = lineCross[lineOf[i]]
                    - (crossSizes[i] + (row ? item.Margin.Vertical : item.Margin.Horizontal));
                var crossOffset = AlignOffset(align, crossSpace);

                float borderX;
                float borderY;

                if (row)
                {
                    borderX = content.X + physical + item.Margin.Left;
                    borderY = content.Y + lineCrossStart[lineOf[i]] + crossOffset + item.Margin.Top;

                    PrepareAndMeasureWidth(item, content.Width, mainSize, false, false, false);

                    var stretch = align == AlignItems.Stretch && IsCrossSizeAuto(item.Style, row);
                    var forcedHeight = stretch
                        ? MathF.Max(0, lineCross[lineOf[i]] - item.Margin.Vertical)
                        : (float?)null;

                    LayoutPreparedBox(
                        item,
                        borderX,
                        borderY,
                        content.Width,
                        definiteContentHeight ?? float.NaN,
                        forcedHeight,
                        fillHeight: false,
                        definiteContentHeight: null);
                }
                else
                {
                    borderY = content.Y + physical + item.Margin.Top;
                    borderX = content.X + lineCrossStart[lineOf[i]] + crossOffset + item.Margin.Left;

                    var stretch = align == AlignItems.Stretch && IsCrossSizeAuto(item.Style, row);
                    var forcedWidth = stretch
                        ? MathF.Max(0, lineCross[lineOf[i]] - item.Margin.Horizontal)
                        : crossSizes[i];

                    PrepareAndMeasureWidth(item, content.Width, forcedWidth, false, false, false);

                    LayoutPreparedBox(
                        item,
                        borderX,
                        borderY,
                        content.Width,
                        definiteContentHeight ?? content.Height,
                        forcedBorderHeight: mainSize,
                        fillHeight: false,
                        definiteContentHeight: null);
                }

                position += outerMainSize + mainGap + justifyBetween;
            }

            usedCross += lineCross[line] + crossBetween;

            if (line + 1 < lineStarts.Count - 1)
            {
                usedCross += crossGap;
            }
        }

        if (row)
        {
            return MathF.Max(0, usedCross);
        }

        return MathF.Max(0, mainAvailable);
    }

    private List<LayoutBox> CollectFlowChildren(LayoutBox box)
    {
        var items = new List<LayoutBox>(box.Children.Count);
        Collect(box, items);
        return items;

        // display: contents generates no box, so its children are direct
        // participants in the parent's flex/grid formatting context. Flattening
        // here is what makes <Fragment>/Each(...) work inside a flex container.
        static void Collect(LayoutBox parent, List<LayoutBox> items)
        {
            for (var i = 0; i < parent.Children.Count; i++)
            {
                var child = parent.Children[i];

                if (child.IsOutOfFlow || child.Style.Display == Display.None)
                {
                    continue;
                }

                if (child.IsContents)
                {
                    Collect(child, items);
                    continue;
                }

                items.Add(child);
            }
        }
    }

    private float ResolveFlexBase(
        LayoutBox item,
        bool row,
        float cbWidth,
        float cbHeight,
        in LengthContext context)
    {
        var style = item.Style;
        PrepareBox(item, cbWidth);

        var mainExtra = row
            ? item.Padding.Horizontal + item.Border.Horizontal
            : item.Padding.Vertical + item.Border.Vertical;

        if (!style.FlexBasis.IsAuto)
        {
            var intrinsicBasis = row
                ? MeasureMaxContentWidth(item)
                : MeasureMaxContentHeight(item, cbWidth);

            var basis = style.FlexBasis.IsIntrinsic
                ? intrinsicBasis
                : style.FlexBasis.ResolveOr(in context, row ? cbWidth : cbHeight, 0);

            if (!style.FlexBasis.IsIntrinsic && style.BoxSizing != BoxSizing.BorderBox)
            {
                basis += mainExtra;
            }

            return ClampFlexBaseSize(item, row, cbWidth, cbHeight, in context, basis);
        }

        var mainStyle = row ? style.Width : style.Height;
        var resolved = mainStyle.ResolveOr(in context, row ? cbWidth : cbHeight, float.NaN);

        if (float.IsFinite(resolved))
        {
            var size = style.BoxSizing == BoxSizing.BorderBox ? resolved : resolved + mainExtra;
            return ClampFlexBaseSize(item, row, cbWidth, cbHeight, in context, size);
        }

        var measured = row
            ? MeasureMaxContentWidth(item)
            : MeasureMaxContentHeight(item, cbWidth);

        return ClampFlexBaseSize(item, row, cbWidth, cbHeight, in context, measured);
    }

    private float ClampFlexBaseSize(
        LayoutBox item,
        bool row,
        float cbWidth,
        float cbHeight,
        in LengthContext context,
        float value)
    {
        var style = item.Style;

        if (row)
        {
            return ClampBorderWidth(style, in context, cbWidth, value);
        }

        var extra = item.Padding.Vertical + item.Border.Vertical;
        var min = style.MinHeight.ResolveOr(in context, cbHeight, 0);
        var max = style.MaxHeight.IsAuto
            ? float.PositiveInfinity
            : style.MaxHeight.ResolveOr(in context, cbHeight, float.PositiveInfinity);

        if (style.BoxSizing == BoxSizing.ContentBox)
        {
            min += extra;

            if (float.IsFinite(max))
            {
                max += extra;
            }
        }

        return MathF.Max(0, MathF.Max(min, MathF.Min(value, max)));
    }

    private float MeasureMaxContentHeight(LayoutBox box, float cbWidth)
    {
        var style = box.Style;
        var context = CreateContext(style);
        var padding = style.Padding.Resolve(in context, cbWidth);
        var border = ResolveBorders(style, in context, cbWidth);
        var resolved = style.Height.ResolveOr(in context, float.NaN, float.NaN);

        if (float.IsFinite(resolved))
        {
            return style.BoxSizing == BoxSizing.BorderBox
                ? resolved
                : resolved + padding.Vertical + border.Vertical;
        }

        var content = 0f;
        var text = GetNodeText(box.Node);

        if (!string.IsNullOrEmpty(text) && ShouldLayoutText(style, text))
        {
            var textStyle = CreateTextStyle(style);
            var layout = GetTextLayout(style, text, MathF.Max(1, cbWidth), out _);
            content = layout.Size.Height;
        }

        var row = style.IsFlexContainer
            && style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse;

        // A table row lays its cells out horizontally even though it is not a
        // flex container; summing their heights makes a table measure many
        // times taller than it actually is.
        var horizontal = row || style.Display == Display.TableRow;

        var gap = style.IsFlexContainer && !row
            ? ResolveGap(style.RowGap, in context, cbWidth)
            : 0f;

        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (child.IsOutOfFlow || child.IsContents)
            {
                continue;
            }

            var childContext = CreateContext(child.Style);
            var margin = child.Style.Margin.Resolve(in childContext, cbWidth);
            var childHeight = MeasureMaxContentHeight(child, MathF.Max(1, cbWidth)) + margin.Vertical;

            if (horizontal)
            {
                content = MathF.Max(content, childHeight);
            }
            else
            {
                if (content > 0)
                {
                    content += gap;
                }

                content += childHeight;
            }
        }

        return content + padding.Vertical + border.Vertical;
    }

    private static AlignItems EffectiveAlignSelf(ComputedStyle item, ComputedStyle container)
    {
        return item.AlignSelf switch
        {
            AlignSelf.Auto => container.AlignItems,
            AlignSelf.Stretch => AlignItems.Stretch,
            AlignSelf.FlexStart => AlignItems.FlexStart,
            AlignSelf.FlexEnd => AlignItems.FlexEnd,
            AlignSelf.Center => AlignItems.Center,
            AlignSelf.Baseline => AlignItems.Baseline,
            AlignSelf.Start => AlignItems.Start,
            AlignSelf.End => AlignItems.End,
            AlignSelf.SelfStart => AlignItems.SelfStart,
            AlignSelf.SelfEnd => AlignItems.SelfEnd,
            _ => AlignItems.Stretch
        };
    }

    private static bool IsCrossSizeAuto(ComputedStyle style, bool row)
    {
        return row ? style.Height.IsAuto : style.Width.IsAuto;
    }

    private static float AlignOffset(AlignItems align, float free)
    {
        if (free <= 0)
        {
            return 0;
        }

        return align switch
        {
            AlignItems.FlexEnd or AlignItems.End or AlignItems.SelfEnd => free,
            AlignItems.Center => free * 0.5f,
            _ => 0
        };
    }

    private static void ComputeJustify(
        JustifyContent justify,
        float free,
        int count,
        out float start,
        out float between)
    {
        if (free <= 0 || count <= 0)
        {
            start = 0;
            between = 0;
            return;
        }

        switch (justify)
        {
            case JustifyContent.FlexEnd:
            case JustifyContent.End:
            case JustifyContent.Right:
                start = free;
                between = 0;
                break;

            case JustifyContent.Center:
                start = free * 0.5f;
                between = 0;
                break;

            case JustifyContent.SpaceBetween:
                start = 0;
                between = count > 1 ? free / (count - 1) : 0;
                break;

            case JustifyContent.SpaceAround:
                between = free / count;
                start = between * 0.5f;
                break;

            case JustifyContent.SpaceEvenly:
                between = free / (count + 1);
                start = between;
                break;

            default:
                start = 0;
                between = 0;
                break;
        }
    }

    private static float ResolveGap(CssLength gap, in LengthContext context, float basis)
    {
        return MathF.Max(0, gap.ResolveOr(in context, basis, 0));
    }
}
