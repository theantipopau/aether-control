using AetherControl.Core.Enums;
using AetherControl.Core.Models;

namespace AetherControl.Services.Tray;

/// <summary>Formats the enabled tray metrics from a snapshot into a single compact line (e.g. "CPU 54°C · GPU 48°C · RAM 37%").</summary>
public static class TrayReadoutFormatter
{
    public static string Format(HardwareSnapshot snapshot, IEnumerable<TrayMetricPreference> preferences)
    {
        var parts = preferences
            .Where(p => p.Enabled)
            .OrderBy(p => p.Order)
            .Select(p => FormatMetric(p.Metric, snapshot))
            .Where(s => s is not null);

        return string.Join("  ·  ", parts);
    }

    private static string? FormatMetric(MetricKind metric, HardwareSnapshot snapshot) => metric switch
    {
        MetricKind.CpuTemperature => $"CPU {snapshot.Cpu.TemperatureCelsius:F0}°C",
        MetricKind.CpuUtilisation => $"CPU {snapshot.Cpu.UtilisationPercent:F0}%",
        MetricKind.GpuTemperature => $"GPU {snapshot.Gpu.TemperatureCelsius:F0}°C",
        MetricKind.GpuUtilisation => $"GPU {snapshot.Gpu.UtilisationPercent:F0}%",
        MetricKind.RamUtilisationPercent => $"RAM {snapshot.Memory.UtilisationPercent:F0}%",
        MetricKind.NetworkDownloadKbps => $"NET {snapshot.Network.DownloadKbps / 1000:F0}Mbps",
        MetricKind.NetworkUploadKbps => $"UP {snapshot.Network.UploadKbps / 1000:F0}Mbps",
        _ => null
    };
}
