using AetherControl.Core.Fans;

namespace AetherControl.Tests;

public sealed class FanCurveTests
{
    private static FanCurve TestCurve() => new("Test",
    [
        new FanCurvePoint(40, 20),
        new FanCurvePoint(60, 50),
        new FanCurvePoint(80, 100)
    ]);

    [Fact]
    public void Evaluate_BelowFirstKnot_ClampsToFirst()
    {
        Assert.Equal(20, TestCurve().Evaluate(0));
        Assert.Equal(20, TestCurve().Evaluate(40));
    }

    [Fact]
    public void Evaluate_AboveLastKnot_ClampsToLast()
    {
        Assert.Equal(100, TestCurve().Evaluate(80));
        Assert.Equal(100, TestCurve().Evaluate(120));
    }

    [Fact]
    public void Evaluate_Midpoint_Interpolates()
    {
        // Halfway between (40,20) and (60,50) → 35.
        Assert.Equal(35, TestCurve().Evaluate(50));
    }

    [Fact]
    public void Evaluate_ExactKnot_ReturnsKnotPercent()
    {
        Assert.Equal(50, TestCurve().Evaluate(60));
    }

    [Fact]
    public void Constructor_UnsortedInput_IsSorted()
    {
        var curve = new FanCurve("Unsorted",
        [
            new FanCurvePoint(80, 100),
            new FanCurvePoint(40, 20)
        ]);

        Assert.Equal(20, curve.Evaluate(30));
        Assert.Equal(100, curve.Evaluate(90));
    }

    [Fact]
    public void Constructor_SinglePoint_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FanCurve("One", [new FanCurvePoint(50, 50)]));
    }

    [Fact]
    public void Constructor_DuplicateTemperatures_Throws()
    {
        Assert.Throws<ArgumentException>(() => new FanCurve("Dup",
        [
            new FanCurvePoint(50, 20),
            new FanCurvePoint(50, 80)
        ]));
    }

    [Fact]
    public void Constructor_PercentOutOfRange_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FanCurve("High",
        [
            new FanCurvePoint(40, 20),
            new FanCurvePoint(60, 101)
        ]));
    }

    [Fact]
    public void Presets_AllFloorsAtSafetyFloor_AndTopOutAtFull()
    {
        foreach (var preset in new[] { FanCurvePresets.Silent, FanCurvePresets.Balanced, FanCurvePresets.Performance })
        {
            Assert.True(preset.Evaluate(-50) >= 20);
            Assert.Equal(100, preset.Evaluate(200));
        }
    }

    [Fact]
    public void Presets_SilentIsCoolest_PerformanceIsMostAggressive()
    {
        // At the same mid-range temperature, Silent < Balanced < Performance.
        Assert.True(FanCurvePresets.Silent.Evaluate(70) < FanCurvePresets.Balanced.Evaluate(70));
        Assert.True(FanCurvePresets.Balanced.Evaluate(70) < FanCurvePresets.Performance.Evaluate(70));
    }

    [Theory]
    [InlineData(50, 50, false)] // identical — never rewrite
    [InlineData(54, 50, false)] // 4pp wobble around a knot — the hunting case, suppressed
    [InlineData(55, 50, true)] // 5pp step — goes through
    [InlineData(30, 50, true)] // large drop — goes through
    public void ShouldApply_GatesOnMeaningfulSteps(int target, int lastApplied, bool expected)
    {
        Assert.Equal(expected, FanCurveApplication.ShouldApply(target, lastApplied));
    }
}
