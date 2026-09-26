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

    // Was a plain bool (IsFreeSpaceStale) — generalized to the shared MetricQuality model (Phase 33)
    // so "no fresh reading this poll, but a real prior value exists" (Stale) and "never obtained a
    // reading for this disk at all" (Unavailable) are distinct facts instead of both being "stale".
    // Default Good, not because every caller verifies freshness, but because the two places that
    // actually determine this (StorageHealthProbe.QueryPhysicalDisks/Enrich) always set it
    // explicitly — a default of Good only matters for test/ad-hoc construction, where "trust it
    // unless told otherwise" is the reasonable assumption.
    public MetricQuality FreeSpaceQuality { get; set; } = MetricQuality.Good;

    // A drive with no real SMART temperature sensor (or one this poll couldn't reach) used to
    // silently report 0°C via HardwareSnapshotMapper's fallback-to-zero pattern — indistinguishable
    // from a genuinely-measured 0°C. Set by HardwareSnapshotMapper.MapStorage based on whether an
    // actual sensor was found, not inferred from the value itself.
    public MetricQuality TemperatureQuality { get; set; } = MetricQuality.Good;

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

    // Deliberately plain, byte-exact record equality (no override) — a prior version of this class
    // overrode Equals to compare at display precision (whole GB) specifically to stop
    // ObservableCollectionMergeExtensions.MergeFrom from recreating a storage card whenever the
    // system drive's real, continuous write activity changed its exact FreeBytes by a few KB. That
    // was domain-model equality shaped around a UI-layer/formatting concern, and it would have kept
    // needing a new exclusion for the next volatile field someone added (activity, read/write rate,
    // ...). The real fix is architectural, not an equality tweak: Dashboard/PortraitViewModel now
    // hold a long-lived StorageDriveViewModel per DeviceId (LiveCollectionSync,
    // AetherControl.Core.Collections) and call its Apply(StorageDriveInfo) every poll, which sets
    // each field directly — CommunityToolkit's generated property setters already raise
    // PropertyChanged only when a field actually changed, per field, with no need to compare whole
    // snapshot objects at all. This type stays a plain immutable, byte-precise snapshot exactly as
    // StorageHealthProbe produces it every poll.
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

    /// <summary>When <see cref="Motherboard"/> (voltages/fans/VRM temps) was actually read from the
    /// Super I/O chip. Lags <see cref="TimestampUtc"/> whenever a poisoned read was replaced by the
    /// last known-good reading — UI should present that as stale, not live.</summary>
    public DateTimeOffset MotherboardReadUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Session-cumulative Super I/O poll counters, so the poison rate is visible without
    /// digging through <c>superio-reopen.log</c>.</summary>
    public long SuperIoPolls { get; set; }
    public long SuperIoPoisonedPolls { get; set; }

    public TimeSpan MotherboardAge => TimestampUtc - MotherboardReadUtc;
}
