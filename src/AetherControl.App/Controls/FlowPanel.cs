using System;
using AetherControl.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace AetherControl.App.Controls;

/// <summary>
/// Variable-height wrapping panel for section-scale content (settings groups, optimiser cards,
/// list cards). Children flow left-to-right and wrap to the next row based on
/// <see cref="MinItemWidth"/> — one column when the window is narrow, several when it is wide —
/// with each row as tall as its tallest child. This is the section-level counterpart of
/// <see cref="FluidPanel"/>: same pure Core plan (<see cref="FlowLayout.Plan"/>), but rows here
/// have no fixed height because section cards are as tall as their content.
/// </summary>
public sealed class FlowPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(FlowPanel), new PropertyMetadata(420d));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(20d));

    /// <summary>Minimum comfortable width of one child — below this the row count drops instead
    /// of squeezing children further.</summary>
    public double MinItemWidth
    {
        get => (double)GetValue(MinItemWidthProperty);
        set => SetValue(MinItemWidthProperty, value);
    }

    /// <summary>Gap between children, horizontally and vertically.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private FlowLayoutPlan _plan = FlowLayoutPlan.Empty;
    private double _plannedWidth = -1;
    private int _plannedItemCount;
    private double[] _rowHeights = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? Children.Count * MinItemWidth + Math.Max(0, Children.Count - 1) * Spacing
            : availableSize.Width;

        Plan(width);
        MeasureRows();
        return new Size(width, TotalHeight());
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        // Remeasure when the final width differs from the planned one (scrollbar claiming space,
        // parent snapping) or children appeared after the last measure — arranging with a stale
        // cell width is exactly the "card past the edge" failure FluidPanel guards against.
        if (Math.Abs(finalSize.Width - _plannedWidth) > 0.5 || _plannedItemCount != Children.Count)
        {
            Plan(finalSize.Width);
            MeasureRows();
        }

        var index = 0;
        var y = 0d;
        for (var row = 0; row < _rowHeights.Length && index < Children.Count; row++)
        {
            var x = 0d;
            for (var i = 0; i < _plan.ItemsPerRow && index < Children.Count; i++, index++)
            {
                var child = Children[index];
                child.Arrange(new Rect(x, y, _plan.CellWidth, child.DesiredSize.Height));
                x += _plan.CellWidth + Spacing;
            }

            y += _rowHeights[row] + Spacing;
        }

        return finalSize;
    }

    private void Plan(double width)
    {
        _plan = FlowLayout.Plan(width, Children.Count, MinItemWidth, Spacing);
        _plannedWidth = width;
        _plannedItemCount = Children.Count;
    }

    private void MeasureRows()
    {
        var heights = new double[Math.Max(1, _plan.RowCount)];

        var index = 0;
        for (var row = 0; row < _plan.RowCount; row++)
        {
            var rowHeight = 0d;
            for (var i = 0; i < _plan.ItemsPerRow && index < Children.Count; i++, index++)
            {
                var child = Children[index];
                child.Measure(new Size(_plan.CellWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            }

            heights[row] = rowHeight;
        }

        _rowHeights = heights;
    }

    private double TotalHeight()
    {
        if (_rowHeights.Length == 0)
        {
            return 0;
        }

        var total = 0d;
        foreach (var h in _rowHeights)
        {
            total += h;
        }

        return total + Spacing * Math.Max(0, _rowHeights.Length - 1);
    }
}
