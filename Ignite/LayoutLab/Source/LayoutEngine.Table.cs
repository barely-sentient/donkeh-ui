namespace Ignite.LayoutLab;

public sealed partial class LayoutEngine
{
    /// <summary>
    /// A simple fixed-column table layout. Rows are discovered through
    /// row-group boxes, column widths are the max-content width of every cell in
    /// that column, and spare width is shared across columns. Cells are laid out
    /// with a forced border-box width so text wraps inside the column instead of
    /// reflowing the rest of the row.
    /// </summary>
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
