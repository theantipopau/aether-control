using System;
using System.Collections.Generic;

namespace AetherControl.Core.Layout;

/// <summary>
/// Row plan for one fluid metric-card row: how many children sit in each row, and how wide each
/// row's cells are. Produced by <see cref="FluidLayout.Plan"/>, consumed by the app's FluidPanel.
/// </summary>
public sealed class FluidLayoutPlan
{
    public FluidLayoutPlan(IReadOnlyList<int> itemsPerRow, IReadOnlyList<double> cellWidths)
    {
        ItemsPerRow = itemsPerRow;
        CellWidths = cellWidths;
        var total = 0;
        foreach (var items in itemsPerRow)
        {
            total += items;
        }

        TotalItems = total;
    }

    public static FluidLayoutPlan Empty { get; } = new(Array.Empty<int>(), Array.Empty<double>());

    /// <summary>Child count per row, in order.</summary>
    public IReadOnlyList<int> ItemsPerRow { get; }

    /// <summary>Cell width for each row (index-aligned with <see cref="ItemsPerRow"/>). Every cell
    /// in a row shares this width; the row's last child absorbs rounding so the row reaches the
    /// container's right edge exactly.</summary>
    public IReadOnlyList<double> CellWidths { get; }

    public int TotalItems { get; }

    public bool IsEmpty => ItemsPerRow.Count == 0;
}

/// <summary>
/// The math behind fluid metric-card rows. Given a container width and a child count it decides
/// columns per row and cell widths so that:
/// <list type="bullet">
///   <item>rows fill edge to edge — a partial row stretches its cells (flex-wrap stretch) instead
///   of leaving a ragged empty tail, which is what the fixed-ItemWidth VariableSizedWrapGrid plus
///   the earlier FluidWrapGrid.Attach pass produced when a row's items didn't divide the column
///   count (reported as "not scaling correctly", 2026-10-02);</item>
///   <item>no row ends up with a single lone child (the orphaned VOLTAGE card) unless the row
///   count makes it literally unavoidable;</item>
///   <item>cells never shrink below <see cref="SoftMinCellWidth"/> for a single row, never exceed
///   <see cref="MaxCellWidth"/> when any other layout exists, and multi-row layouts keep cells at
///   or above <see cref="MinCellWidth"/> where possible;</item>
///   <item>the plan is a pure function of (width, count) — unit-tested in FluidLayoutTests, which
///   the old markup-attached VariableSizedWrapGrid sizing could not be (it lived entirely inside
///   a WinUI panel the test project can't reference).</item>
/// </list>
/// Priority order when constraints collide: exact division (every row full) &gt; no lone-child row
/// &gt; cell cap &gt; larger column count (cells closer to the minimum).
/// </summary>
public static class FluidLayout
{
    /// <summary>Comfortable minimum cell (164px card + its 12px trailing margin).</summary>
    public const double MinCellWidth = 176;

    /// <summary>Single rows may dip to this so N items still share one row instead of orphaning
    /// the last one (152 = 140px card + margin, matching MetricCard's MinWidth).</summary>
    public const double SoftMinCellWidth = 152;

    /// <summary>No stretched cell grows past this (one item in a wide column becomes a wide card,
    /// not a full-bleed bar).</summary>
    public const double MaxCellWidth = 360;

    public static FluidLayoutPlan Plan(double width, int count)
    {
        if (count <= 0 || width < 1)
        {
            return FluidLayoutPlan.Empty;
        }

        var singleCell = Math.Floor(width / count);
        if (singleCell >= SoftMinCellWidth)
        {
            // Everything fits on one row; cap how far a stray item may stretch.
            return OneRow(count, Math.Min(singleCell, MaxCellWidth));
        }

        var maxColumns = Math.Max(1, (int)Math.Floor(width / MinCellWidth));
        var columns = ChooseColumns(width, count, maxColumns);
        if (columns <= 0)
        {
            // Degenerate width (below one minimum cell): one row of small cells still fits exactly.
            return OneRow(count, Math.Max(1, singleCell));
        }

        var rows = new List<int>();
        var cells = new List<double>();
        for (var remaining = count; remaining > 0; remaining -= columns)
        {
            var items = Math.Min(columns, remaining);
            rows.Add(items);
            cells.Add(Math.Floor(width / items));
        }

        return new FluidLayoutPlan(rows, cells);
    }

    private static FluidLayoutPlan OneRow(int count, double cellWidth) =>
        new(new[] { count }, new[] { cellWidth });

    /// <summary>Largest column count first (cells closest to the minimum), three passes.</summary>
    private static int ChooseColumns(double width, int count, int maxColumns)
    {
        if (maxColumns < 2)
        {
            return -1;
        }

        // Pass 1: rows divide the count exactly — every row full, no stretch needed.
        for (var columns = maxColumns; columns >= 2; columns--)
        {
            if (count % columns == 0 && CellFits(width, count, columns))
            {
                return columns;
            }
        }

        // Pass 2: no lone-child last row, cells within the cap (partial rows stretch to fill).
        for (var columns = maxColumns; columns >= 2; columns--)
        {
            if (LastRowItems(count, columns) != 1 && CellFits(width, count, columns))
            {
                return columns;
            }
        }

        // Pass 3: prefer a lone-free layout even if the stretched final row exceeds the cap.
        for (var columns = maxColumns; columns >= 2; columns--)
        {
            if (LastRowItems(count, columns) != 1)
            {
                return columns;
            }
        }

        return -1;
    }

    private static int LastRowItems(int count, int columns)
    {
        var remainder = count % columns;
        return remainder == 0 ? columns : remainder;
    }

    private static bool CellFits(double width, int count, int columns) =>
        Math.Floor(width / LastRowItems(count, columns)) <= MaxCellWidth;
}
