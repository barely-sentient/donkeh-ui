namespace Donkeh.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Grid layout with explicit and auto placement, fixed/percent/auto/fr
    /// tracks, gaps and item alignment. Auto placement is row-major, matching the
    /// default <c>grid-auto-flow</c>.
    /// </summary>
    /// <param name="box">
    /// The grid container; its style supplies the track templates, gaps,
    /// auto-flow mode and default item alignment.
    /// </param>
    /// <param name="content">
    /// Content box in absolute CSS pixels. Its width is the available inline
    /// size for column tracks and its origin anchors every placed item.
    /// </param>
    /// <param name="definiteContentHeight">
    /// Definite content-box height, or <see langword="null"/> when the block size
    /// is indefinite. Percent and <c>fr</c> row tracks need a definite height;
    /// when it is missing they fall back to intrinsic sizing.
    /// </param>
    /// <returns>
    /// The used content height: the sum of the resolved row sizes plus the
    /// inter-row gaps, floored at zero. The width is already known from
    /// <paramref name="content"/>.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The pass runs in this order: expand <c>repeat(auto-fill|auto-fit, ...)</c>
    /// against the available space; place every item; grow the implicit track
    /// lists from the placement extents; resolve column sizes from items'
    /// max-content contributions; measure each item at its resolved cell width to
    /// obtain a natural height; resolve row sizes from those heights; compute
    /// track positions; then position, stretch and lay out each item inside its
    /// cell.
    /// </para>
    /// <para>
    /// Track sizing follows the same order for both axes (see
    /// <see cref="ResolveTrackSizes"/>): fixed and percentage tracks resolve
    /// their sizing function, <c>fit-content()</c> tracks clamp their
    /// contribution to the limit, and intrinsic/auto tracks take their items'
    /// contributions. If a definite axis size leaves positive free space it is
    /// distributed to <c>fr</c> tracks by fraction, or evenly to auto tracks when
    /// there are no flexible tracks. Negative free space is never distributed,
    /// so overflowing items overflow the container rather than shrinking fixed
    /// tracks.
    /// </para>
    /// <para>
    /// Tracks beyond the explicit template are implicit and take their sizing
    /// function from <c>grid-auto-columns</c> / <c>grid-auto-rows</c>, repeating
    /// that pattern from the first implicit track. An unspecified template
    /// therefore makes every track implicit.
    /// </para>
    /// <para>
    /// In column flow the placement algorithm runs transposed, including line
    /// names and named areas, but placements are converted back to row/column
    /// axes before sizing.
    /// </para>
    /// <para>
    /// With no in-flow children the explicit row template is still sized, so a
    /// grid that declares tracks contributes their height (plus gaps). Children
    /// come from a per-depth pooled list that is released in a
    /// <see langword="finally"/> block, so the method allocates only the track
    /// and contribution arrays.
    /// </para>
    /// </remarks>
    private float LayoutGrid(LayoutBox box, Rect content, float? definiteContentHeight)
    {
        var style = box.Style;
        var items = CollectFlowChildren(box);

        try
        {
        StableSortByOrder(items);

        var context = CreateContext(style);
        var columnGap = ResolveGap(style.ColumnGap, in context, content.Width);
        var rowGap = ResolveGap(style.RowGap, in context, content.Height);

        var columnExpansion = ExpandTracks(
            style.GridTemplateColumns,
            content.Width,
            columnGap,
            in context);

        var rowExpansion = ExpandTracks(
            style.GridTemplateRows,
            definiteContentHeight ?? float.NaN,
            rowGap,
            in context);

        if (items.Count == 0)
        {
            // Explicit rows still contribute height without in-flow items.
            var emptyContributions = new float[rowExpansion.Tracks.Length];
            var emptySizes = ResolveTrackSizes(
                rowExpansion.Tracks,
                definiteContentHeight ?? float.NaN,
                emptyContributions,
                emptyContributions,
                rowGap,
                in context);
            var emptyHeight = rowGap * Math.Max(0, emptySizes.Length - 1);

            for (var i = 0; i < emptySizes.Length; i++)
            {
                emptyHeight += emptySizes[i];
            }

            return MathF.Max(0, emptyHeight);
        }

        var columnTracks = columnExpansion.Tracks;
        var rowTracks = rowExpansion.Tracks;
        var columnLineNames = columnExpansion.LineNames;
        var rowLineNames = rowExpansion.LineNames;

        var columnCount = Math.Max(1, columnTracks.Length);
        var columnFlow = (style.GridAutoFlow & GridAutoFlow.Column) != 0;
        var initialColumns = columnFlow ? Math.Max(1, rowTracks.Length) : columnCount;

        // In column flow the placement algorithm runs on transposed axes, so the
        // line names and named areas are transposed too.
        var placementColumnNames = columnFlow ? rowLineNames : columnLineNames;
        var placementRowNames = columnFlow ? columnLineNames : rowLineNames;
        var placementAreas = columnFlow ? style.GridTemplateAreas?.Transpose() : style.GridTemplateAreas;

        var placements = PlaceGridItems(
            items,
            initialColumns,
            columnFlow,
            placementColumnNames,
            placementRowNames,
            placementAreas,
            style.GridAutoFlow,
            out var placedColumnCount,
            out var placedRowCount);

        // Grow the implicit track lists. In column flow the transposed placement
        // counts swap back to the original axes.
        if (columnFlow)
        {
            rowTracks = GrowTracks(rowTracks, placedColumnCount, style.GridAutoRows);
            columnTracks = GrowTracks(columnTracks, placedRowCount, style.GridAutoColumns);
        }
        else
        {
            columnTracks = GrowTracks(columnTracks, placedColumnCount, style.GridAutoColumns);
            rowTracks = GrowTracks(rowTracks, placedRowCount, style.GridAutoRows);
        }

        // auto-fit removes empty copies of the repeated pattern. Doing this
        // before sizing also removes their gutters, because positions are
        // rebuilt from the compacted track list.
        columnTracks = CollapseAutoFitTracks(
            columnTracks, placements, columnAxis: true, columnExpansion);
        rowTracks = CollapseAutoFitTracks(
            rowTracks, placements, columnAxis: false, rowExpansion);

        columnCount = columnTracks.Length;
        var rowCount = rowTracks.Length;

        if (columnCount == 0 || rowCount == 0)
        {
            return 0;
        }

        var columnContributions = new float[columnCount];
        var columnMinContributions = new float[columnCount];
        var rowContributions = new float[rowCount];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var placement = placements[i];
            PrepareBox(item, content.Width);

            var span = Math.Max(1, placement.ColumnSpan);
            var width = MeasureMaxContentWidth(item) + item.Margin.Horizontal;
            var minWidth = MeasureMinContentWidth(item) + item.Margin.Horizontal;
            var share = width / span;
            var minShare = minWidth / span;

            for (var c = placement.Column; c < placement.Column + placement.ColumnSpan && c < columnCount; c++)
            {
                columnContributions[c] = MathF.Max(columnContributions[c], share);
                columnMinContributions[c] = MathF.Max(columnMinContributions[c], minShare);
            }
        }

        var columnSizes = ResolveTrackSizes(
            columnTracks,
            content.Width,
            columnContributions,
            columnMinContributions,
            columnGap,
            in context);

        // Measure natural item heights at their resolved cell widths.
        var naturalWidths = new float[items.Count];
        var naturalHeights = new float[items.Count];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var placement = placements[i];
            var cellWidth = GridSpanSize(columnSizes, placement.Column, placement.ColumnSpan, columnGap);
            var available = MathF.Max(0, cellWidth - item.Margin.Horizontal);
            var maxContent = MeasureMaxContentWidth(item);
            var width = MathF.Min(maxContent, available);

            PrepareAndMeasureWidth(item, content.Width, width, false, false, false, edgesPrepared: true);
            LayoutPreparedBox(
                item,
                0,
                0,
                content.Width,
                content.Height,
                forcedBorderHeight: null,
                fillHeight: false,
                definiteContentHeight: null);

            naturalWidths[i] = item.BorderBox.Width;
            naturalHeights[i] = item.BorderBox.Height;

            var outerHeight = item.BorderBox.Height + item.Margin.Vertical;
            var share = outerHeight / Math.Max(1, placement.RowSpan);

            for (var r = placement.Row; r < placement.Row + placement.RowSpan && r < rowCount; r++)
            {
                rowContributions[r] = MathF.Max(rowContributions[r], share);
            }
        }

        var rowSizes = ResolveTrackSizes(
            rowTracks,
            definiteContentHeight ?? float.NaN,
            rowContributions,
            rowContributions,
            rowGap,
            in context);

        var columnPositions = GridPositions(columnSizes, columnGap);
        var rowPositions = GridPositions(rowSizes, rowGap);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var placement = placements[i];
            var cellX = content.X + columnPositions[placement.Column];
            var cellY = content.Y + rowPositions[placement.Row];
            var cellWidth = GridSpanSize(columnSizes, placement.Column, placement.ColumnSpan, columnGap);
            var cellHeight = GridSpanSize(rowSizes, placement.Row, placement.RowSpan, rowGap);

            var justify = EffectiveJustifySelf(item.Style, style);
            var align = EffectiveAlignSelf(item.Style, style);

            var stretchWidth = justify == JustifySelf.Stretch && item.Style.Width.IsAuto;
            var stretchHeight = align == AlignItems.Stretch && item.Style.Height.IsAuto;

            var itemWidth = stretchWidth
                ? MathF.Max(0, cellWidth - item.Margin.Horizontal)
                : MathF.Min(naturalWidths[i], MathF.Max(0, cellWidth - item.Margin.Horizontal));

            var itemHeight = stretchHeight
                ? MathF.Max(0, cellHeight - item.Margin.Vertical)
                : MathF.Min(naturalHeights[i], MathF.Max(0, cellHeight - item.Margin.Vertical));

            var x = cellX + item.Margin.Left + AlignGridOffset(justify, cellWidth - itemWidth - item.Margin.Horizontal);
            var y = cellY + item.Margin.Top + AlignGridOffset(align, cellHeight - itemHeight - item.Margin.Vertical);

            PrepareAndMeasureWidth(item, content.Width, itemWidth, false, false, false);
            LayoutPreparedBox(
                item,
                x,
                y,
                content.Width,
                content.Height,
                stretchHeight ? itemHeight : (float?)null,
                fillHeight: false,
                definiteContentHeight: null);
        }

        var usedHeight = 0f;

        for (var r = 0; r < rowSizes.Length; r++)
        {
            usedHeight += rowSizes[r];

            if (r > 0)
            {
                usedHeight += rowGap;
            }
        }

        return MathF.Max(0, usedHeight);
        }
        finally
        {
            ReleaseFlowChildren();
        }
    }

    /// <summary>
    /// One item's resolved cell in the canonical row/column axes: zero-based
    /// start indices plus spans of at least one track.
    /// </summary>
    /// <param name="Row">Zero-based row index of the first covered track.</param>
    /// <param name="Column">Zero-based column index of the first covered track.</param>
    /// <param name="RowSpan">Number of rows covered, at least one.</param>
    /// <param name="ColumnSpan">Number of columns covered, at least one.</param>
    /// <remarks>
    /// The placement is always stored in row/column order even when
    /// <c>grid-auto-flow: column</c> transposes the placement algorithm; see
    /// <see cref="ToPlacement"/>. Indices are track indices, not CSS line
    /// numbers, and may address implicit tracks beyond the explicit template.
    /// </remarks>
    private readonly record struct GridItemPlacement(int Row, int Column, int RowSpan, int ColumnSpan);

    /// <summary>
    /// Converts a placement expressed in the algorithm's working axes back to
    /// canonical row/column indices.
    /// </summary>
    /// <param name="columnFlow">Whether the placement pass ran transposed.</param>
    /// <param name="row">Working-axis row (the placement column in column flow).</param>
    /// <param name="column">Working-axis column (the placement row in column flow).</param>
    /// <param name="rowSpan">Span along <paramref name="row"/>.</param>
    /// <param name="columnSpan">Span along <paramref name="column"/>.</param>
    /// <returns>
    /// A canonical placement. For column flow the row/column and their spans are
    /// swapped; otherwise the values pass through unchanged.
    /// </returns>
    /// <remarks>
    /// Column flow swaps <c>grid-row</c> and <c>grid-column</c> before
    /// placement, so undo the swap here before track sizing and positioning.
    /// </remarks>
    private static GridItemPlacement ToPlacement(
        bool columnFlow,
        int row,
        int column,
        int rowSpan,
        int columnSpan)
    {
        return columnFlow
            ? new GridItemPlacement(column, row, columnSpan, rowSpan)
            : new GridItemPlacement(row, column, rowSpan, columnSpan);
    }

    /// <summary>
    /// Places every grid item. Following the spec's order keeps explicit items
    /// from being overlapped by auto-placed ones:
    /// 1. items with definite row and column,
    /// 2. items locked to a row but auto in the column axis,
    /// 3. everything else through a forward-only (sparse) or rescanned (dense)
    ///    auto-placement cursor.
    /// Column flow runs the same algorithm on transposed axes.
    /// </summary>
    /// <param name="items">
    /// In-flow children in paint order; the array indexes match this list.
    /// </param>
    /// <param name="initialColumns">
    /// Column count available before placement, normally the explicit template's
    /// track count. Raised to at least one.
    /// </param>
    /// <param name="columnFlow">
    /// <see langword="true"/> for <c>grid-auto-flow: column</c>; row and column
    /// inputs are swapped for the duration of the algorithm.
    /// </param>
    /// <param name="columnLineNames">Line names for the working column axis.</param>
    /// <param name="rowLineNames">Line names for the working row axis.</param>
    /// <param name="areas">
    /// Named areas, already transposed for column flow, or
    /// <see langword="null"/>.
    /// </param>
    /// <param name="autoFlow">
    /// Auto-flow mode; the <see cref="GridAutoFlow.Dense"/> bit selects dense
    /// packing.
    /// </param>
    /// <param name="columnCount">
    /// Receives the number of columns the placement grid grew to, at least one.
    /// </param>
    /// <param name="rowCount">
    /// Receives the number of rows the placement grid grew to, at least one.
    /// </param>
    /// <returns>
    /// A placement for every entry in <paramref name="items"/>, in the same
    /// order.
    /// </returns>
    /// <remarks>
    /// <para>
    /// Occupancy is tracked in a jagged <c>List&lt;bool[]&gt;</c> that grows on
    /// demand (see <see cref="EnsureGridCapacity"/>); cells outside the tracked
    /// grid are treated as free. Because no item can be placed before the grid
    /// covers it, the pass is effectively O(items x candidate cells) with small
    /// constants and allocates only the grid and the placement arrays.
    /// </para>
    /// <para>
    /// Named lines and areas are resolved once up front, before any item is
    /// placed, so a named area that spans tracks beyond the explicit template
    /// still reserves the right cells. A <c>span</c> whose start is auto is
    /// anchored from its end line, matching the CSS fallback.
    /// </para>
    /// <para>
    /// Sparse packing advances a single cursor and never revisits earlier rows,
    /// which can leave holes. Dense packing restarts the search from row/column
    /// zero for every item and back-fills those holes. In step 2 the sparse
    /// variant keeps one cursor per locked row.
    /// </para>
    /// </remarks>
    private GridItemPlacement[] PlaceGridItems(
        List<LayoutBox> items,
        int initialColumns,
        bool columnFlow,
        string[][] columnLineNames,
        string[][] rowLineNames,
        GridTemplateAreas? areas,
        GridAutoFlow autoFlow,
        out int columnCount,
        out int rowCount)
    {
        columnCount = Math.Max(1, initialColumns);
        rowCount = 1;

        var count = items.Count;
        var dense = (autoFlow & GridAutoFlow.Dense) != 0;
        var placements = new GridItemPlacement[count];
        var placed = new bool[count];
        var columns = new GridPlacement[count];
        var rows = new GridPlacement[count];
        var occupied = new List<bool[]>();

        EnsureGridCapacity(occupied, ref rowCount, ref columnCount, 1, columnCount);

        // Resolve named lines and areas once. Column flow transposes the axes,
        // so grid-row becomes the placement column and vice versa.
        for (var i = 0; i < count; i++)
        {
            var style = items[i].Style;
            var columnPlacement = columnFlow ? style.GridRow : style.GridColumn;
            var rowPlacement = columnFlow ? style.GridColumn : style.GridRow;

            if (style.GridAreaName is { Length: > 0 } areaName
                && areas is not null
                && areas.Areas.TryGetValue(areaName, out var area))
            {
                columnPlacement = new GridPlacement(
                    area.ColumnStart + 1,
                    area.ColumnEnd + 1,
                    false,
                    false,
                    area.ColumnSpan);
                rowPlacement = new GridPlacement(
                    area.RowStart + 1,
                    area.RowEnd + 1,
                    false,
                    false,
                    area.RowSpan);
            }
            else
            {
                columnPlacement = ResolveNamedPlacement(columnPlacement, columnLineNames, areas, true);
                rowPlacement = ResolveNamedPlacement(rowPlacement, rowLineNames, areas, false);
            }

            columns[i] = columnPlacement;
            rows[i] = rowPlacement;
        }

        // Step 1: fully definite placements. A definite end line still fixes
        // the start when the start itself is `span`, so use the resolver's
        // fallback instead of only checking StartAuto.
        for (var i = 0; i < count; i++)
        {
            var columnSpan = PlacementSpan(columns[i], columnCount);
            var rowSpan = PlacementSpan(rows[i], rowCount);
            var column = ResolveGridLine(columns[i], columnSpan, columnCount, columns[i].StartAuto);
            var row = ResolveGridLine(rows[i], rowSpan, rowCount, rows[i].StartAuto);

            if (column < 0 || row < 0)
            {
                continue;
            }

            EnsureGridCapacity(occupied, ref rowCount, ref columnCount, row + rowSpan, column + columnSpan);
            Place(i, row, column, rowSpan, columnSpan);
        }

        // Step 2: items locked to a row. Sparse packing continues past earlier
        // items in the same row; dense packing rescans from the first column.
        var rowCursors = new Dictionary<int, int>();

        for (var i = 0; i < count; i++)
        {
            if (placed[i]
                || ResolveGridLine(
                    columns[i],
                    PlacementSpan(columns[i], columnCount),
                    columnCount,
                    columns[i].StartAuto) >= 0)
            {
                continue;
            }

            var rowSpan = PlacementSpan(rows[i], rowCount);
            var columnSpan = PlacementSpan(columns[i], columnCount);
            var row = ResolveGridLine(rows[i], rowSpan, rowCount, rows[i].StartAuto);

            if (row < 0)
            {
                continue;
            }

            var start = dense
                ? 0
                : rowCursors.TryGetValue(row, out var cursor) ? cursor : 0;
            var column = FindFreeColumn(occupied, row, rowSpan, columnSpan, columnCount, start);
            EnsureGridCapacity(occupied, ref rowCount, ref columnCount, row + rowSpan, column + columnSpan);

            if (!dense)
            {
                rowCursors[row] = column + columnSpan;
            }

            Place(i, row, column, rowSpan, columnSpan);
        }

        // Step 3/4: auto-placement cursor for the remaining items.
        var autoRow = 0;
        var autoColumn = 0;

        for (var i = 0; i < count; i++)
        {
            if (placed[i])
            {
                continue;
            }

            var rowSpan = PlacementSpan(rows[i], rowCount);
            var columnSpan = PlacementSpan(columns[i], columnCount);
            var definiteColumn = ResolveGridLine(
                columns[i],
                columnSpan,
                columnCount,
                columns[i].StartAuto);
            int row;
            int column;

            if (definiteColumn >= 0)
            {
                column = definiteColumn;
                row = FindFreeRow(
                    occupied,
                    column,
                    columnSpan,
                    rowSpan,
                    rowCount,
                    dense ? 0 : Math.Max(0, autoRow));

                EnsureGridCapacity(occupied, ref rowCount, ref columnCount, row + rowSpan, column + columnSpan);

                if (!dense)
                {
                    autoRow = row;
                    autoColumn = column + columnSpan;
                }
            }
            else
            {
                if (dense)
                {
                    FindFreeCell(occupied, rowSpan, columnSpan, columnCount, out row, out column);
                }
                else
                {
                    FindFreeCellFrom(
                        occupied,
                        autoRow,
                        autoColumn,
                        rowSpan,
                        columnSpan,
                        columnCount,
                        out row,
                        out column);
                }

                EnsureGridCapacity(occupied, ref rowCount, ref columnCount, row + rowSpan, column + columnSpan);

                if (!dense)
                {
                    autoRow = row;
                    autoColumn = column + columnSpan;

                    if (autoColumn >= columnCount)
                    {
                        autoRow = row + 1;
                        autoColumn = 0;
                    }
                }
            }

            Place(i, row, column, rowSpan, columnSpan);
        }

        // Preserve a useful implicit column count when the template had no
        // explicit tracks and auto placement created several columns.
        rowCount = occupied.Count;
        return placements;

        void Place(int index, int row, int column, int rowSpan, int columnSpan)
        {
            placements[index] = ToPlacement(columnFlow, row, column, rowSpan, columnSpan);
            MarkOccupied(occupied, row, column, rowSpan, columnSpan);
            placed[index] = true;
        }
    }

    /// <summary>Resolves named lines and named areas into numeric line numbers.</summary>
    /// <param name="placement">
    /// Parsed placement; may contain start/end names or a named span.
    /// </param>
    /// <param name="lineNames">Names for each line of one axis, indexable by line.</param>
    /// <param name="areas">Named areas for the container, or <see langword="null"/>.</param>
    /// <param name="columnAxis">
    /// <see langword="true"/> when <paramref name="placement"/> describes the
    /// column axis, selecting the area's column bounds instead of its row bounds.
    /// </param>
    /// <returns>
    /// A placement with names replaced by 1-based line numbers. Names that cannot
    /// be found are dropped so the remaining auto/definite sides can still place
    /// the item.
    /// </returns>
    /// <remarks>
    /// <para>
    /// An area name takes precedence over a line name on the same side and yields
    /// both bounds at once, so the item spans the whole area. Line names are
    /// matched case-sensitively and the first match at or after the search origin
    /// wins, which lets repeated names (for example the implicit lines of a
    /// repeated track pattern) resolve in order.
    /// </para>
    /// <para>
    /// A span with only a start name keeps its size and leaves the end auto; a
    /// span with only an end name is anchored backwards from that line. When both
    /// names resolve, the line distance determines the span. A span with neither
    /// name stays fully auto.
    /// </para>
    /// </remarks>
    private static GridPlacement ResolveNamedPlacement(
        GridPlacement placement,
        string[][] lineNames,
        GridTemplateAreas? areas,
        bool columnAxis)
    {
        if (!placement.HasNames)
        {
            return placement;
        }

        var startFound = false;
        var endFound = false;
        var startLine = 0;
        var endLine = 0;

        if (placement.StartName is { Length: > 0 } startName)
        {
            if (areas is not null && areas.Areas.TryGetValue(startName, out var startArea))
            {
                var areaStart = columnAxis ? startArea.ColumnStart : startArea.RowStart;
                var areaEnd = columnAxis ? startArea.ColumnEnd : startArea.RowEnd;
                startLine = areaStart + 1;
                startFound = true;

                // The area supplies both bounds only when the end side is auto
                // and no span was requested. An explicit end line (or an end
                // name resolved below) must not be discarded.
                if (placement.EndAuto && placement.End == 0 && placement.SpanCount == 0
                    && placement.EndName is not { Length: > 0 })
                {
                    return new GridPlacement(
                        areaStart + 1,
                        areaEnd + 1,
                        false,
                        false,
                        Math.Max(1, areaEnd - areaStart));
                }
            }
            else
            {
                var index = FindLine(lineNames, startName, 0);

                if (index >= 0)
                {
                    startLine = index + 1;
                    startFound = true;
                }
            }
        }

        if (placement.EndName is { Length: > 0 } endName)
        {
            if (areas is not null && areas.Areas.TryGetValue(endName, out var endArea))
            {
                endLine = (columnAxis ? endArea.ColumnEnd : endArea.RowEnd) + 1;
                endFound = true;
            }
            else
            {
                var index = FindLine(lineNames, endName, startFound ? startLine : 0);

                if (index >= 0)
                {
                    endLine = index + 1;
                    endFound = true;
                }
            }
        }

        if (placement.SpanCount > 0)
        {
            if (startFound)
            {
                return new GridPlacement(startLine, 0, false, true, placement.SpanCount, null, null, false);
            }

            if (endFound)
            {
                return new GridPlacement(
                    endLine - placement.SpanCount,
                    endLine,
                    false,
                    false,
                    placement.SpanCount,
                    null,
                    null,
                    true);
            }

            return new GridPlacement(
                0,
                0,
                true,
                true,
                placement.SpanCount,
                null,
                null,
                placement.SpanOnEnd);
        }

        if (startFound && endFound)
        {
            return new GridPlacement(
                startLine,
                endLine,
                false,
                false,
                Math.Max(1, endLine - startLine));
        }

        if (startFound)
        {
            return new GridPlacement(startLine, placement.End, false, placement.End == 0);
        }

        if (endFound)
        {
            return new GridPlacement(placement.Start, endLine, placement.Start == 0, false);
        }

        return placement with { StartName = null, EndName = null };
    }

    /// <summary>Finds the first line at or after a start index carrying a name.</summary>
    /// <param name="lineNames">
    /// Names for each line; index <c>i</c> is the line before track <c>i</c>.
    /// </param>
    /// <param name="name">
    /// Line name to match, compared with <see cref="StringComparison.Ordinal"/>.
    /// </param>
    /// <param name="from">
    /// Zero-based line index to start from; negative values are clamped to zero.
    /// </param>
    /// <returns>The zero-based line index, or -1 when the name is absent.</returns>
    /// <remarks>
    /// A line may have several names, so every name slot is checked. The ordinal
    /// comparison makes line names case-sensitive, as CSS custom identifiers are.
    /// </remarks>
    private static int FindLine(string[][] lineNames, string name, int from)
    {
        for (var i = Math.Max(0, from); i < lineNames.Length; i++)
        {
            var names = lineNames[i];

            for (var n = 0; n < names.Length; n++)
            {
                if (string.Equals(names[n], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>Resolves one axis of a placement to a zero-based start track index.</summary>
    /// <param name="placement">Placement for one axis, after named resolution.</param>
    /// <param name="span">Number of tracks the item covers, at least one.</param>
    /// <param name="trackCount">Current track count, used to resolve negative lines.</param>
    /// <param name="startAuto">
    /// Whether the start side is auto. Passed separately because a named-area
    /// resolution can replace the placement's own flag.
    /// </param>
    /// <returns>
    /// A zero-based start index, or -1 when the item has no definite position on
    /// this axis and must be auto-placed.
    /// </returns>
    /// <remarks>
    /// A definite end line plus a span anchors the item backwards:
    /// <c>max(0, end - span)</c>. The clamp prevents a negative index when the
    /// span is larger than the number of lines before the end.
    /// </remarks>
    private static int ResolveGridLine(GridPlacement placement, int span, int trackCount, bool startAuto)
    {
        if (startAuto || placement.Start == 0)
        {
            if (!placement.EndAuto && placement.End != 0)
            {
                var end = NormalizeGridLine(placement.End, trackCount);
                return Math.Max(0, end - span);
            }

            return -1;
        }

        return NormalizeGridLine(placement.Start, trackCount);
    }

    /// <summary>Resolves how many tracks a placement covers, including negative end lines.</summary>
    /// <param name="placement">Placement for one axis.</param>
    /// <param name="trackCount">Current track count, used to normalise negative lines.</param>
    /// <returns>The span in tracks, at least one.</returns>
    /// <remarks>
    /// Raw line numbers cannot be subtracted before normalisation: <c>1 / -1</c>
    /// would otherwise produce a negative distance and collapse to one track.
    /// </remarks>
    private static int PlacementSpan(GridPlacement placement, int trackCount)
    {
        if (placement.SpanCount > 0 && (placement.StartAuto || placement.EndAuto))
        {
            return Math.Max(1, placement.SpanCount);
        }

        if (placement.StartAuto || placement.EndAuto)
        {
            return 1;
        }

        var start = NormalizeGridLine(placement.Start, trackCount);
        var end = NormalizeGridLine(placement.End, trackCount);
        return Math.Max(1, end - start);
    }

    /// <summary>Converts a CSS grid line number to a zero-based track index.</summary>
    /// <param name="line">
    /// 1-based line number; negative counts backwards from the end; 0 means auto.
    /// </param>
    /// <param name="trackCount">Number of explicit tracks at call time.</param>
    /// <returns>
    /// The zero-based track index, or -1 for line 0. Negative indices produced by
    /// an out-of-range negative line are clamped to zero.
    /// </returns>
    /// <remarks>
    /// Line 1 maps to track 0 and line <c>trackCount + 1</c> maps to the implicit
    /// track one past the end. Negative lines count backwards from the end line:
    /// -1 is the line after the last explicit track (index <c>trackCount</c>),
    /// -2 is the last track's start line, and so on.
    /// </remarks>
    private static int NormalizeGridLine(int line, int trackCount)
    {
        if (line == 0)
        {
            return -1;
        }

        return line > 0 ? line - 1 : Math.Max(0, trackCount + line + 1);
    }

    /// <summary>Grows the occupancy grid so it covers at least the requested extents.</summary>
    /// <param name="occupied">Jagged occupancy grid, rows of column flags; grown in place.</param>
    /// <param name="rowCount">Current row count, raised to cover <paramref name="rows"/>.</param>
    /// <param name="columnCount">
    /// Current column count, raised to cover <paramref name="columns"/>.
    /// </param>
    /// <param name="rows">Minimum number of rows that must be addressable.</param>
    /// <param name="columns">Minimum number of columns that must be addressable.</param>
    /// <remarks>
    /// Adds row arrays and widens existing rows with <c>Array.Resize</c>. Both
    /// counts are at least one. Growth is not geometric: every call widens only
    /// to the requested column count, so repeated single-column growth can copy
    /// each row repeatedly.
    /// </remarks>
    private static void EnsureGridCapacity(
        List<bool[]> occupied,
        ref int rowCount,
        ref int columnCount,
        int rows,
        int columns)
    {
        rowCount = Math.Max(rowCount, Math.Max(1, rows));
        columnCount = Math.Max(columnCount, Math.Max(1, columns));

        while (occupied.Count < rowCount)
        {
            occupied.Add(new bool[columnCount]);
        }

        for (var r = 0; r < occupied.Count; r++)
        {
            if (occupied[r].Length < columnCount)
            {
                var cells = occupied[r];
                Array.Resize(ref cells, columnCount);
                occupied[r] = cells;
            }
        }
    }

    /// <summary>Tests whether a rectangular span of cells is unoccupied.</summary>
    /// <param name="occupied">Occupancy grid to inspect.</param>
    /// <param name="row">Zero-based first row of the span.</param>
    /// <param name="column">Zero-based first column of the span.</param>
    /// <param name="rowSpan">Number of rows in the span.</param>
    /// <param name="columnSpan">Number of columns in the span.</param>
    /// <returns><see langword="true"/> when none of the covered cells is occupied.</returns>
    /// <remarks>
    /// Cells outside the tracked grid count as free, and the scan returns
    /// <see langword="true"/> immediately on reaching them. The caller is
    /// expected to grow the grid afterwards, so placement can always extend into
    /// new implicit tracks.
    /// </remarks>
    private static bool GridAreaFree(
        List<bool[]> occupied,
        int row,
        int column,
        int rowSpan,
        int columnSpan)
    {
        for (var r = row; r < row + rowSpan; r++)
        {
            if (r >= occupied.Count)
            {
                return true;
            }

            var cells = occupied[r];

            for (var c = column; c < column + columnSpan; c++)
            {
                if (c >= cells.Length)
                {
                    return true;
                }

                if (cells[c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Marks every cell of a rectangular span as occupied.</summary>
    /// <param name="occupied">Occupancy grid, extended in place as needed.</param>
    /// <param name="row">Zero-based first row of the span.</param>
    /// <param name="column">Zero-based first column of the span.</param>
    /// <param name="rowSpan">Number of rows to mark.</param>
    /// <param name="columnSpan">Number of columns to mark.</param>
    /// <remarks>
    /// Adds missing rows and widens rows that are too narrow, then sets the
    /// covered flags. Unlike <see cref="EnsureGridCapacity"/>, this can grow
    /// beyond the declared row/column counts, which makes
    /// <c>occupied.Count</c> the true post-placement row count.
    /// </remarks>
    private static void MarkOccupied(
        List<bool[]> occupied,
        int row,
        int column,
        int rowSpan,
        int columnSpan)
    {
        for (var r = row; r < row + rowSpan; r++)
        {
            while (occupied.Count <= r)
            {
                occupied.Add(new bool[Math.Max(1, column + columnSpan)]);
            }

            var cells = occupied[r];

            if (cells.Length < column + columnSpan)
            {
                Array.Resize(ref cells, column + columnSpan);
                occupied[r] = cells;
            }

            for (var c = column; c < column + columnSpan; c++)
            {
                cells[c] = true;
            }
        }
    }

    /// <summary>Finds the first free column in one row for a row-locked item.</summary>
    /// <param name="occupied">Occupancy grid to search.</param>
    /// <param name="row">Zero-based row to scan.</param>
    /// <param name="rowSpan">Rows the item covers, all of which must be free.</param>
    /// <param name="columnSpan">Columns the item covers.</param>
    /// <param name="columnCount">
    /// Current column count; the scan may return this value to overflow.
    /// </param>
    /// <param name="start">First column to try; negative values are clamped to zero.</param>
    /// <returns>
    /// The first free starting column, or <paramref name="columnCount"/> when no
    /// position fits, signalling that an implicit column must be added.
    /// </returns>
    /// <remarks>
    /// The upper bound is inclusive so the overflow column itself is reported
    /// rather than silently returning the last column.
    /// </remarks>
    private static int FindFreeColumn(
        List<bool[]> occupied,
        int row,
        int rowSpan,
        int columnSpan,
        int columnCount,
        int start)
    {
        for (var c = Math.Max(0, start); c <= columnCount; c++)
        {
            if (GridAreaFree(occupied, row, c, rowSpan, columnSpan))
            {
                return c;
            }
        }

        return columnCount;
    }

    /// <summary>Finds the first free row for an item with a definite column.</summary>
    /// <param name="occupied">Occupancy grid to search.</param>
    /// <param name="column">Zero-based first column of the item.</param>
    /// <param name="columnSpan">Columns the item covers, all of which must be free.</param>
    /// <param name="rowSpan">Rows the item covers.</param>
    /// <param name="rowCount">
    /// Current row count; the scan may return this value to overflow.
    /// </param>
    /// <param name="start">First row to try; negative values are clamped to zero.</param>
    /// <returns>
    /// The first free starting row, or <paramref name="rowCount"/> when no
    /// position fits, signalling that an implicit row must be added.
    /// </returns>
    /// <remarks>
    /// Sparse auto-placement passes the current cursor row so earlier rows are
    /// never revisited; dense placement passes zero.
    /// </remarks>
    private static int FindFreeRow(
        List<bool[]> occupied,
        int column,
        int columnSpan,
        int rowSpan,
        int rowCount,
        int start)
    {
        for (var r = Math.Max(0, start); r <= rowCount; r++)
        {
            if (GridAreaFree(occupied, r, column, rowSpan, columnSpan))
            {
                return r;
            }
        }

        return rowCount;
    }

    /// <summary>Searches the whole grid from the origin for the first free cell.</summary>
    /// <param name="occupied">Occupancy grid to search.</param>
    /// <param name="rowSpan">Rows the item covers.</param>
    /// <param name="columnSpan">Columns the item covers.</param>
    /// <param name="columnCount">Current column count.</param>
    /// <param name="row">Receives the zero-based row of the first fit.</param>
    /// <param name="column">Receives the zero-based column of the first fit.</param>
    /// <returns>
    /// <see langword="true"/> when a position fit within the current width; see
    /// <see cref="FindFreeCellFrom"/> for the failure semantics.
    /// </returns>
    /// <remarks>
    /// This is the dense auto-placement entry point: it always restarts at
    /// row 0, column 0 so earlier holes are back-filled.
    /// </remarks>
    private static bool FindFreeCell(
        List<bool[]> occupied,
        int rowSpan,
        int columnSpan,
        int columnCount,
        out int row,
        out int column)
    {
        return FindFreeCellFrom(occupied, 0, 0, rowSpan, columnSpan, columnCount, out row, out column);
    }

    /// <summary>Searches row-major from a cursor for the first free cell.</summary>
    /// <param name="occupied">Occupancy grid to search.</param>
    /// <param name="startRow">Row of the cursor.</param>
    /// <param name="startColumn">Column of the cursor in <paramref name="startRow"/>.</param>
    /// <param name="rowSpan">Rows the item covers.</param>
    /// <param name="columnSpan">Columns the item covers.</param>
    /// <param name="columnCount">
    /// Current column count; the item must fit entirely within it.
    /// </param>
    /// <param name="row">
    /// Receives the zero-based row of the fit, or <c>occupied.Count</c> on
    /// failure.
    /// </param>
    /// <param name="column">
    /// Receives the zero-based column of the fit, or zero on failure.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a fit was found. <see langword="false"/> means
    /// the item is wider than the current grid or no cell remained; the caller
    /// then grows the implicit columns and places at the reported overflow row.
    /// </returns>
    /// <remarks>
    /// A cursor column at or past <paramref name="columnCount"/> advances to the
    /// next row. The scan stops at the first row after the tracked grid, where
    /// cells count as free, so a non-spanning item always advances rather than
    /// failing. Negative cursor components are clamped to zero.
    /// </remarks>
    private static bool FindFreeCellFrom(
        List<bool[]> occupied,
        int startRow,
        int startColumn,
        int rowSpan,
        int columnSpan,
        int columnCount,
        out int row,
        out int column)
    {
        startRow = Math.Max(0, startRow);
        startColumn = Math.Max(0, startColumn);

        if (startColumn >= columnCount)
        {
            startRow++;
            startColumn = 0;
        }

        for (var r = startRow; r <= occupied.Count; r++)
        {
            var first = r == startRow ? startColumn : 0;

            for (var c = first; c + columnSpan <= columnCount; c++)
            {
                if (GridAreaFree(occupied, r, c, rowSpan, columnSpan))
                {
                    row = r;
                    column = c;
                    return true;
                }
            }
        }

        row = occupied.Count;
        column = 0;
        return false;
    }

    /// <summary>
    /// The result of expanding a track template: concrete tracks plus an
    /// optional name for every line, including both outer edges.
    /// </summary>
    /// <param name="Tracks">Expanded tracks in order, with auto-repeat already replicated.</param>
    /// <param name="LineNames">
    /// Names per line. Index <c>i</c> names the line before track <c>i</c>; there
    /// are <c>Tracks.Length + 1</c> entries, the last naming the end line.
    /// </param>
    /// <param name="AutoRepeatStart">First track index of the expanded auto-repeat, or -1.</param>
    /// <param name="AutoRepeatPatternLength">Tracks in one copy of the repeated pattern.</param>
    /// <param name="AutoRepeatCount">Number of expanded copies.</param>
    /// <param name="AutoRepeatFit">Whether the repeat used <c>auto-fit</c>.</param>
    /// <remarks>
    /// An empty result means the template was unspecified, so all tracks are
    /// implicit. The arrays are newly allocated by <see cref="ExpandTracks"/> and
    /// are not shared with the parsed <see cref="GridTrackList"/>.
    /// </remarks>
    private readonly record struct ExpandedTracks(
        GridTrack[] Tracks,
        string[][] LineNames,
        int AutoRepeatStart,
        int AutoRepeatPatternLength,
        int AutoRepeatCount,
        bool AutoRepeatFit);

    /// <summary>
    /// Expands <c>repeat(auto-fill|auto-fit, ...)</c> to the number of copies
    /// that fit in the available space. A fixed or <c>minmax()</c> minimum gives
    /// the repeat size; patterns without one fall back to a single copy.
    /// </summary>
    /// <param name="list">Parsed template, or <see langword="null"/> for none.</param>
    /// <param name="available">
    /// Size of the axis in CSS pixels. May be <see cref="float.NaN"/> for an
    /// indefinite axis, which disables repetition.
    /// </param>
    /// <param name="gap">Gap between tracks in CSS pixels, included in the repeat size.</param>
    /// <param name="context">Length context used to resolve fixed, percent and fr minimums.</param>
    /// <returns>
    /// Concrete tracks and line names, or empty arrays when the template is
    /// empty/unspecified.
    /// </returns>
    /// <remarks>
    /// <para>
    /// The repeated pattern is inserted in place of the parser's placeholder
    /// track. Line names around the placeholder are preserved, and each copy
    /// contributes its own internal names, so named lines inside a repeat resolve
    /// to every copy.
    /// </para>
    /// <para>
    /// <c>auto-fit</c> first clamps the repeat count to the number of items.
    /// After placement, <see cref="CollapseAutoFitTracks"/> removes copies that
    /// received no items and compacts the surviving tracks, which also drops
    /// their gutters so free space reaches the remaining tracks.
    /// </para>
    /// </remarks>
    private static ExpandedTracks ExpandTracks(
        GridTrackList? list,
        float available,
        float gap,
        in LengthContext context)
    {
        if (list is null || list.Count == 0)
        {
            // An unspecified template means no explicit tracks. Every track is
            // implicit and picks up grid-auto-rows / grid-auto-columns from the
            // very first one.
            return new ExpandedTracks([], [], -1, 0, 0, false);
        }

        if (list.AutoRepeatIndex < 0 || list.AutoRepeatTracks.Length == 0)
        {
            return new ExpandedTracks(list.Tracks, list.LineNames, -1, 0, 0, false);
        }

        var pattern = list.AutoRepeatTracks;
        var patternNames = list.AutoRepeatLineNames;
        var repeats = CountAutoRepeats(pattern, available, gap, in context);

        // auto-fit uses the same expansion as auto-fill; empty copies are
        // removed after placement by CollapseAutoFitTracks.
        var tracks = new List<GridTrack>(list.Count - 1 + pattern.Length * repeats);
        var names = new List<List<string>>(tracks.Capacity + 1)
        {
            new(list.LineNames.Length > 0 ? list.LineNames[0] : [])
        };

        for (var i = 0; i < list.Count; i++)
        {
            if (i == list.AutoRepeatIndex)
            {
                for (var repeat = 0; repeat < repeats; repeat++)
                {
                    for (var p = 0; p < pattern.Length; p++)
                    {
                        if (p == 0 && patternNames.Length > 0)
                        {
                            names[^1].AddRange(patternNames[0]);
                        }

                        tracks.Add(pattern[p]);
                        names.Add(new List<string>(
                            p + 1 < patternNames.Length ? patternNames[p + 1] : []));
                    }
                }
            }
            else
            {
                tracks.Add(list.Tracks[i]);
                names.Add(new List<string>(
                    i + 1 < list.LineNames.Length ? list.LineNames[i + 1] : []));
            }
        }

        return new ExpandedTracks(
            tracks.ToArray(),
            ToNameArrays(names),
            list.AutoRepeatIndex,
            pattern.Length,
            repeats,
            list.AutoRepeatFit);
    }

    /// <summary>Collapses empty copies of an auto-fit repeat pattern.</summary>
    /// <param name="tracks">Expanded tracks on one axis.</param>
    /// <param name="placements">Item placements, remapped in place.</param>
    /// <param name="columnAxis">
    /// <see langword="true"/> to operate on columns, <see langword="false"/> for rows.
    /// </param>
    /// <param name="expansion">Metadata recorded by <see cref="ExpandTracks"/>.</param>
    /// <returns>
    /// The original array when nothing collapsed, otherwise a compacted array.
    /// </returns>
    /// <remarks>
    /// A copy is empty when no item occupies any of its tracks. Removing it also
    /// removes the gaps around it, so free space reaches the remaining tracks
    /// naturally during sizing.
    /// </remarks>
    private static GridTrack[] CollapseAutoFitTracks(
        GridTrack[] tracks,
        GridItemPlacement[] placements,
        bool columnAxis,
        in ExpandedTracks expansion)
    {
        if (!expansion.AutoRepeatFit
            || expansion.AutoRepeatCount <= 0
            || expansion.AutoRepeatPatternLength <= 0
            || expansion.AutoRepeatStart < 0)
        {
            return tracks;
        }

        var occupied = new bool[tracks.Length];

        for (var i = 0; i < placements.Length; i++)
        {
            var placement = placements[i];
            var start = columnAxis ? placement.Column : placement.Row;
            var span = Math.Max(1, columnAxis ? placement.ColumnSpan : placement.RowSpan);

            for (var t = start; t < start + span && t < occupied.Length; t++)
            {
                occupied[t] = true;
            }
        }

        var keep = new bool[tracks.Length];
        Array.Fill(keep, true);
        var collapsed = false;

        for (var copy = 0; copy < expansion.AutoRepeatCount; copy++)
        {
            var copyStart = expansion.AutoRepeatStart + copy * expansion.AutoRepeatPatternLength;
            var hasItem = false;

            for (var t = copyStart; t < copyStart + expansion.AutoRepeatPatternLength && t < tracks.Length; t++)
            {
                hasItem |= occupied[t];
            }

            if (hasItem)
            {
                continue;
            }

            collapsed = true;

            for (var t = copyStart; t < copyStart + expansion.AutoRepeatPatternLength && t < tracks.Length; t++)
            {
                keep[t] = false;
            }
        }

        if (!collapsed)
        {
            return tracks;
        }

        var map = new int[tracks.Length + 1];
        var compacted = new List<GridTrack>(tracks.Length);

        for (var i = 0; i < tracks.Length; i++)
        {
            map[i] = compacted.Count;

            if (keep[i])
            {
                compacted.Add(tracks[i]);
            }
        }

        map[tracks.Length] = compacted.Count;

        for (var i = 0; i < placements.Length; i++)
        {
            var placement = placements[i];
            var start = columnAxis ? placement.Column : placement.Row;
            var span = Math.Max(1, columnAxis ? placement.ColumnSpan : placement.RowSpan);
            var end = Math.Min(start + span, tracks.Length);
            var newStart = map[Math.Min(start, tracks.Length)];
            var newEnd = map[Math.Max(newStart, end)];
            var newSpan = Math.Max(1, newEnd - newStart);

            placements[i] = columnAxis
                ? placement with { Column = newStart, ColumnSpan = newSpan }
                : placement with { Row = newStart, RowSpan = newSpan };
        }

        return compacted.ToArray();
    }

    /// <summary>Copies per-line name lists into a jagged array.</summary>
    /// <param name="names">One list per grid line, including the trailing end line.</param>
    /// <returns>A new outer array whose entries are newly allocated name arrays.</returns>
    /// <remarks>
    /// The copy breaks the link to the temporary <see cref="List{T}"/> used while
    /// expanding repeats, so the returned line names can outlive the expansion.
    /// </remarks>
    private static string[][] ToNameArrays(List<List<string>> names)
    {
        var result = new string[names.Count][];

        for (var i = 0; i < names.Count; i++)
        {
            result[i] = names[i].ToArray();
        }

        return result;
    }

    /// <summary>Counts how many copies of an auto-repeat pattern fit on one axis.</summary>
    /// <param name="pattern">Tracks inside <c>repeat()</c>; must be non-empty.</param>
    /// <param name="available">
    /// Axis size in CSS pixels; non-finite or non-positive values yield one
    /// copy.
    /// </param>
    /// <param name="gap">Gap between tracks in CSS pixels.</param>
    /// <param name="context">Length context for resolving the pattern's minimums.</param>
    /// <returns>The number of copies, clamped to the range 1 to 1024.</returns>
    /// <remarks>
    /// <para>
    /// The repeat size is the sum of each track's minimum: a fixed length, a
    /// percentage, or a <c>minmax()</c> with a definite first argument. Internal
    /// gaps are added once per pattern. The count is
    /// <c>floor((available + gap) / (repeatSize + gap))</c>, which is the standard
    /// auto-repeat formula and avoids a trailing gap being counted twice.
    /// </para>
    /// <para>
    /// A pattern containing <c>auto</c>, an intrinsic minimum, or an <c>fr</c>
    /// track with an intrinsic minimum falls back to a single copy, because the
    /// repeat size then depends on content that is not known yet. A
    /// <c>fit-content()</c> track uses its stored limit (the maximum side), not
    /// its auto minimum. The 1024 cap matches the parser's accepted repeat range
    /// and bounds the expanded track array.
    /// </para>
    /// </remarks>
    private static int CountAutoRepeats(
        GridTrack[] pattern,
        float available,
        float gap,
        in LengthContext context)
    {
        if (!float.IsFinite(available) || available <= 0 || pattern.Length == 0)
        {
            return 1;
        }

        var repeatSize = 0f;

        for (var i = 0; i < pattern.Length; i++)
        {
            var track = pattern[i];
            float size;

            switch (track.Kind)
            {
                case GridTrackKind.Fixed:
                case GridTrackKind.Percent:
                    size = track.Minimum.ResolveOr(in context, available, 0);
                    break;

                case GridTrackKind.FitContent:
                    // fit-content() stores its limit as the maximum; its
                    // minimum is auto and would resolve to the fallback zero.
                    size = track.Maximum.ResolveOr(in context, available, 0);
                    break;

                case GridTrackKind.Fr when !track.Minimum.IsAuto && !track.Minimum.IsIntrinsic:
                    size = track.Minimum.ResolveOr(in context, available, 0);
                    break;

                default:
                    return 1;
            }

            if (!float.IsFinite(size) || size < 0)
            {
                return 1;
            }

            repeatSize += size;
        }

        repeatSize += gap * Math.Max(0, pattern.Length - 1);

        if (repeatSize <= 0)
        {
            return 1;
        }

        var count = (int)MathF.Floor((available + gap) / (repeatSize + gap));
        return Math.Clamp(count, 1, 1024);
    }

    /// <summary>Extends a track array to cover implicit tracks.</summary>
    /// <param name="tracks">Explicit tracks; returned unchanged when already long enough.</param>
    /// <param name="count">Required track count, normally the placement extent.</param>
    /// <param name="implicitTracks">
    /// Parsed <c>grid-auto-rows</c> or <c>grid-auto-columns</c> pattern, or
    /// <see langword="null"/> for the default.
    /// </param>
    /// <returns>
    /// An array of exactly <paramref name="count"/> tracks. When no growth was
    /// needed this is the input array itself, not a copy.
    /// </returns>
    /// <remarks>
    /// Implicit tracks repeat the auto pattern starting with the first implicit
    /// track, so the pattern index is <c>i - explicitCount</c> modulo the pattern
    /// length. With no pattern, each implicit track is
    /// <see cref="GridTrack.Auto"/>.
    /// </remarks>
    private static GridTrack[] GrowTracks(
        GridTrack[] tracks,
        int count,
        GridTrackList? implicitTracks)
    {
        if (tracks.Length >= count)
        {
            return tracks;
        }

        var grown = new GridTrack[count];
        Array.Copy(tracks, grown, tracks.Length);

        // grid-auto-rows / grid-auto-columns is a pattern that repeats starting
        // with the first implicit track.
        var pattern = implicitTracks is { Count: > 0 } ? implicitTracks.Tracks : null;

        for (var i = tracks.Length; i < count; i++)
        {
            grown[i] = pattern is null
                ? GridTrack.Auto
                : pattern[(i - tracks.Length) % pattern.Length];
        }

        return grown;
    }

    /// <summary>
    /// Resolves final track sizes from tracks, items' contributions and free
    /// space.
    /// </summary>
    /// <param name="tracks">Tracks to size, in order.</param>
    /// <param name="available">
    /// Size of the axis in CSS pixels, or <see cref="float.NaN"/> when the axis is
    /// indefinite (for example an auto-height grid's rows).
    /// </param>
    /// <param name="contributions">
    /// Per-track max-content contribution in CSS pixels: the largest max-content
    /// outer size of any item, divided by the item's span so spanning items do not
    /// inflate every covered track.
    /// </param>
    /// <param name="minContributions">
    /// Per-track min-content contribution, measured and divided the same way.
    /// </param>
    /// <param name="gap">Gap between tracks in CSS pixels.</param>
    /// <param name="context">Length context used to resolve fixed and percentage minimums.</param>
    /// <returns>One non-negative size in CSS pixels per input track.</returns>
    /// <remarks>
    /// <para>
    /// Sizing order. Fixed and percentage tracks resolve their minimum against
    /// <paramref name="available"/> and grow toward the max-content contribution
    /// up to the <see cref="GridTrack.Maximum"/> limit; <c>fit-content()</c>
    /// tracks clamp the max-content contribution between min-content and the
    /// limit. <c>min-content</c> tracks use the min-content contribution,
    /// <c>max-content</c> tracks use the max-content contribution, and auto
    /// tracks take at least their minimum then their max-content contribution.
    /// Flexible tracks start at the larger of their explicit minimum and their
    /// min-content contribution, then share free space.
    /// </para>
    /// <para>
    /// On a definite axis, positive free space (<c>available - total</c>) goes to
    /// <c>fr</c> tracks in proportion to their fractions. If no track has a
    /// positive fraction it is split evenly across the collected auto tracks
    /// (which can include flexible tracks with a zero fraction) instead; this
    /// implements <c>align-content/justify-content: stretch</c> for auto tracks.
    /// Negative free space is ignored: no track is shrunk, so overflowing
    /// content overflows the container.
    /// </para>
    /// <para>
    /// On an indefinite axis there is no free space to distribute. <c>fr</c>
    /// tracks fall back to the larger of their minimum and min-content
    /// contribution and behave like auto tracks.
    /// </para>
    /// </remarks>
    private float[] ResolveTrackSizes(
        GridTrack[] tracks,
        float available,
        float[] contributions,
        float[] minContributions,
        float gap,
        in LengthContext context)
    {
        var sizes = new float[tracks.Length];
        var autoTracks = new List<int>(tracks.Length);
        var frTotal = 0f;
        var total = gap * Math.Max(0, tracks.Length - 1);

        for (var i = 0; i < tracks.Length; i++)
        {
            var track = tracks[i];

            switch (track.Kind)
            {
                case GridTrackKind.Fixed:
                case GridTrackKind.Percent:
                {
                    var minimum = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    var maximum = track.Maximum.ResolveOr(in context, available, minimum);

                    // Honour the minmax() maximum: grow from the minimum toward
                    // the content contribution without exceeding the limit.
                    if (float.IsFinite(maximum) && maximum > 0)
                    {
                        maximum = MathF.Max(minimum, maximum);
                        sizes[i] = Math.Clamp(MathF.Max(0, contributions[i]), minimum, maximum);
                    }
                    else
                    {
                        sizes[i] = minimum;
                    }

                    break;
                }

                case GridTrackKind.Fr:
                    frTotal += MathF.Max(0, track.Fr);
                    sizes[i] = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    sizes[i] = MathF.Max(sizes[i], MathF.Max(0, minContributions[i]));
                    autoTracks.Add(i);
                    break;

                case GridTrackKind.FitContent:
                {
                    var limit = MathF.Max(0, track.Maximum.ResolveOr(
                        in context,
                        available,
                        MathF.Max(0, track.FitContentLimit)));
                    sizes[i] = MathF.Min(MathF.Max(0, contributions[i]), limit);
                    break;
                }

                case GridTrackKind.MinContent:
                {
                    var minimum = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    sizes[i] = MathF.Max(minimum, MathF.Max(0, minContributions[i]));
                    break;
                }

                case GridTrackKind.MaxContent:
                {
                    var minimum = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    sizes[i] = MathF.Max(minimum, MathF.Max(0, contributions[i]));
                    break;
                }

                default:
                {
                    var minimum = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    sizes[i] = MathF.Max(minimum, MathF.Max(0, contributions[i]));
                    autoTracks.Add(i);
                    break;
                }
            }

            total += sizes[i];
        }

        if (float.IsFinite(available))
        {
            var free = available - total;

            if (free > 0 && frTotal > 0)
            {
                for (var i = 0; i < tracks.Length; i++)
                {
                    if (tracks[i].Kind == GridTrackKind.Fr)
                    {
                        sizes[i] += free * (MathF.Max(0, tracks[i].Fr) / frTotal);
                    }
                }
            }
            else if (free > 0 && autoTracks.Count > 0)
            {
                var share = free / autoTracks.Count;

                for (var i = 0; i < autoTracks.Count; i++)
                {
                    sizes[autoTracks[i]] += share;
                }
            }
        }
        else if (frTotal > 0)
        {
            // An indefinite axis has no free space to distribute; fr tracks
            // behave like auto tracks and keep their intrinsic contribution,
            // but never fall below an explicit minimum.
            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Kind == GridTrackKind.Fr)
                {
                    var minimum = MathF.Max(0, tracks[i].Minimum.ResolveOr(in context, available, 0));
                    sizes[i] = MathF.Max(minimum, MathF.Max(0, minContributions[i]));
                }
            }
        }

        return sizes;
    }

    /// <summary>Computes the leading edge of every track from its size and the gap.</summary>
    /// <param name="sizes">Resolved track sizes in CSS pixels.</param>
    /// <param name="gap">Gap between tracks in CSS pixels.</param>
    /// <returns>Offsets, relative to the grid content box origin, one per track.</returns>
    /// <remarks>
    /// The cursor starts at zero and advances by each size plus one gap, so the
    /// returned positions do not include a gap before the first track.
    /// </remarks>
    private static float[] GridPositions(float[] sizes, float gap)
    {
        var positions = new float[sizes.Length];
        var cursor = 0f;

        for (var i = 0; i < sizes.Length; i++)
        {
            positions[i] = cursor;
            cursor += sizes[i] + gap;
        }

        return positions;
    }

    /// <summary>Measures a cell spanning one or more tracks, including internal gaps.</summary>
    /// <param name="sizes">Resolved track sizes in CSS pixels.</param>
    /// <param name="start">Zero-based index of the first track.</param>
    /// <param name="span">Number of tracks covered.</param>
    /// <param name="gap">Gap between tracks in CSS pixels.</param>
    /// <returns>
    /// The span's size in CSS pixels; zero when <paramref name="start"/> is out
    /// of range.
    /// </returns>
    /// <remarks>
    /// The loop stops at the end of <paramref name="sizes"/>, so an oversized span
    /// is silently truncated rather than reading past the array. A gap is added
    /// between consecutive tracks but not before the first or after the last.
    /// </remarks>
    private static float GridSpanSize(float[] sizes, int start, int span, float gap)
    {
        var total = 0f;

        for (var i = start; i < start + span && i < sizes.Length; i++)
        {
            total += sizes[i];

            if (i > start)
            {
                total += gap;
            }
        }

        return total;
    }

    /// <summary>
    /// Resolves an item's <c>justify-self</c> against the container's
    /// <c>justify-items</c>.
    /// </summary>
    /// <param name="item">Computed style of the grid item.</param>
    /// <param name="container">Computed style of the grid container.</param>
    /// <returns>
    /// The effective inline-axis alignment; never <see cref="JustifySelf.Auto"/>.
    /// </returns>
    /// <remarks>
    /// Only an explicit <c>auto</c> falls back to the container. The container's
    /// <see cref="JustifyItems.Normal"/> and <see cref="JustifyItems.Legacy"/>
    /// map to <see cref="JustifySelf.Stretch"/>, matching the grid default. An
    /// item-level <see cref="JustifySelf.Normal"/> is returned unchanged and is
    /// treated like start by the offset helper.
    /// </remarks>
    private static JustifySelf EffectiveJustifySelf(ComputedStyle item, ComputedStyle container)
    {
        return item.JustifySelf switch
        {
            JustifySelf.Auto => container.JustifyItems switch
            {
                JustifyItems.Stretch => JustifySelf.Stretch,
                JustifyItems.Start => JustifySelf.Start,
                JustifyItems.End => JustifySelf.End,
                JustifyItems.Center => JustifySelf.Center,
                JustifyItems.Left => JustifySelf.Left,
                JustifyItems.Right => JustifySelf.Right,
                _ => JustifySelf.Stretch
            },
            _ => item.JustifySelf
        };
    }

    /// <summary>Computes the inline-axis offset of an item inside its cell.</summary>
    /// <param name="justify">Effective <c>justify-self</c> alignment.</param>
    /// <param name="free">
    /// Free space in the cell after margins and item width, in CSS pixels.
    /// </param>
    /// <returns>Zero, half the free space, or all of it, depending on the alignment.</returns>
    /// <remarks>
    /// Non-positive free space returns zero, so an overflowing item stays at its
    /// start edge. End/right/self-end push the item fully to the end; center
    /// splits the space; start, stretch, normal and self-start stay at zero.
    /// </remarks>
    private static float AlignGridOffset(JustifySelf justify, float free)
    {
        if (free <= 0)
        {
            return 0;
        }

        return justify switch
        {
            JustifySelf.End or JustifySelf.Right or JustifySelf.SelfEnd => free,
            JustifySelf.Center => free * 0.5f,
            _ => 0
        };
    }

    /// <summary>Computes the block-axis offset of an item inside its cell.</summary>
    /// <param name="align">Effective <c>align-self</c> alignment.</param>
    /// <param name="free">
    /// Free space in the cell after margins and item height, in CSS pixels.
    /// </param>
    /// <returns>Zero, half the free space, or all of it, depending on the alignment.</returns>
    /// <remarks>
    /// Non-positive free space returns zero. Flex-end/end/self-end push the item
    /// fully to the end; center splits the space; start, stretch and baseline stay
    /// at zero. Baseline alignment is therefore approximated as start.
    /// </remarks>
    private static float AlignGridOffset(AlignItems align, float free)
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
}
