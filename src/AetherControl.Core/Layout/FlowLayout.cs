using System;

namespace AetherControl.Core.Layout;

/// <summary>Result of <see cref="FlowLayout.Plan"/>: how many items share a row and how wide each
/// cell is. Row *heights* depend on the children themselves, so they stay in the panel — this part
/// is just the arithmetic worth testing on its own.</summary>
public sealed record FlowLayoutPlan(int ItemsPerRow, double CellWidth, int RowCount)
{
    public static readonly FlowLayoutPlan Empty = new(0, 0, 0);
}

/// <summary>
/// Greedy wrap plan for section-scale content (cards, settings groups) that must reflow with the
/// window: how many children fit per row at a given width, given each child's minimum comfortable
/// width and the gap between them. Unlike <see cref="FluidLayout"/> (fixed-height metric-card rows
/// with a cell band), this has no upper cap — cells grow to fill the row, so a wide window spreads
/// the content out instead of pinning it to a maximum card width. One child per row whenever the
/// width can't fit two side by side; never zero.
/// </summary>
public static class FlowLayout
{
    public static FlowLayoutPlan Plan(double width, int count, double minItemWidth, double spacing)
    {
        if (count <= 0)
        {
            return FlowLayoutPlan.Empty;
        }

        var w = double.IsFinite(width) && width > 0 ? width : minItemWidth;
        var min = minItemWidth > 0 ? minItemWidth : 1;
        var gap = spacing > 0 ? spacing : 0;

        // How many slots of `min` plus their gaps fit: n*min + (n-1)*gap <= w
        //  ->  n <= (w + gap) / (min + gap)
        var perRow = (int)Math.Floor((w + gap) / (min + gap));
        perRow = Math.Max(1, Math.Min(perRow, count));

        var cellWidth = (w - gap * (perRow - 1)) / perRow;
        var rows = (count + perRow - 1) / perRow;

        return new FlowLayoutPlan(perRow, cellWidth, rows);
    }
}
