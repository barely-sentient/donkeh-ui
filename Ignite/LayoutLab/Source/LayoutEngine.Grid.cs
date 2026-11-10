namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// Grid layout with explicit and auto placement, fixed/percent/auto/fr
    /// tracks, gaps and item alignment. Auto placement is row-major, matching the
    /// default <c>grid-auto-flow</c>.
    /// </summary>
    private float LayoutGrid(LayoutBox box, Rect content, float? definiteContentHeight)
    {
        var style = box.Style;
        var items = CollectFlowChildren(box);
        StableSortByOrder(items);

        if (items.Count == 0)
        {
            return 0;
        }

        var context = CreateContext(style);
        var columnGap = ResolveGap(style.ColumnGap, in context, content.Width);
        var rowGap = ResolveGap(style.RowGap, in context, content.Height);

        var columnExpansion = ExpandTracks(
            style.GridTemplateColumns,
            content.Width,
            columnGap,
            in context,
            items.Count);

        var rowExpansion = ExpandTracks(
            style.GridTemplateRows,
            definiteContentHeight ?? float.NaN,
            rowGap,
            in context,
            items.Count);

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

        columnCount = columnTracks.Length;
        var rowCount = rowTracks.Length;

        var columnContributions = new float[columnCount];
        var rowContributions = new float[rowCount];

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var placement = placements[i];
            PrepareBox(item, content.Width);

            var width = MeasureMaxContentWidth(item) + item.Margin.Horizontal;
            var share = width / Math.Max(1, placement.ColumnSpan);

            for (var c = placement.Column; c < placement.Column + placement.ColumnSpan && c < columnCount; c++)
            {
                columnContributions[c] = MathF.Max(columnContributions[c], share);
            }
        }

        var columnSizes = ResolveTrackSizes(
            columnTracks,
            content.Width,
            columnContributions,
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

            PrepareAndMeasureWidth(item, content.Width, width, false, false, false);
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

    private readonly record struct GridItemPlacement(int Row, int Column, int RowSpan, int ColumnSpan);

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
            var columnSpan = Math.Max(1, columns[i].Span);
            var rowSpan = Math.Max(1, rows[i].Span);
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
                    Math.Max(1, columns[i].Span),
                    columnCount,
                    columns[i].StartAuto) >= 0)
            {
                continue;
            }

            var rowSpan = Math.Max(1, rows[i].Span);
            var columnSpan = Math.Max(1, columns[i].Span);
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

            var rowSpan = Math.Max(1, rows[i].Span);
            var columnSpan = Math.Max(1, columns[i].Span);
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
                return new GridPlacement(
                    areaStart + 1,
                    areaEnd + 1,
                    false,
                    false,
                    Math.Max(1, areaEnd - areaStart));
            }

            var index = FindLine(lineNames, startName, 0);

            if (index >= 0)
            {
                startLine = index + 1;
                startFound = true;
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

    private static int NormalizeGridLine(int line, int trackCount)
    {
        if (line == 0)
        {
            return -1;
        }

        return line > 0 ? line - 1 : Math.Max(0, trackCount + line);
    }

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

    private readonly record struct ExpandedTracks(GridTrack[] Tracks, string[][] LineNames);

    /// <summary>
    /// Expands <c>repeat(auto-fill|auto-fit, ...)</c> to the number of copies
    /// that fit in the available space. A fixed or <c>minmax()</c> minimum gives
    /// the repeat size; patterns without one fall back to a single copy.
    /// </summary>
    private static ExpandedTracks ExpandTracks(
        GridTrackList? list,
        float available,
        float gap,
        in LengthContext context,
        int itemCount)
    {
        if (list is null || list.Count == 0)
        {
            // An unspecified template means no explicit tracks. Every track is
            // implicit and picks up grid-auto-rows / grid-auto-columns from the
            // very first one.
            return new ExpandedTracks([], []);
        }

        if (list.AutoRepeatIndex < 0 || list.AutoRepeatTracks.Length == 0)
        {
            return new ExpandedTracks(list.Tracks, list.LineNames);
        }

        var pattern = list.AutoRepeatTracks;
        var patternNames = list.AutoRepeatLineNames;
        var repeats = CountAutoRepeats(pattern, available, gap, in context);

        if (list.AutoRepeatFit)
        {
            // auto-fit collapses tracks that would otherwise stay empty. With a
            // repeated pattern this is approximated by the item count.
            repeats = Math.Clamp(repeats, 1, Math.Max(1, itemCount));
        }

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

        return new ExpandedTracks(tracks.ToArray(), ToNameArrays(names));
    }

    private static string[][] ToNameArrays(List<List<string>> names)
    {
        var result = new string[names.Count][];

        for (var i = 0; i < names.Count; i++)
        {
            result[i] = names[i].ToArray();
        }

        return result;
    }

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
                case GridTrackKind.FitContent:
                    size = track.Minimum.ResolveOr(in context, available, 0);
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

    private float[] ResolveTrackSizes(
        GridTrack[] tracks,
        float available,
        float[] contributions,
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
                    sizes[i] = MathF.Max(0, track.Minimum.ResolveOr(in context, available, 0));
                    break;

                case GridTrackKind.Fr:
                    frTotal += MathF.Max(0, track.Fr);
                    autoTracks.Add(i);
                    break;

                case GridTrackKind.FitContent:
                    sizes[i] = MathF.Min(
                        MathF.Max(0, track.FitContentLimit),
                        MathF.Max(0, contributions[i]));
                    break;

                default:
                    sizes[i] = MathF.Max(0, contributions[i]);

                    if (track.Kind == GridTrackKind.Auto)
                    {
                        autoTracks.Add(i);
                    }

                    break;
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
            // behave like auto tracks and keep their intrinsic contribution.
            for (var i = 0; i < tracks.Length; i++)
            {
                if (tracks[i].Kind == GridTrackKind.Fr)
                {
                    sizes[i] = MathF.Max(0, contributions[i]);
                }
            }
        }

        return sizes;
    }

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
