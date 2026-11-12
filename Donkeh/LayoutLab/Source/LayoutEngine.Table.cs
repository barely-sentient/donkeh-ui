namespace Donkeh.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>One cell and the grid rectangle it occupies.</summary>
    private readonly record struct TableCellPlacement(
        LayoutBox Cell,
        int Row,
        int Column,
        int RowSpan,
        int ColumnSpan);

    /// <summary>
    /// A simple table layout with colspan/rowspan, collapsed or separate borders,
    /// and border spacing.
    /// </summary>
    /// <param name="box">Table or inline-table box whose rows are laid out.</param>
    /// <param name="content">Content box of the table in absolute coordinates.</param>
    /// <param name="definiteContentHeight">
    /// Definite content height passed to cell layout for percentage heights.
    /// </param>
    /// <returns>The total used row height in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// Column sizing is max-content based: the widest cell in each column wins,
    /// with spanning cells sharing their width across the covered tracks. When
    /// columns are narrower than the table the spare width is divided equally;
    /// when they are wider they are scaled down proportionally, but never below
    /// their min-content width.
    /// </para>
    /// <para>
    /// Cells are measured once to discover row heights and then laid out again
    /// with the final row span forced on them. A rowspan cell grows the last row
    /// it covers when its content is taller than the rows it spans. Border
    /// spacing is applied between rows and columns; in collapsed mode spacing is
    /// zero and each cell paints half its border so adjacent edges form a single
    /// line.
    /// </para>
    /// </remarks>
    private float LayoutTable(LayoutBox box, Rect content, float definiteContentHeight)
    {
        var rows = new List<LayoutBox>();
        CollectTableRows(box, rows);

        if (rows.Count == 0)
        {
            return 0;
        }

        var rowCells = new List<LayoutBox>[rows.Count];

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = new List<LayoutBox>(8);
            CollectTableCells(rows[r], cells);
            rowCells[r] = cells;
        }

        var placements = PlaceTableCells(rows.Count, rowCells, out var columnCount);

        if (columnCount == 0 || placements.Count == 0)
        {
            return 0;
        }

        var style = box.Style;
        var context = CreateContext(style);
        var collapse = style.BorderCollapse == BorderCollapse.Collapse;
        var spacing = collapse
            ? 0
            : MathF.Max(0, style.BorderSpacing.ResolveOr(in context, content.Width, 0));

        // Halving each cell border once, before either measurement pass, keeps
        // adjacent collapsed edges from doubling and avoids halving twice.
        for (var i = 0; i < placements.Count; i++)
        {
            ApplyCollapsedCellStyle(placements[i].Cell, collapse, content.Width);
        }

        var widths = new float[columnCount];
        var minWidths = new float[columnCount];

        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];
            var span = Math.Max(1, placement.ColumnSpan);
            var maxContent = MeasureMaxContentWidth(placement.Cell);
            var minContent = MeasureMinContentWidth(placement.Cell);
            var share = maxContent / span;
            var minShare = minContent / span;

            for (var c = placement.Column; c < placement.Column + placement.ColumnSpan && c < columnCount; c++)
            {
                widths[c] = MathF.Max(widths[c], share);
                minWidths[c] = MathF.Max(minWidths[c], minShare);
            }
        }

        // A spanning cell may be wider than the columns it inherited; share the
        // deficit so the cell still fits.
        for (var i = 0; i < placements.Count; i++)
        {
            var placement = placements[i];

            if (placement.ColumnSpan <= 1)
            {
                continue;
            }

            var needed = MeasureMaxContentWidth(placement.Cell);
            var available = SpanSize(widths, placement.Column, placement.ColumnSpan, spacing);

            if (needed <= available)
            {
                continue;
            }

            var share = (needed - available) / placement.ColumnSpan;

            for (var c = placement.Column; c < placement.Column + placement.ColumnSpan && c < columnCount; c++)
            {
                widths[c] += share;
            }
        }

        var availableWidth = MathF.Max(0, content.Width);
        var total = spacing * Math.Max(0, columnCount - 1);

        for (var c = 0; c < columnCount; c++)
        {
            if (widths[c] <= 0)
            {
                widths[c] = MathF.Max(1, availableWidth / columnCount * 0.1f);
            }

            minWidths[c] = MathF.Min(widths[c], MathF.Max(0, minWidths[c]));
            total += widths[c];
        }

        if (total <= 0)
        {
            for (var c = 0; c < columnCount; c++)
            {
                widths[c] = availableWidth / columnCount;
            }
        }
        else if (total < availableWidth)
        {
            var extra = (availableWidth - total) / columnCount;

            for (var c = 0; c < columnCount; c++)
            {
                widths[c] += extra;
            }
        }
        else if (total > availableWidth)
        {
            var scale = availableWidth > 0 ? availableWidth / total : 0;

            for (var c = 0; c < columnCount; c++)
            {
                widths[c] = MathF.Max(minWidths[c], widths[c] * scale);
            }
        }

        var rowHeights = new float[rows.Count];
        var spanRequirements = new List<(TableCellPlacement Placement, float Natural)>();

        // Measurement pass: discover each cell's natural height. Single-row
        // cells set their row's baseline; rowspans are resolved afterwards so a
        // tall span cannot be counted twice against later cells.
        for (var r = 0; r < rows.Count; r++)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];

                if (placement.Row != r)
                {
                    continue;
                }

                var cellWidth = SpanSize(widths, placement.Column, placement.ColumnSpan, spacing);
                var x = content.X + ColumnOffset(widths, placement.Column, spacing);

                PrepareAndMeasureWidth(
                    placement.Cell,
                    content.Width,
                    cellWidth,
                    fillWidth: false,
                    shrinkToFit: false,
                    allowAutoMargins: false);

                var natural = LayoutPreparedBox(
                    placement.Cell,
                    x,
                    content.Y,
                    content.Width,
                    definiteContentHeight,
                    forcedBorderHeight: null,
                    fillHeight: false,
                    definiteContentHeight: null);

                if (placement.RowSpan <= 1)
                {
                    rowHeights[placement.Row] = MathF.Max(rowHeights[placement.Row], natural);
                }
                else
                {
                    spanRequirements.Add((placement, natural));
                }
            }
        }

        // Grow the last row of a rowspan just enough for the cell's natural
        // height; other cells in earlier rows keep their own heights.
        for (var i = 0; i < spanRequirements.Count; i++)
        {
            var (placement, natural) = spanRequirements[i];
            var available = SpanSize(rowHeights, placement.Row, placement.RowSpan, spacing);

            if (natural > available)
            {
                rowHeights[placement.Row + placement.RowSpan - 1] += natural - available;
            }
        }

        // Final pass: force the complete row span onto every cell.
        var cursorY = content.Y;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];

                if (placement.Row != r)
                {
                    continue;
                }

                var x = content.X + ColumnOffset(widths, placement.Column, spacing);
                var cellWidth = SpanSize(widths, placement.Column, placement.ColumnSpan, spacing);
                var cellHeight = SpanSize(rowHeights, placement.Row, placement.RowSpan, spacing);

                PrepareAndMeasureWidth(
                    placement.Cell,
                    content.Width,
                    cellWidth,
                    fillWidth: false,
                    shrinkToFit: false,
                    allowAutoMargins: false);

                LayoutPreparedBox(
                    placement.Cell,
                    x,
                    cursorY,
                    content.Width,
                    definiteContentHeight,
                    forcedBorderHeight: cellHeight,
                    fillHeight: false,
                    definiteContentHeight: null);
            }

            var row = rows[r];
            row.Margin = Thickness.Zero;
            row.Border = Thickness.Zero;
            row.Padding = Thickness.Zero;
            row.ApplyBorderBox(content.X, cursorY, content.Width, rowHeights[r]);
            row.Baseline = rowHeights[r];
            UpdateScrollMetrics(row);

            cursorY += rowHeights[r] + (r + 1 < rows.Count ? spacing : 0);
        }

        // Give row groups their bounding box so their backgrounds and scroll
        // metrics cover the rows they own.
        for (var i = 0; i < box.Children.Count; i++)
        {
            var group = box.Children[i];

            if (group.Style.Display is Display.TableRow or Display.TableCell)
            {
                continue;
            }

            var top = float.PositiveInfinity;
            var bottom = float.NegativeInfinity;

            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];

                if (IsDescendantOf(row, group))
                {
                    top = MathF.Min(top, row.BorderBox.Y);
                    bottom = MathF.Max(bottom, row.BorderBox.Bottom);
                }
            }

            if (float.IsFinite(top) && bottom > top)
            {
                group.Margin = Thickness.Zero;
                group.Border = Thickness.Zero;
                group.Padding = Thickness.Zero;
                group.ApplyBorderBox(content.X, top, content.Width, bottom - top);
                group.Baseline = bottom - top;
                UpdateScrollMetrics(group);
            }
        }

        var usedHeight = spacing * Math.Max(0, rows.Count - 1);

        for (var r = 0; r < rowHeights.Length; r++)
        {
            usedHeight += rowHeights[r];
        }

        return MathF.Max(0, usedHeight);
    }

    /// <summary>Places cells into a row/column grid, honoring row and column spans.</summary>
    /// <param name="rowCount">Number of rows to populate.</param>
    /// <param name="rowCells">Cells collected per row.</param>
    /// <param name="columnCount">Receives the widest occupied column count.</param>
    /// <returns>One placement per cell in document order.</returns>
    /// <remarks>
    /// Occupied slots left by a rowspan are skipped when the next row places its
    /// cells. A <c>rowspan="0"</c> attribute is treated as spanning the remaining
    /// rows, matching HTML.
    /// </remarks>
    private static List<TableCellPlacement> PlaceTableCells(
        int rowCount,
        List<LayoutBox>[] rowCells,
        out int columnCount)
    {
        var placements = new List<TableCellPlacement>();
        var occupied = new List<bool[]>();
        columnCount = 0;

        for (var r = 0; r < rowCount; r++)
        {
            EnsureOccupiedRows(occupied, r + 1);
            var cells = rowCells[r];
            var column = 0;

            for (var i = 0; i < cells.Count; i++)
            {
                var cell = cells[i];

                while (column < occupied[r].Length && occupied[r][column])
                {
                    column++;
                }

                var columnSpan = Math.Clamp(ReadSpan(cell, "colspan", 1), 1, 1024);
                var rowSpan = ReadSpan(cell, "rowspan", 1);

                if (rowSpan <= 0)
                {
                    rowSpan = rowCount - r;
                }

                rowSpan = Math.Clamp(rowSpan, 1, rowCount - r);
                EnsureOccupiedRows(occupied, r + rowSpan);

                for (var rr = r; rr < r + rowSpan; rr++)
                {
                    var end = column + columnSpan;

                    if (occupied[rr].Length < end)
                    {
                        var rowSlots = occupied[rr];
                        Array.Resize(ref rowSlots, end);
                        occupied[rr] = rowSlots;
                    }

                    for (var cc = column; cc < end; cc++)
                    {
                        occupied[rr][cc] = true;
                    }
                }

                placements.Add(new TableCellPlacement(cell, r, column, rowSpan, columnSpan));
                column += columnSpan;
                columnCount = Math.Max(columnCount, column);
            }
        }

        return placements;
    }

    private static void EnsureOccupiedRows(List<bool[]> occupied, int count)
    {
        while (occupied.Count < count)
        {
            occupied.Add([]);
        }
    }

    /// <summary>Reads an HTML integer span attribute.</summary>
    private static int ReadSpan(LayoutBox cell, string name, int fallback)
    {
        return cell.Node.TryGetAttribute(name, out var text)
            && int.TryParse(text, out var value)
            ? value
            : fallback;
    }

    private static float ColumnOffset(float[] widths, int column, float spacing)
    {
        var offset = spacing * column;

        for (var c = 0; c < column && c < widths.Length; c++)
        {
            offset += widths[c];
        }

        return offset;
    }

    private static float SpanSize(float[] sizes, int start, int span, float spacing)
    {
        var total = 0f;

        for (var i = start; i < start + span && i < sizes.Length; i++)
        {
            if (i > start)
            {
                total += spacing;
            }

            total += sizes[i];
        }

        return total;
    }

    /// <summary>Halves a cell's border widths when the table collapses borders.</summary>
    private void ApplyCollapsedCellStyle(LayoutBox cell, bool collapse, float cbWidth)
    {
        if (!collapse)
        {
            return;
        }

        var style = cell.Style;
        var context = CreateContext(style);
        var widths = style.BorderWidth.Resolve(in context, cbWidth);

        if (widths.Top <= 0 && widths.Right <= 0 && widths.Bottom <= 0 && widths.Left <= 0)
        {
            return;
        }

        var clone = style.Clone();
        clone.BorderWidth = new CssLength4(
            CssLength.Px(widths.Top * 0.5f),
            CssLength.Px(widths.Right * 0.5f),
            CssLength.Px(widths.Bottom * 0.5f),
            CssLength.Px(widths.Left * 0.5f));
        cell.Style = clone;
    }

    /// <summary>Collects table rows in document order, descending through row groups.</summary>
    /// <param name="box">Table, row group or wrapper box to search.</param>
    /// <param name="rows">List that receives row boxes.</param>
    /// <remarks>
    /// Descends into row groups, block wrappers and <c>display:contents</c>
    /// boxes. Rows inside table cells are not collected, so a nested table is
    /// treated as cell content rather than as additional rows of the outer table.
    /// </remarks>
    private static void CollectTableRows(LayoutBox box, List<LayoutBox> rows)
    {
        for (var i = 0; i < box.Children.Count; i++)
        {
            var child = box.Children[i];

            if (child.Style.Display == Display.TableRow)
            {
                rows.Add(child);
                continue;
            }

            if (child.Style.Display is Display.TableRowGroup
                or Display.TableHeaderGroup
                or Display.TableFooterGroup
                or Display.Block
                or Display.Contents)
            {
                CollectTableRows(child, rows);
            }
        }
    }

    /// <summary>Collects the cells of one row in document order.</summary>
    /// <param name="row">Table row box to inspect.</param>
    /// <param name="cells">List that receives cell boxes.</param>
    /// <remarks>
    /// A child counts as a cell when its computed display is
    /// <c>table-cell</c> or its tag is <c>td</c>/<c>th</c>. The tag fallback
    /// covers markup whose cell display was not resolved.
    /// </remarks>
    private static void CollectTableCells(LayoutBox row, List<LayoutBox> cells)
    {
        for (var i = 0; i < row.Children.Count; i++)
        {
            var child = row.Children[i];

            if (child.Style.Display == Display.TableCell
                || child.Node.TagName is "td" or "th")
            {
                cells.Add(child);
            }
        }
    }

    /// <summary>Tests whether a box is a descendant of another box.</summary>
    /// <param name="box">Box whose ancestor chain is walked.</param>
    /// <param name="ancestor">Candidate ancestor.</param>
    /// <returns>
    /// <see langword="true"/> when <paramref name="ancestor"/> appears in the
    /// parent chain of <paramref name="box"/>.
    /// </returns>
    /// <remarks>
    /// Comparison is by reference and starts at the parent, so a box is not its
    /// own descendant. The walk cost is proportional to the depth of the box.
    /// </remarks>
    private static bool IsDescendantOf(LayoutBox box, LayoutBox ancestor)
    {
        for (var current = box.Parent; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, ancestor))
            {
                return true;
            }
        }

        return false;
    }
}
