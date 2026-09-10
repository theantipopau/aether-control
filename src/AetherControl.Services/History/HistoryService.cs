using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Data.Repositories;

namespace AetherControl.Services.History;

public sealed class HistoryService(HistoryRepository historyRepository) : IHistoryService
{
    public Task RecordAsync(HardwareSnapshot snapshot, CancellationToken ct = default)
    {
        var timestamp = snapshot.TimestampUtc;
        var samples = new List<SensorSample>
        {
            Sample(MetricKind.CpuTemperature, snapshot.Cpu.TemperatureCelsius, timestamp),
            Sample(MetricKind.CpuPackagePower, snapshot.Cpu.PackagePowerWatts, timestamp),
            Sample(MetricKind.CpuUtilisation, snapshot.Cpu.UtilisationPercent, timestamp),
            Sample(MetricKind.CpuClockSpeed, snapshot.Cpu.ClockSpeedMhz, timestamp),
            Sample(MetricKind.CpuVoltage, snapshot.Cpu.CoreVoltage, timestamp),
            Sample(MetricKind.GpuTemperature, snapshot.Gpu.TemperatureCelsius, timestamp),
            Sample(MetricKind.GpuHotspotTemperature, snapshot.Gpu.HotspotTemperatureCelsius, timestamp),
            Sample(MetricKind.GpuUtilisation, snapshot.Gpu.UtilisationPercent, timestamp),
            Sample(MetricKind.GpuPowerDraw, snapshot.Gpu.PowerDrawWatts, timestamp),
            Sample(MetricKind.GpuVramUsage, snapshot.Gpu.VramUsedMb, timestamp),
            Sample(MetricKind.RamUsedBytes, snapshot.Memory.UsedBytes, timestamp),
            Sample(MetricKind.RamUtilisationPercent, snapshot.Memory.UtilisationPercent, timestamp),
            Sample(MetricKind.NetworkUploadKbps, snapshot.Network.UploadKbps, timestamp),
            Sample(MetricKind.NetworkDownloadKbps, snapshot.Network.DownloadKbps, timestamp),
            Sample(MetricKind.NetworkLatencyMs, snapshot.Network.LatencyMs, timestamp)
        };

        return historyRepository.InsertAsync(samples, ct);
    }

    public Task<IReadOnlyList<SensorSample>> QueryAsync(
        MetricKind metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        HistoryResolution resolution,
        CancellationToken ct = default)
        => historyRepository.QueryAsync(metric, fromUtc, toUtc, resolution, ct);

    public Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default)
        => historyRepository.PurgeOlderThanAsync(DateTimeOffset.UtcNow - age, ct);

    private static SensorSample Sample(MetricKind metric, double value, DateTimeOffset timestamp) => new()
    {
        Metric = metric,
        Value = value,
        TimestampUtc = timestamp
    };
}
