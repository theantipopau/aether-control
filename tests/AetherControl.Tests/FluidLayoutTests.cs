using AetherControl.Core.Layout;

namespace AetherControl.Tests;

/// <summary>
/// Pins the fluid row plan behind "not scaling correctly" (2026-10-02): rows must fill their
/// container edge to edge, never orphan a single child into its own row when any alternative
/// exists, and never blow a cell past the stretch cap.
/// </summary>
public class FluidLayoutTests
{
    [Theory]
    [InlineData(1124, 5, 224)] // wide enough: one row, cells stretch to fill (was 187 + a 199px tail)
    [InlineData(1124, 4, 281)]
    [InlineData(1124, 6, 187)]
    [InlineData(795, 4, 198)]
    public void Plan_WideEnoughForAllItems_FillsSingleRow(double width, int count, double expectedCell)
    {
        var plan = FluidLayout.Plan(width, count);

        Assert.Single(plan.ItemsPerRow);
        Assert.Equal(count, plan.TotalItems);
        Assert.Equal(expectedCell, plan.CellWidths[0]);
    }

    [Fact]
    public void Plan_SlightlyBelowMinimumCell_StillSingleRow_NoLoneCard()
    {
        // The reported bug: 5 cards in a 795px column wrapped 4 + a lone VOLTAGE card.
        // 795/5 = 159 ≥ SoftMinCellWidth, so all five share the row instead.
        var plan = FluidLayout.Plan(795, 5);

        Assert.Single(plan.ItemsPerRow);
        Assert.Equal(5, plan.ItemsPerRow[0]);
        Assert.Equal(159, plan.CellWidths[0]);
    }

    [Theory]
    [InlineData(795, 6)] // 3 + 3, not 4 + 2 (a 397px giant row) — exact division wins
    [InlineData(795, 9)] // 3 + 3 + 3, not 4 + 4 + 1
    [InlineData(795, 10)] // 4 + 4 + 2: no lone child, cells within cap
    [InlineData(1124, 13)] // 5 + 5 + 3: primes can't divide, lone child still avoided
    public void Plan_MultiRow_NeverLeavesALoneChildRow(double width, int count)
    {
        var plan = FluidLayout.Plan(width, count);

        Assert.True(plan.ItemsPerRow.Count > 1);
        Assert.Equal(count, plan.TotalItems);
        Assert.All(plan.ItemsPerRow, items => Assert.True(items >= 2));
    }

    [Fact]
    public void Plan_SingleItemInWideColumn_CapsStretchInsteadOfGoingFullBleed()
    {
        var plan = FluidLayout.Plan(1124, 1);

        Assert.Single(plan.ItemsPerRow);
        Assert.Equal(FluidLayout.MaxCellWidth, plan.CellWidths[0]);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(500, 0)]
    [InlineData(-100, 5)]
    public void Plan_InvalidInputs_ReturnEmpty(double width, int count)
    {
        Assert.True(FluidLayout.Plan(width, count).IsEmpty);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(600)]
    [InlineData(795)]
    [InlineData(1000)]
    [InlineData(1124)]
    [InlineData(1500)]
    public void Plan_AnyWidthAndCount_RowsFitWithinTheContainer(double width)
    {
        for (var count = 1; count <= 14; count++)
        {
            var plan = FluidLayout.Plan(width, count);
            Assert.Equal(count, plan.TotalItems);
            for (var row = 0; row < plan.ItemsPerRow.Count; row++)
            {
                var rowWidth = plan.ItemsPerRow[row] * plan.CellWidths[row];
                Assert.True(rowWidth <= width,
                    $"{count} items at {width}px: row {row} needs {rowWidth}px");
            }
        }
    }

    [Fact]
    public void Plan_TinyContainer_StillFitsEveryChild()
    {
        var plan = FluidLayout.Plan(300, 5);

        Assert.Single(plan.ItemsPerRow);
        Assert.True(5 * plan.CellWidths[0] <= 300);
    }
}
