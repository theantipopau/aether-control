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
/// width and the gap between them. Cells stretch to share the row — but never past
/// <paramref name="maxCellWidth"/> (FluidLayout's MaxCellWidth precedent): section content is
/// authored against ~560–620px comfortable widths, so an uncapped lone trailing section on a wide
/// window became a full-bleed bar of empty chrome (reported as "huge empty space" on
/// Optimisation at window sizes where only one or two sections share the row, 2026-10-18).
/// Capped rows simply don't reach the right edge; the panel left-aligns them. One child per row
/// whenever the width can't fit two side by side; never zero.
/// </summary>
public static class FlowLayout
{
    /// <summary>Default cap: comfortably past every page's MinItemWidth (440–620), tight enough
    /// that a lone section never turns into a full-bleed empty bar on a 1440p-maximised window.</summary>
    public const double DefaultMaxCellWidth = 720;

    public static FlowLayoutPlan Plan(double width, int count, double minItemWidth, double spacing, double maxCellWidth = DefaultMaxCellWidth)
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

        // The cap never squeezes below the minimum — a nonsense max just means "fill".
        var cap = double.IsFinite(maxCellWidth) && maxCellWidth > 0 ? Math.Max(maxCellWidth, min) : min;
        var cellWidth = Math.Min((w - gap * (perRow - 1)) / perRow, cap);
        var rows = (count + perRow - 1) / perRow;

        return new FlowLayoutPlan(perRow, cellWidth, rows);
    }
}
