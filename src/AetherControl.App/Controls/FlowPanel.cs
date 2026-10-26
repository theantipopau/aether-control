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
///
/// <see cref="Masonry"/> switches to column packing: the same column count, but each child goes
/// into the currently shortest column. Use it when cards differ a lot in height — in row mode a
/// tall card leaves the short cards beside it stretched into empty space (Phase 67: the
/// Performance page's Fan Control card left Gaming Profile and Quick Tasks mostly blank).
/// </summary>
public sealed class FlowPanel : Panel
{
    public static readonly DependencyProperty MinItemWidthProperty = DependencyProperty.Register(
        nameof(MinItemWidth), typeof(double), typeof(FlowPanel), new PropertyMetadata(420d));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(
        nameof(Spacing), typeof(double), typeof(FlowPanel), new PropertyMetadata(20d));

    public static readonly DependencyProperty MasonryProperty = DependencyProperty.Register(
        nameof(Masonry), typeof(bool), typeof(FlowPanel), new PropertyMetadata(false));

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

    /// <summary>When true, children are packed into the shortest column (masonry) rather than
    /// into rows whose height is set by the tallest child.</summary>
    public bool Masonry
    {
        get => (bool)GetValue(MasonryProperty);
        set => SetValue(MasonryProperty, value);
    }

    public static readonly DependencyProperty MaxCellWidthProperty = DependencyProperty.Register(
        nameof(MaxCellWidth), typeof(double), typeof(FlowPanel), new PropertyMetadata(FlowLayout.DefaultMaxCellWidth));

    /// <summary>Widest a section card may stretch to fill its row — past this the row simply ends
    /// early (left-aligned) instead of growing a full-bleed bar of empty chrome on wide windows.</summary>
    public double MaxCellWidth
    {
        get => (double)GetValue(MaxCellWidthProperty);
        set => SetValue(MaxCellWidthProperty, value);
    }

    private FlowLayoutPlan _plan = FlowLayoutPlan.Empty;
    private double _plannedWidth = -1;
    private int _plannedItemCount;
    private double[] _rowHeights = [];

    // Masonry placement: child index, x and y of each placed child, plus the total height.
    private readonly List<(int Index, double X, double Y)> _masonryPositions = [];
    private double _masonryHeight;

    /// <summary>Indices that threw during measure (Phase 65: OptimisationPage's first layout threw
    /// "Element is already the child of another element" out of a child's own Measure, with the
    /// native boundary hiding which element). Skipped in both passes so one poisoned child can't
    /// take the page down, and each skip is logged with the child's identity so the real culprit
    /// is named in unhandled-exceptions.log instead of guessed at. Cleared whenever the child
    /// set changes, so a transient failure doesn't stick.</summary>
    private readonly HashSet<int> _brokenChildren = [];

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width)
            ? Children.Count * MinItemWidth + Math.Max(0, Children.Count - 1) * Spacing
            : availableSize.Width;

        Plan(width);
        if (Masonry)
        {
            MeasureMasonry();
            return new Size(width, _masonryHeight);
        }

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
            if (Masonry)
            {
                MeasureMasonry();
            }
            else
            {
                MeasureRows();
            }
        }

        if (Masonry)
        {
            foreach (var (index, x, y) in _masonryPositions)
            {
                var child = Children[index];
                child.Arrange(new Rect(x, y, _plan.CellWidth, child.DesiredSize.Height));
            }

            return finalSize;
        }

        var childIndex = 0;
        var rowY = 0d;
        for (var row = 0; row < _rowHeights.Length && childIndex < Children.Count; row++)
        {
            var x = 0d;
            for (var i = 0; i < _plan.ItemsPerRow && childIndex < Children.Count; i++, childIndex++)
            {
                if (_brokenChildren.Contains(childIndex))
                {
                    continue;
                }

                var child = Children[childIndex];
                child.Arrange(new Rect(x, rowY, _plan.CellWidth, child.DesiredSize.Height));
                x += _plan.CellWidth + Spacing;
            }

            rowY += _rowHeights[row] + Spacing;
        }

        return finalSize;
    }

    private void Plan(double width)
    {
        if (Children.Count != _plannedItemCount)
        {
            _brokenChildren.Clear();
        }

        _plan = FlowLayout.Plan(width, Children.Count, MinItemWidth, Spacing, MaxCellWidth);
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
                if (_brokenChildren.Contains(index) || !MeasureChild(index))
                {
                    continue;
                }

                rowHeight = Math.Max(rowHeight, Children[index].DesiredSize.Height);
            }

            heights[row] = rowHeight;
        }

        _rowHeights = heights;
    }

    /// <summary>Places each child in the currently shortest column. Column count is the same as
    /// row mode's ItemsPerRow, so the page keeps its width-responsive column count.</summary>
    private void MeasureMasonry()
    {
        _masonryPositions.Clear();
        var columnHeights = new double[Math.Max(1, _plan.ItemsPerRow)];

        for (var index = 0; index < Children.Count; index++)
        {
            if (_brokenChildren.Contains(index) || !MeasureChild(index))
            {
                continue;
            }

            var column = 0;
            for (var c = 1; c < columnHeights.Length; c++)
            {
                if (columnHeights[c] < columnHeights[column])
                {
                    column = c;
                }
            }

            _masonryPositions.Add((index, column * (_plan.CellWidth + Spacing), columnHeights[column]));
            columnHeights[column] += Children[index].DesiredSize.Height + Spacing;
        }

        var tallest = 0d;
        foreach (var h in columnHeights)
        {
            tallest = Math.Max(tallest, h);
        }

        _masonryHeight = _masonryPositions.Count == 0 ? 0 : Math.Max(0, tallest - Spacing);
    }

    private bool MeasureChild(int index)
    {
        var child = Children[index];
        try
        {
            child.Measure(new Size(_plan.CellWidth, double.PositiveInfinity));
            return true;
        }
        catch (Exception ex)
        {
            _brokenChildren.Add(index);
            Services.UnhandledExceptionLog.Write(ex,
                $"FlowPanel skipped child #{index} ({DescribeChild(child)}) — it threw during Measure.");
            return false;
        }
    }

    private static string DescribeChild(UIElement child)
    {
        var name = (child as FrameworkElement)?.Name;
        var context = (child as FrameworkElement)?.DataContext?.GetType().Name;
        return $"type={child.GetType().Name} name='{name}' datacontext={context}";
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
