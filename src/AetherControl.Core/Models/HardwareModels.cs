using AetherControl.Core.Enums;

namespace AetherControl.Core.Models;

public sealed class CpuInfo
{
    public string Name { get; set; } = string.Empty;
    public double TemperatureCelsius { get; set; }
    public double PackagePowerWatts { get; set; }
    public double UtilisationPercent { get; set; }
    public double ClockSpeedMhz { get; set; }
    public double CoreVoltage { get; set; }
    public IReadOnlyList<CpuCoreInfo> Cores { get; set; } = [];
}

public sealed class CpuCoreInfo
{
    public int Index { get; set; }
    public double TemperatureCelsius { get; set; }
    public double ClockSpeedMhz { get; set; }
    public double LoadPercent { get; set; }
}

public sealed class GpuInfo
{
    public string Name { get; set; } = string.Empty;
    public double TemperatureCelsius { get; set; }
    public double HotspotTemperatureCelsius { get; set; }
    public double UtilisationPercent { get; set; }
    public double PowerDrawWatts { get; set; }
    public double VramUsedMb { get; set; }
    public double VramTotalMb { get; set; }
    public double FanSpeedPercent { get; set; }
    public double FanSpeedRpm { get; set; }
    public double CoreClockMhz { get; set; }
    public double MemoryClockMhz { get; set; }
}

public sealed class MemoryInfo
{
    public double UsedBytes { get; set; }
    public double AvailableBytes { get; set; }
    public double TotalBytes { get; set; }
    public double UtilisationPercent => TotalBytes <= 0 ? 0 : UsedBytes / TotalBytes * 100.0;
    public double SpeedMhz { get; set; }
}

// A record, not a class — the root cause of a real, live-captured storage display bug (a card
// visibly sweeping from 0 to its target on *every single poll*, forever) turned out to be this type
// lacking value equality. ObservableCollectionMergeExtensions.MergeFrom replaces the object at an
// index whenever the incoming reading "differs" from what's there — but every StorageHealthProbe
// poll constructs a brand-new StorageDriveInfo even when every field is identical, and reference
// equality made that look like a real change every time. ItemsControl (with a VariableSizedWrapGrid
// ItemsPanel, no virtualization) responds to that Replace by tearing down and recreating the
// MetricCard container from scratch — proven via a live ui-value-trace.log capture: a brand new
// MetricCard GUID appeared every ~1 second, each one re-animating from 0. Records get value-based
// Equals/GetHashCode for free while keeping the same { get; set; } mutable-property shape every
// existing caller already uses.
public sealed record StorageDriveInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DriveHealthStatus Health { get; set; } = DriveHealthStatus.Unknown;
    public double CapacityBytes { get; set; }
    public double FreeBytes { get; set; }
    public double TemperatureCelsius { get; set; }
    public bool IsNvme { get; set; }

    // True when this poll couldn't get a fresh free-space reading (a transient WMI failure, or the
    // disk-to-partition association graph didn't resolve this drive that cycle) and FreeBytes/
    // CapacityBytes are carried over from the last successful poll rather than zeroed — a missing
    // reading is not the same fact as "0 bytes free" and must never be displayed as one.
    public bool IsFreeSpaceStale { get; set; }

    public double CapacityGb => CapacityBytes / 1024 / 1024 / 1024;
    public double FreeGb => FreeBytes / 1024 / 1024 / 1024;

    // Clamped, not just computed — FreeBytes can transiently exceed CapacityBytes for a real, non-
    // buggy reason (CapacityBytes and FreeBytes are independent readings from independent WMI
    // queries taken microseconds apart; the stale-fallback path can also carry over a last-good
    // FreeBytes alongside a freshly-read, slightly different CapacityBytes). Without a floor this
    // produced a literal negative "used%" (caught by a regression test, not observed live) — a
    // disk cannot have negative used space, so this states that fact once, here, rather than
    // needing every consumer (a meter bar, a converter, a future one) to remember to clamp it too.
    public double UsedPercent => CapacityBytes <= 0 ? 0 : Math.Clamp((CapacityBytes - FreeBytes) / CapacityBytes * 100.0, 0.0, 100.0);

    // Custom equality, overriding the record default — evidenced necessary by a live trace: the
    // system drive is under real, constant write activity (browser cache, temp files, logs), so its
    // exact FreeBytes differs by a few KB on nearly every single poll. Byte-exact equality (what the
    // compiler-generated record Equals would do) still called that "different" every time, still
    // triggered a container rebuild via ObservableCollectionMergeExtensions.MergeFrom, and the
    // rebuilt container's value animation still started from zero — for exactly the drive the
    // reported symptom was about. Comparing at display precision (whole GB/°C, matching this card's
    // own "F0" format) instead of raw bytes is what actually stops the every-second rebuild for a
    // drive with real, continuous, sub-perceptible activity, without hiding a change large enough to
    // actually move the displayed number.
    public bool Equals(StorageDriveInfo? other) =>
        other is not null
        && DeviceId == other.DeviceId
        && Model == other.Model
        && Health == other.Health
        && IsNvme == other.IsNvme
        && IsFreeSpaceStale == other.IsFreeSpaceStale
        && Math.Round(FreeGb) == Math.Round(other.FreeGb)
        && Math.Round(CapacityGb) == Math.Round(other.CapacityGb)
        && Math.Round(TemperatureCelsius) == Math.Round(other.TemperatureCelsius);

    public override int GetHashCode() =>
        HashCode.Combine(DeviceId, Model, Health, IsNvme, IsFreeSpaceStale, Math.Round(FreeGb), Math.Round(CapacityGb), Math.Round(TemperatureCelsius));
}

public sealed class MotherboardInfo
{
    public string Model { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public IReadOnlyList<NamedSensorValue> Voltages { get; set; } = [];
    public IReadOnlyList<NamedSensorValue> FanSpeeds { get; set; } = [];
    public IReadOnlyList<NamedSensorValue> VrmTemperatures { get; set; } = [];
}

// Record for the same reason as StorageDriveInfo above — this backs the Motherboard Voltages/Fan
// Speeds/VRM Temperatures ItemsControls, which have the identical "recreated every poll" exposure.
public sealed record NamedSensorValue
{
    public string Name { get; set; } = string.Empty;
    public double Value { get; set; }
    public string Unit { get; set; } = string.Empty;
}

public sealed class NetworkInfo
{
    public string AdapterName { get; set; } = string.Empty;
    public double UploadKbps { get; set; }
    public double DownloadKbps { get; set; }
    public double LatencyMs { get; set; }
    public string ExternalIpAddress { get; set; } = string.Empty;
}

public sealed class HardwareSnapshot
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public CpuInfo Cpu { get; set; } = new();
    public GpuInfo Gpu { get; set; } = new();
    public MemoryInfo Memory { get; set; } = new();
    public IReadOnlyList<StorageDriveInfo> Drives { get; set; } = [];
    public MotherboardInfo Motherboard { get; set; } = new();
    public NetworkInfo Network { get; set; } = new();
}
