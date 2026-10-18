using AetherControl.Core.Layout;

namespace AetherControl.Tests;

/// <summary>Flow planning backs the section-scale wrap panels — a page must show one full-width
/// column when it's narrow and pack more when it's wide, never zero or negative cells.</summary>
public class FlowLayoutTests
{
    [Fact]
    public void NarrowWidth_YieldsSingleFullWidthColumn()
    {
        var plan = FlowLayout.Plan(500, 7, minItemWidth: 420, spacing: 20);

        Assert.Equal(1, plan.ItemsPerRow);
        Assert.Equal(7, plan.RowCount);
        Assert.Equal(500, plan.CellWidth, 3);
    }

    [Fact]
    public void WideWidth_PacksMultiplePerRowAndCellsStayAtLeastMinWidth()
    {
        // 1400 available, 420 min + 20 gap -> floor((1400+20)/440) = 3 per row.
        var plan = FlowLayout.Plan(1400, 7, minItemWidth: 420, spacing: 20);

        Assert.Equal(3, plan.ItemsPerRow);
        Assert.Equal(3, plan.RowCount); // 3 + 3 + 1
        Assert.True(plan.CellWidth >= 420);
        // Cells + gaps fill the row edge to edge.
        Assert.Equal(1400, plan.ItemsPerRow * plan.CellWidth + (plan.ItemsPerRow - 1) * 20, 3);
    }

    [Fact]
    public void ExactFitBoundary_UsesAllSlots()
    {
        // Two mins + one gap fit exactly: 2*420 + 20 = 860.
        var plan = FlowLayout.Plan(860, 5, minItemWidth: 420, spacing: 20);

        Assert.Equal(2, plan.ItemsPerRow);
        Assert.Equal(3, plan.RowCount);
    }

    [Fact]
    public void JustBelowExactFit_DropsToOneColumn()
    {
        var plan = FlowLayout.Plan(859, 5, minItemWidth: 420, spacing: 20);

        Assert.Equal(1, plan.ItemsPerRow);
        Assert.Equal(5, plan.RowCount);
    }

    [Fact]
    public void ChildCountNeverExceedsAvailable()
    {
        var plan = FlowLayout.Plan(4000, 4, minItemWidth: 420, spacing: 20);

        Assert.Equal(4, plan.ItemsPerRow);
        Assert.Equal(1, plan.RowCount);
    }

    [Fact]
    public void EmptyChildren_ReturnsEmptyPlan()
    {
        var plan = FlowLayout.Plan(1400, 0, minItemWidth: 420, spacing: 20);

        Assert.Equal(0, plan.ItemsPerRow);
        Assert.Equal(0, plan.RowCount);
    }

    [Fact]
    public void NonFiniteWidth_FallsBackToMinWidthSingleColumn()
    {
        var plan = FlowLayout.Plan(double.PositiveInfinity, 3, minItemWidth: 420, spacing: 20);

        Assert.Equal(1, plan.ItemsPerRow);
        Assert.Equal(3, plan.RowCount);
        Assert.Equal(420, plan.CellWidth, 3);
    }

    [Fact]
    public void WideWindow_LoneTrailingSectionIsCappedNotFullBleed()
    {
        // Optimisation at ~2200px content width, 5 sections of 560+20: 3 per row, 3+2.
        // Uncapped the last row's 2 cells would stretch to 1090 each; capped they stop at 720
        // and the row simply ends early instead of growing empty chrome.
        var plan = FlowLayout.Plan(2200, 5, minItemWidth: 560, spacing: 20);

        Assert.Equal(3, plan.ItemsPerRow);
        Assert.Equal(2, plan.RowCount);
        Assert.Equal(720, plan.CellWidth, 3);
    }

    [Fact]
    public void CapNeverSqueezesBelowMinWidth()
    {
        var plan = FlowLayout.Plan(2200, 5, minItemWidth: 560, spacing: 20, maxCellWidth: 100);

        Assert.Equal(560, plan.CellWidth, 3);
    }

    [Fact]
    public void NarrowColumn_FillBelowCapIsUnchanged()
    {
        // Single column at 500px: fill (500) is under the cap, so behaviour is identical.
        var plan = FlowLayout.Plan(500, 7, minItemWidth: 420, spacing: 20);

        Assert.Equal(1, plan.ItemsPerRow);
        Assert.Equal(500, plan.CellWidth, 3);
    }
}
