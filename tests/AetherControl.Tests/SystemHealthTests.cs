using AetherControl.Core.Health;

namespace AetherControl.Tests;

public class SystemHealthTests
{
    [Fact]
    public void NoReadings_IsUnknown_NotNormal()
    {
        var result = SystemHealth.Evaluate(0, 0, 50, motherboardStale: false);
        Assert.Equal(HealthLevel.Unknown, result.Level);
    }

    [Fact]
    public void AllBelowThresholds_IsNormal()
    {
        var result = SystemHealth.Evaluate(61, 45, 60, motherboardStale: false);
        Assert.Equal(HealthLevel.Normal, result.Level);
        Assert.DoesNotContain("stale", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WorstReadingWins_AndIsListedFirst()
    {
        var result = SystemHealth.Evaluate(90, 76, 60, motherboardStale: false);
        Assert.Equal(HealthLevel.Critical, result.Level);
        Assert.StartsWith("CPU at 90", result.Detail);
        Assert.Contains("GPU at 76", result.Detail);
    }

    [Fact]
    public void HighRam_IsWarning()
    {
        // Matches the screenshot case: RAM 77% is fine, 85% is a warning.
        Assert.Equal(HealthLevel.Normal, SystemHealth.Evaluate(61, 45, 77, false).Level);
        Assert.Equal(HealthLevel.Warning, SystemHealth.Evaluate(61, 45, 85, false).Level);
    }

    [Fact]
    public void StaleMotherboard_IsMentioned_WithoutChangingLevel()
    {
        var result = SystemHealth.Evaluate(61, 45, 60, motherboardStale: true);
        Assert.Equal(HealthLevel.Normal, result.Level);
        Assert.Contains("stale", result.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingGpuTemp_StillEvaluatesCpu()
    {
        Assert.Equal(HealthLevel.Warning, SystemHealth.Evaluate(80, 0, 40, false).Level);
    }
}
