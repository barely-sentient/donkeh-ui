using System.Buffers;

namespace Donkeh.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// A flex layout with optional wrapping, grow/shrink, gap, justify and
    /// cross-axis alignment. The implementation follows the CSS order of
    /// operations closely enough that common toolbars, cards and sidebars lay out
    /// as authored without a second pass over the DOM.
    /// </summary>
    /// <param name="box">Flex container whose items are laid out.</param>
    /// <param name="content">Content box of the container in absolute coordinates.</param>
    /// <param name="definiteContentHeight">
    /// Definite content height for column containers, or <see langword="null"/>
    /// when the height is content-based.
    /// </param>
    /// <returns>
    /// The used main size for a column container, or the total cross size of the
    /// flex lines for a row container.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Items are collected through <c>display:contents</c> and sorted by
    /// <c>order</c>. The method resolves base sizes, breaks items into lines when
    /// wrapping is enabled, distributes positive free space by
    /// <c>flex-grow</c> and negative free space by scaled
    /// <c>flex-shrink</c>, then measures the cross size of each line.
    /// </para>
    /// <para>
    /// Automatic minimum sizes use the item's measured max-content size as a
    /// floor for non-scroll items. Shrink clamping can leave a line overflowing,
    /// so up to four extra passes remove the remaining overflow from items that
    /// still have room above their floor. A single line fills a definite cross
    /// size; multiple lines share extra space when
    /// <c>align-content:stretch</c> applies.
    /// </para>
    /// <para>
    /// Placement honours auto margins before <c>justify-content</c>, reverses the
    /// main axis for <c>row-reverse</c>/<c>column-reverse</c>, and stretches items
    /// whose cross size is auto and whose effective alignment is
    /// <c>stretch</c>. Scratch arrays come from <see cref="ArrayPool{T}"/>.
    /// </para>
    /// </remarks>
    private float LayoutFlex(LayoutBox box, Rect content, float? definiteContentHeight)
    {
        var style = box.Style;
        var row = style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse;
        var reverse = style.FlexDirection is FlexDirection.RowReverse or FlexDirection.ColumnReverse;

        var items = CollectFlowChildren(box);

        try
        {
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

        // Flex layout is a hot path: a page with a few dozen flex containers
        // would otherwise allocate six arrays per container on every frame.
        // Rent the scratch storage from the shared pool and return it when the
        // method exits.
        using var scratch = new FlexScratch(count);
        var bases = scratch.Bases;
        var mains = scratch.Mains;
        var minMains = scratch.MinMains;
        var mainMargins = scratch.MainMargins;
        var lineOf = scratch.LineOf;
        var lineStarts = scratch.LineStarts;
        var lineCount = 1;

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
                && i > lineStarts[lineCount - 1]
                && float.IsFinite(mainAvailable))
            {
                var lineStart = lineStarts[lineCount - 1];
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
                    lineStarts[lineCount++] = i;
                }
            }

            lineOf[i] = lineCount - 1;
        }

        lineStarts[lineCount++] = count;

        var lineCrossCount = lineCount - 1;
        var lineCross = scratch.LineCross;
        Array.Clear(lineCross, 0, lineCrossCount);
        var crossSizes = scratch.CrossSizes;

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
        for (var line = 0; line < lineCount - 1; line++)
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
        if (float.IsFinite(crossAvailable) && lineCrossCount > 0)
        {
            if (lineCrossCount == 1)
            {
                // A single flex line fills the container's definite cross size.
                // Items that establish their own scroll container then overflow
                // internally instead of stretching the line past the container.
                lineCross[0] = MathF.Max(0, crossAvailable);
            }
            else if (style.AlignContent == AlignContent.Stretch)
            {
                var usedCrossSpace = crossGap * (lineCrossCount - 1);

                for (var line = 0; line < lineCrossCount; line++)
                {
                    usedCrossSpace += lineCross[line];
                }

                var extra = crossAvailable - usedCrossSpace;

                if (extra > 0)
                {
                    var share = extra / lineCrossCount;

                    for (var line = 0; line < lineCrossCount; line++)
                    {
                        lineCross[line] += share;
                    }
                }
            }
        }

        var totalCross = 0f;

        for (var line = 0; line < lineCrossCount; line++)
        {
            totalCross += lineCross[line];
        }

        if (lineCrossCount > 1)
        {
            totalCross += crossGap * (lineCrossCount - 1);
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
                lineCrossCount,
                out crossStart,
                out crossBetween);
        }

        var lineCrossStart = scratch.LineCrossStart;
        var cursorCross = crossStart;

        for (var line = 0; line < lineCrossCount; line++)
        {
            lineCrossStart[line] = cursorCross;
            cursorCross += lineCross[line] + crossGap + crossBetween;
        }

        // Final pass: place every item.
        var usedCross = 0f;

        for (var line = 0; line < lineCount - 1; line++)
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

                    var stretch = align == AlignItems.Stretch && IsCrossSizeAuto(item.Style, row);
                    var forcedHeight = stretch
                        ? MathF.Max(0, lineCross[lineOf[i]] - item.Margin.Vertical)
                        : (float?)null;

                    // The measure pass already laid this item out at the same
                    // main size. If the cross size did not change, moving the
                    // finished subtree is exact and avoids a second recursive
                    // layout of everything inside it.
                    if (forcedHeight is null
                        || MathF.Abs(forcedHeight.Value - item.BorderBox.Height) < 0.5f)
                    {
                        MoveMeasuredItem(item, borderX, borderY);
                    }
                    else
                    {
                        PrepareAndMeasureWidth(item, content.Width, mainSize, false, false, false);

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
                }
                else
                {
                    borderY = content.Y + physical + item.Margin.Top;
                    borderX = content.X + lineCrossStart[lineOf[i]] + crossOffset + item.Margin.Left;

                    var stretch = align == AlignItems.Stretch && IsCrossSizeAuto(item.Style, row);
                    var forcedWidth = stretch
                        ? MathF.Max(0, lineCross[lineOf[i]] - item.Margin.Horizontal)
                        : crossSizes[i];

                    if (MathF.Abs(forcedWidth - item.BorderBox.Width) < 0.5f)
                    {
                        MoveMeasuredItem(item, borderX, borderY);
                    }
                    else
                    {
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
                }

                position += outerMainSize + mainGap + justifyBetween;
            }

            usedCross += lineCross[line] + crossBetween;

            if (line + 1 < lineCount - 1)
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
        finally
        {
            ReleaseFlowChildren();
        }
    }

    /// <summary>Moves an already-laid-out flex item to its final position.</summary>
    /// <param name="item">Measured item whose geometry is final apart from its origin.</param>
    /// <param name="borderX">Final absolute x coordinate of the border box.</param>
    /// <param name="borderY">Final absolute y coordinate of the border box.</param>
    /// <remarks>
    /// Translating the measured subtree preserves its internal layout. The
    /// border box is then reapplied at the destination so derived boxes reflect
    /// any auto margins changed during placement. This avoids a second recursive
    /// layout for items whose cross size did not change.
    /// </remarks>
    private static void MoveMeasuredItem(LayoutBox item, float borderX, float borderY)
    {
        var dx = borderX - item.BorderBox.X;
        var dy = borderY - item.BorderBox.Y;

        if (dx != 0 || dy != 0)
        {
            item.Translate(dx, dy);
        }

        // Auto margins may have changed during placement, so rebuild the
        // derived boxes at the final position instead of leaving a stale
        // margin box behind.
        item.ApplyBorderBox(borderX, borderY, item.BorderBox.Width, item.BorderBox.Height);
    }

    /// <summary>Collects the boxes that participate in a flex or grid formatting context.</summary>
    /// <param name="box">Container whose children are collected.</param>
    /// <returns>
    /// A pooled list owned by the engine. It is valid until the next collection at
    /// the same nesting depth or until <see cref="ReleaseFlowChildren"/> is called.
    /// </returns>
    /// <remarks>
    /// Out-of-flow and <c>display:none</c> children are skipped.
    /// <c>display:contents</c> boxes contribute their children instead of
    /// themselves, which makes framework fragments direct flex/grid items. The
    /// caller must release the list in a <c>finally</c> block.
    /// </remarks>
    private List<LayoutBox> CollectFlowChildren(LayoutBox box)
    {
        if (_flowItemDepth == _flowItemLists.Count)
        {
            _flowItemLists.Add(new List<LayoutBox>());
        }

        var items = _flowItemLists[_flowItemDepth++];
        items.Clear();
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

    /// <summary>Releases the current nesting level of <see cref="_flowItemLists"/>.</summary>
    /// <remarks>
    /// Must be paired with <see cref="CollectFlowChildren"/> in a <c>finally</c>
    /// block. The list itself is retained and reused; only the depth counter is
    /// decremented.
    /// </remarks>
    private void ReleaseFlowChildren()
    {
        if (_flowItemDepth > 0)
        {
            _flowItemDepth--;
        }
    }

    /// <summary>
    /// Resolves an item's flex base size from <c>flex-basis</c> or its main size.
    /// </summary>
    /// <param name="item">Flex item being sized.</param>
    /// <param name="row">
    /// <see langword="true"/> for a row axis, <see langword="false"/> for a column.
    /// </param>
    /// <param name="cbWidth">Containing-block width for percentages and measurement.</param>
    /// <param name="cbHeight">Containing-block height for percentages.</param>
    /// <param name="context">Length context with the item's font metrics.</param>
    /// <returns>The clamped flex base size in CSS pixels.</returns>
    /// <remarks>
    /// A non-auto <c>flex-basis</c> wins; intrinsic keywords use max-content
    /// measurement and other values resolve against the main-axis containing-block
    /// size. Without a basis the main size (<c>width</c> or <c>height</c>) applies,
    /// and an auto size falls back to max-content. Content-box values gain padding
    /// and border before <see cref="ClampFlexBaseSize"/> applies min/max constraints.
    /// </remarks>
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

    /// <summary>Clamps a flex base size to the item's min and max main size.</summary>
    /// <param name="item">Flex item whose constraints apply.</param>
    /// <param name="row">Whether the main axis is horizontal.</param>
    /// <param name="cbWidth">Containing-block width for the horizontal clamp.</param>
    /// <param name="cbHeight">Containing-block height for the vertical clamp.</param>
    /// <param name="context">Length context with the item's font metrics.</param>
    /// <param name="value">Base size to clamp, in CSS pixels.</param>
    /// <returns>The clamped base size, never negative.</returns>
    /// <remarks>
    /// Row clamping reuses <see cref="ClampBorderWidth"/>; column clamping
    /// converts content-box min and max heights to border-box units.
    /// </remarks>
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

    /// <summary>Computes a box's max-content border-box height for flex and table sizing.</summary>
    /// <param name="box">Box to measure.</param>
    /// <param name="cbWidth">
    /// Containing-block width for text shaping and percentages.
    /// </param>
    /// <returns>Max-content height including padding and border, in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// A definite style height short-circuits measurement. Otherwise the result
    /// starts with the box's own text height and adds child contributions:
    /// a wrapping row sums its flex lines, a non-wrapping row or table row uses
    /// its tallest child, and a block or column container stacks children with
    /// the main-axis gap.
    /// </para>
    /// <para>
    /// Children with a definite flex basis or width occupy that width for line
    /// breaking. Out-of-flow and <c>display:contents</c> children are skipped.
    /// </para>
    /// </remarks>
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
            var layout = GetTextLayout(box.Node, style, text, MathF.Max(1, cbWidth), out _);
            content = layout.Size.Height;
        }

        var row = style.IsFlexContainer
            && style.FlexDirection is FlexDirection.Row or FlexDirection.RowReverse;

        // A wrapping row container is taller than its tallest child: each flex
        // line contributes its own height. Ignoring this makes a navbar measure
        // its collapsed content as a single line and then clip the overflow.
        var wraps = row && style.FlexWrap is FlexWrap.Wrap or FlexWrap.WrapReverse;

        // A table row lays its cells out horizontally even though it is not a
        // flex container; summing their heights makes a table measure many
        // times taller than it actually is.
        var horizontal = row && !wraps || style.Display == Display.TableRow;

        var mainGap = style.IsFlexContainer
            ? ResolveGap(row ? style.ColumnGap : style.RowGap, in context, cbWidth)
            : 0f;
        var crossGap = style.IsFlexContainer
            ? ResolveGap(row ? style.RowGap : style.ColumnGap, in context, cbWidth)
            : 0f;

        if (wraps)
        {
            var available = MathF.Max(1, cbWidth - padding.Horizontal - border.Horizontal);
            var lineWidth = 0f;
            var lineHeight = 0f;
            var wrappedHeight = 0f;
            var hasLine = false;

            for (var i = 0; i < box.Children.Count; i++)
            {
                var child = box.Children[i];

                if (child.IsOutOfFlow || child.IsContents)
                {
                    continue;
                }

                var childContext = CreateContext(child.Style);
                var margin = child.Style.Margin.Resolve(in childContext, cbWidth);
                var childPadding = child.Style.Padding.Resolve(in childContext, available);
                var childBorder = ResolveBorders(child.Style, in childContext, available);
                var childWidth = MeasureMaxContentWidth(child);

                // A definite flex basis (Bootstrap's collapsed navbar uses
                // flex-basis:100%) or width decides how much of the line the
                // item occupies. Ignoring it makes the wrapper measure every
                // child as one line and then clip the wrapped content.
                var basis = child.Style.FlexBasis;
                var basisWidth = !basis.IsAuto && !basis.IsIntrinsic
                    ? basis.ResolveOr(in childContext, available, float.NaN)
                    : float.NaN;
                var specifiedWidth = float.IsFinite(basisWidth) && basisWidth > 0
                    ? basisWidth
                    : child.Style.Width.ResolveOr(in childContext, available, float.NaN);

                if (float.IsFinite(specifiedWidth))
                {
                    childWidth = child.Style.BoxSizing == BoxSizing.BorderBox
                        ? specifiedWidth
                        : specifiedWidth + childPadding.Horizontal + childBorder.Horizontal;
                }

                childWidth += margin.Horizontal;
                var childHeight = MeasureMaxContentHeight(child, MathF.Max(1, cbWidth)) + margin.Vertical;

                if (hasLine && lineWidth + mainGap + childWidth > available)
                {
                    wrappedHeight += lineHeight + crossGap;
                    lineWidth = 0;
                    lineHeight = 0;
                    hasLine = false;
                }

                if (hasLine)
                {
                    lineWidth += mainGap;
                }

                lineWidth += childWidth;
                lineHeight = MathF.Max(lineHeight, childHeight);
                hasLine = true;
            }

            if (hasLine)
            {
                wrappedHeight += lineHeight;
            }

            content = MathF.Max(content, wrappedHeight);
        }
        else
        {
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
                        content += mainGap;
                    }

                    content += childHeight;
                }
            }
        }

        return content + padding.Vertical + border.Vertical;
    }

    /// <summary>Resolves an item's effective cross-axis alignment.</summary>
    /// <param name="item">Flex item's style, whose <c>align-self</c> is inspected.</param>
    /// <param name="container">Container style providing the fallback <c>align-items</c>.</param>
    /// <returns>
    /// The mapped alignment; <c>auto</c> inherits the container's value and an
    /// unknown value falls back to <see cref="AlignItems.Stretch"/>.
    /// </returns>
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

    /// <summary>Returns whether an item's cross-axis size is <c>auto</c>.</summary>
    /// <param name="style">Item style to inspect.</param>
    /// <param name="row">Whether the main axis is horizontal, making height the cross axis.</param>
    /// <returns><see langword="true"/> when the relevant cross size is auto.</returns>
    private static bool IsCrossSizeAuto(ComputedStyle style, bool row)
    {
        return row ? style.Height.IsAuto : style.Width.IsAuto;
    }

    /// <summary>Converts cross-axis alignment into an offset inside free space.</summary>
    /// <param name="align">Effective alignment of the item.</param>
    /// <param name="free">Unused cross-axis space for the item.</param>
    /// <returns>The offset from the line's cross start, in CSS pixels.</returns>
    /// <remarks>
    /// Negative free space is ignored because an overflowing item starts at the
    /// line start. Flex-end maps to the full free space, center to half, and every
    /// other value (including baseline) to zero.
    /// </remarks>
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

    /// <summary>
    /// Resolves <c>justify-content</c> into a start offset and item spacing.
    /// </summary>
    /// <param name="justify">Justification mode.</param>
    /// <param name="free">Unused main-axis space for the line.</param>
    /// <param name="count">Number of items on the line.</param>
    /// <param name="start">Receives the offset before the first item.</param>
    /// <param name="between">Receives the extra spacing inserted between adjacent items.</param>
    /// <remarks>
    /// Non-positive free space yields zero for both outputs. <c>space-between</c>
    /// puts all space between items when there are at least two, <c>space-around</c>
    /// gives each item half a share on both sides, and <c>space-evenly</c> gives
    /// equal space to the leading edge, gaps and trailing edge.
    /// </remarks>
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

    /// <summary>Resolves a row or column gap length to a non-negative pixel value.</summary>
    /// <param name="gap">Gap length from the style.</param>
    /// <param name="context">Length context with the element's font metrics.</param>
    /// <param name="basis">Percentage basis for the gap.</param>
    /// <returns>The resolved gap, or zero for auto or negative values.</returns>
    private static float ResolveGap(CssLength gap, in LengthContext context, float basis)
    {
        return MathF.Max(0, gap.ResolveOr(in context, basis, 0));
    }

    /// <summary>
    /// Pooled scratch arrays for one flex layout. A ref struct keeps the
    /// bookkeeping on the stack; the arrays themselves come from the shared
    /// pool and are returned by <see cref="Dispose"/>.
    /// </summary>
    /// <remarks>
    /// Every array is cleared on construction so an earlier borrower cannot leak
    /// values into this layout. <see cref="LineStarts"/> is one element longer
    /// than the item count to hold the sentinel end index; the other line arrays
    /// are sized for the maximum possible line count.
    /// </remarks>
    private ref struct FlexScratch
    {
        /// <summary>Flex base size of each item, before free-space distribution.</summary>
        public float[] Bases;

        /// <summary>Resolved main size of each item after grow or shrink.</summary>
        public float[] Mains;

        /// <summary>Automatic minimum main size of each item, used as a shrink floor.</summary>
        public float[] MinMains;

        /// <summary>Outer main-axis margin of each item.</summary>
        public float[] MainMargins;

        /// <summary>Cross size measured for each item.</summary>
        public float[] CrossSizes;

        /// <summary>Cross size of each flex line, indexed by line number.</summary>
        public float[] LineCross;

        /// <summary>Cross-axis start offset of each flex line.</summary>
        public float[] LineCrossStart;

        /// <summary>Line index for each item.</summary>
        public int[] LineOf;

        /// <summary>
        /// Start index of each flex line. The entry after the last line holds the
        /// item count as a sentinel.
        /// </summary>
        public int[] LineStarts;

        /// <summary>
        /// Rents and clears the scratch arrays for a container with
        /// <paramref name="count"/> items.
        /// </summary>
        /// <param name="count">Number of flex items in the container.</param>
        public FlexScratch(int count)
        {
            Bases = ArrayPool<float>.Shared.Rent(count);
            Mains = ArrayPool<float>.Shared.Rent(count);
            MinMains = ArrayPool<float>.Shared.Rent(count);
            MainMargins = ArrayPool<float>.Shared.Rent(count);
            CrossSizes = ArrayPool<float>.Shared.Rent(count);
            LineCross = ArrayPool<float>.Shared.Rent(count + 1);
            LineCrossStart = ArrayPool<float>.Shared.Rent(count + 1);
            LineOf = ArrayPool<int>.Shared.Rent(count);
            LineStarts = ArrayPool<int>.Shared.Rent(count + 1);

            Array.Clear(Bases, 0, count);
            Array.Clear(Mains, 0, count);
            Array.Clear(MinMains, 0, count);
            Array.Clear(MainMargins, 0, count);
            Array.Clear(CrossSizes, 0, count);
            Array.Clear(LineCross, 0, count + 1);
            Array.Clear(LineCrossStart, 0, count + 1);
            Array.Clear(LineOf, 0, count);
            LineStarts[0] = 0;
        }

        /// <summary>Returns every rented array to <see cref="ArrayPool{T}.Shared"/>.</summary>
        /// <remarks>
        /// Invoked by the <c>using</c> declaration in <see cref="LayoutFlex"/>,
        /// including when layout exits through an exception.
        /// </remarks>
        public void Dispose()
        {
            ArrayPool<float>.Shared.Return(Bases);
            ArrayPool<float>.Shared.Return(Mains);
            ArrayPool<float>.Shared.Return(MinMains);
            ArrayPool<float>.Shared.Return(MainMargins);
            ArrayPool<float>.Shared.Return(CrossSizes);
            ArrayPool<float>.Shared.Return(LineCross);
            ArrayPool<float>.Shared.Return(LineCrossStart);
            ArrayPool<int>.Shared.Return(LineOf);
            ArrayPool<int>.Shared.Return(LineStarts);
        }
    }
}
