using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>
/// Keeps metric-card rows fluid as the window resizes. The wrap grids used a fixed ItemWidth
/// (164/224), so on a wide window cards stopped short of the column edge and left a ragged
/// gutter, and on a narrow one a row could hold fewer cards than there was clearly room for.
/// <para>
/// Instead: columns = floor(width / 176) — 176 is the design minimum cell (a 164px card plus
/// the 12px trailing margin the page styles apply) — and ItemWidth divides the grid's actual
/// width exactly, so every row runs edge to edge while no card ever drops below its minimum.
/// MetricCard stretches to fill its cell (its root Border is MinWidth, not a fixed Width).
/// </para>
/// Walks the visual tree because the grids live inside ItemsControl panel templates — they only
/// exist once their containers have been realized.
/// </summary>
internal static class FluidWrapGrid
{
    public const double MinCellWidth = 176;

    public static int ComputeColumns(double availableWidth) =>
        availableWidth <= 0 ? 0 : Math.Max(1, (int)Math.Floor(availableWidth / MinCellWidth));

    public static void Apply(DependencyObject root) => Walk(root);

    private static void Walk(DependencyObject node)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is VariableSizedWrapGrid grid && grid.ActualWidth > 0)
            {
                var columns = ComputeColumns(grid.ActualWidth);
                var itemWidth = Math.Floor(grid.ActualWidth / columns);
                if (Math.Abs(grid.ItemWidth - itemWidth) > 0.5)
                {
                    grid.ItemWidth = itemWidth;
                }
            }

            Walk(child);
        }
    }
}
