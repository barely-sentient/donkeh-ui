namespace Donkeh.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// A simple fixed-column table layout. Rows are discovered through
    /// row-group boxes, column widths are the max-content width of every cell in
    /// that column, and spare width is shared across columns. Cells are laid out
    /// with a forced border-box width so text wraps inside the column instead of
    /// reflowing the rest of the row.
    /// </summary>
    /// <param name="box">Table or inline-table box whose rows are laid out.</param>
    /// <param name="content">Content box of the table in absolute coordinates.</param>
    /// <param name="definiteContentHeight">
    /// Definite content height passed to cell layout for percentage heights.
    /// </param>
    /// <returns>The total used row height in CSS pixels.</returns>
    /// <remarks>
    /// <para>
    /// Column sizing is max-content based: the widest cell in each column wins.
    /// A column with no measurable content gets ten percent of an equal share so
    /// it stays visible. When the columns are narrower than the table the spare
    /// width is divided equally; when they are wider they are scaled down
    /// proportionally to fit. There is no per-cell explicit column width or
    /// colspan handling.
    /// </para>
    /// <para>
    /// Each row is laid out twice: once to discover its height and once with that
    /// height forced on every cell, so borders and backgrounds cover the full
    /// row. Row groups then receive a bounding box spanning their descendant
    /// rows. Cells are positioned in document order and rows stack vertically
    /// without margin collapsing. The algorithm allocates row and cell lists plus
    /// a column-width array per table.
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
        var columnCount = 0;

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = new List<LayoutBox>(8);
            CollectTableCells(rows[r], cells);
            rowCells[r] = cells;
            columnCount = Math.Max(columnCount, cells.Count);
        }

        if (columnCount == 0)
        {
            return 0;
        }

        var widths = new float[columnCount];

        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rowCells[r];

            for (var c = 0; c < cells.Count; c++)
            {
                widths[c] = MathF.Max(widths[c], MeasureMaxContentWidth(cells[c]));
            }
        }

        var available = MathF.Max(0, content.Width);
        var total = 0f;

        for (var c = 0; c < widths.Length; c++)
        {
            if (widths[c] <= 0)
            {
                widths[c] = MathF.Max(1, available / columnCount * 0.1f);
            }

            total += widths[c];
        }

        if (total <= 0)
        {
            for (var c = 0; c < widths.Length; c++)
            {
                widths[c] = available / columnCount;
            }
        }
        else if (total < available)
        {
            var extra = (available - total) / columnCount;

            for (var c = 0; c < widths.Length; c++)
            {
                widths[c] += extra;
            }
        }
        else if (total > available && available > 0)
        {
            var scale = available / total;

            for (var c = 0; c < widths.Length; c++)
            {
                widths[c] *= scale;
            }
        }

        var y = content.Y;
        var usedHeight = 0f;

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var cells = rowCells[r];
            var x = content.X;
            var rowHeight = 0f;

            for (var c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                var cellWidth = widths[Math.Min(c, widths.Length - 1)];

                PrepareAndMeasureWidth(
                    cell,
                    content.Width,
                    cellWidth,
                    fillWidth: false,
                    shrinkToFit: false,
                    allowAutoMargins: false);

                var height = LayoutPreparedBox(
                    cell,
                    x,
                    y,
                    content.Width,
                    definiteContentHeight,
                    forcedBorderHeight: null,
                    fillHeight: false,
                    definiteContentHeight: null);

                rowHeight = MathF.Max(rowHeight, height);
                x += cellWidth;
            }

            // Second pass: stretch every cell to the row height so borders,
            // backgrounds and hover states cover the whole row.
            x = content.X;

            for (var c = 0; c < cells.Count; c++)
            {
                var cell = cells[c];
                var cellWidth = widths[Math.Min(c, widths.Length - 1)];

                PrepareAndMeasureWidth(
                    cell,
                    content.Width,
                    cellWidth,
                    fillWidth: false,
                    shrinkToFit: false,
                    allowAutoMargins: false);

                LayoutPreparedBox(
                    cell,
                    x,
                    y,
                    content.Width,
                    definiteContentHeight,
                    forcedBorderHeight: rowHeight,
                    fillHeight: false,
                    definiteContentHeight: null);

                x += cellWidth;
            }

            row.Margin = Thickness.Zero;
            row.Border = Thickness.Zero;
            row.Padding = Thickness.Zero;
            row.ApplyBorderBox(content.X, y, content.Width, rowHeight);
            row.Baseline = rowHeight;
            UpdateScrollMetrics(row);

            y += rowHeight;
            usedHeight += rowHeight;
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

        return usedHeight;
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
