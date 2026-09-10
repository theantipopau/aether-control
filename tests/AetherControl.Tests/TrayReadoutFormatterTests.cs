using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using AetherControl.Services.Tray;

namespace AetherControl.Tests;

public class TrayReadoutFormatterTests
{
    [Fact]
    public void Format_OnlyIncludesEnabledMetrics_InOrder()
    {
        var snapshot = new HardwareSnapshot
        {
            Cpu = new CpuInfo { TemperatureCelsius = 54.4 },
            Gpu = new GpuInfo { TemperatureCelsius = 48.2 },
            Memory = new MemoryInfo { UsedBytes = 8_000_000_000, TotalBytes = 16_000_000_000 }
        };

        var preferences = new List<TrayMetricPreference>
        {
            new() { Metric = MetricKind.GpuTemperature, Enabled = true, Order = 1 },
            new() { Metric = MetricKind.CpuTemperature, Enabled = true, Order = 0 },
            new() { Metric = MetricKind.RamUtilisationPercent, Enabled = false, Order = 2 }
        };

        var result = TrayReadoutFormatter.Format(snapshot, preferences);

        Assert.Equal("CPU 54°C  ·  GPU 48°C", result);
    }

    [Fact]
    public void Format_NoEnabledMetrics_ReturnsEmptyString()
    {
        var snapshot = new HardwareSnapshot();
        var result = TrayReadoutFormatter.Format(snapshot, []);
        Assert.Equal(string.Empty, result);
    }
}
