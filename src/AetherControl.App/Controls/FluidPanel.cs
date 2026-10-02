using System;
using AetherControl.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AetherControl.App.Controls;

/// <summary>
/// A horizontal wrap panel for metric-card rows that plans its own rows via
/// <see cref="FluidLayout.Plan"/>: rows fill edge to edge (a partial row stretches its cells like
/// flex-wrap), no row ends up with a single lone child when avoidable, and cells stay inside
/// [SoftMin, Max]. It replaces the fixed-ItemWidth <c>VariableSizedWrapGrid</c> plus the
/// <c>FluidWrapGrid.Attach</c> walk (deleted 2026-10-02), which could only ever assign one uniform
/// ItemWidth per grid — so a 5-card row in a 6-card column left a ragged tail, 5 cards in a 4-card
/// column orphaned the VOLTAGE card onto its own row, and sizing lived in code the test project
/// couldn't reach. As a plain Panel it measures fresh on every layout pass, which also removes the
/// scrollbar-shrinks-the-content-after-attach race that once pushed a card past the window edge.
/// <para>
/// Never re-introduce sizing via a markup-attached property on this control: compiled XAML
/// resolves those through the runtime type system and the earlier
/// <c>controls:FluidWrapGrid.IsFluid="True"</c> threw "Failed to assign to property" inside
/// InitializeComponent (see FluidPanel/FluidWrapGrid history, unhandled-exceptions.log
/// 2026-10-02). Regular instance properties like <see cref="ItemHeight"/> are safe.
/// </para>
/// </summary>
public sealed class FluidPanel : Panel
{
    public static readonly DependencyProperty ItemHeightProperty = DependencyProperty.Register(
        nameof(ItemHeight), typeof(double), typeof(FluidPanel), new PropertyMetadata(106d));

    /// <summary>Fixed row height, matching the old VariableSizedWrapGrid.ItemHeight usages.</summary>
    public double ItemHeight
    {
        get => (double)GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    private FluidLayoutPlan _plan = FluidLayoutPlan.Empty;
    private double _plannedWidth = -1;

    // Best-effort layout evidence: the elevated app can't be screenshotted (UIPI), so every plan
    // *transition* is appended here — width, child count, resulting rows — making "it still isn't
    // scaling" diagnosable from a file instead of a pasted screenshot of a possibly-stale build.
    // Signed off only when the bucketed (width, count, rows) signature actually changes, throttled
    // to one line per 500 ms during a continuous resize drag, and the file is truncated at 256 KB
    // so it can't grow unbounded.
    private static readonly string LayoutLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Aether Control", "fluid-layout.log");
    private string? _lastLoggedSignature;
    private DateTimeOffset _lastLoggedPlan;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? Children.Count * FluidLayout.MinCellWidth
            : availableSize.Width;

        Plan(width);
        MeasureChildren();
        return new Size(width, _plan.ItemsPerRow.Count * ItemHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Remeasure when the final width differs from the planned one (a scrollbar claiming space
        // after measure, a parent snapping columns) or children appeared after the last measure —
        // arranging with stale cells is exactly the "one card past the edge" failure mode.
        if (Math.Abs(finalSize.Width - _plannedWidth) > 0.5 || _plan.TotalItems != Children.Count)
        {
            Plan(finalSize.Width);
            MeasureChildren();
        }

        var index = 0;
        var y = 0d;
        for (var row = 0; row < _plan.ItemsPerRow.Count && index < Children.Count; row++)
        {
            var items = _plan.ItemsPerRow[row];
            var cell = _plan.CellWidths[row];
            var x = 0d;
            for (var i = 0; i < items && index < Children.Count; i++, index++)
            {
                // Last child absorbs sub-pixel rounding so the row ends flush with the container.
                var cellWidth = i == items - 1 ? finalSize.Width - x : cell;
                Children[index].Arrange(new Rect(x, y, cellWidth, ItemHeight));
                x += cell;
            }

            y += ItemHeight;
        }

        return finalSize;
    }

    private void Plan(double width)
    {
        _plan = FluidLayout.Plan(width, Children.Count);
        _plannedWidth = width;
        LogPlanIfChanged();
    }

    private void LogPlanIfChanged()
    {
        var now = DateTimeOffset.UtcNow;
        var signature = $"{(int)(_plannedWidth / 4)}x{Children.Count}:{string.Join("+", _plan.ItemsPerRow)}";
        if (signature == _lastLoggedSignature || now - _lastLoggedPlan < TimeSpan.FromMilliseconds(500))
        {
            return;
        }

        _lastLoggedSignature = signature;
        _lastLoggedPlan = now;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LayoutLogPath)!);
            if (File.Exists(LayoutLogPath) && new FileInfo(LayoutLogPath).Length > 256 * 1024)
            {
                File.WriteAllText(LayoutLogPath, string.Empty);
            }

            File.AppendAllText(LayoutLogPath,
                $"[{now:yyyy-MM-dd HH:mm:ss.fff}] w={_plannedWidth:0} n={Children.Count} " +
                $"rows=[{string.Join("+", _plan.ItemsPerRow)}] cells=[{string.Join(",", _plan.CellWidths.Select(c => c.ToString("0")))}]\n");
        }
        catch
        {
            // best-effort diagnostic only
        }
    }

    private void MeasureChildren()
    {
        var index = 0;
        for (var row = 0; row < _plan.ItemsPerRow.Count && index < Children.Count; row++)
        {
            var cell = new Size(_plan.CellWidths[row], ItemHeight);
            for (var i = 0; i < _plan.ItemsPerRow[row] && index < Children.Count; i++, index++)
            {
                Children[index].Measure(cell);
            }
        }
    }
}
