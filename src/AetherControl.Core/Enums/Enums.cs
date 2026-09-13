namespace AetherControl.Core.Enums;

public enum ThemeMode
{
    Dark,
    Light,
    System
}

public enum AccentColor
{
    Cyan,
    Blue,
    Green,
    Amber,
    Purple
}

public enum PortraitLayoutType
{
    Compact,
    Statistical,
    Showcase
}

public enum MetricKind
{
    CpuTemperature,
    CpuPackagePower,
    CpuUtilisation,
    CpuClockSpeed,
    CpuVoltage,
    GpuTemperature,
    GpuHotspotTemperature,
    GpuUtilisation,
    GpuPowerDraw,
    GpuVramUsage,
    GpuFanSpeed,
    RamUsedBytes,
    RamAvailableBytes,
    RamUtilisationPercent,
    RamSpeedMhz,
    StorageTemperature,
    StorageUsedBytes,
    NetworkUploadKbps,
    NetworkDownloadKbps,
    NetworkLatencyMs,
    MotherboardVrmTemperature,
    MotherboardFanSpeed,
    MotherboardVoltage
}

public enum HardwareCategory
{
    Cpu,
    Gpu,
    Memory,
    Storage,
    Motherboard,
    Network
}

public enum DriveHealthStatus
{
    Unknown,
    Good,
    Caution,
    Bad
}

/// <summary>
/// A displayed metric's provenance/freshness state — separate from its value, so "the reading is
/// good" and "what the reading actually is" never get conflated into a single number the way a
/// silent fallback to 0 does. <see cref="Good"/> is the only quality that means the value should be
/// trusted at face value; every other state exists specifically so a consumer (a card, the tray, an
/// alert) can tell "this looks like 0 because it measured 0" apart from "this looks like 0 because
/// nothing measured anything" — the exact failure mode a storage temperature sensor with no real
/// SMART temperature reading silently fell into before this existed (LHM's fallback-to-zero pattern,
/// same as an unavailable fan RPM or a card with no permission to read a sensor).
/// </summary>
public enum MetricQuality
{
    /// <summary>A fresh, trustworthy reading from this poll.</summary>
    Good,

    /// <summary>The last known-good value is being shown because this poll didn't get a fresh
    /// reading (a transient failure) — the value itself may still be accurate, just not confirmed
    /// as of "now." See StorageDriveViewModel/StorageHealthProbe for the pattern this generalizes.</summary>
    Stale,

    /// <summary>No reading has ever been obtained for this metric on this hardware — distinct from
    /// Stale (which implies a real prior reading exists) and from Unsupported (which implies the
    /// hardware fundamentally can't report this at all).</summary>
    Unavailable,

    /// <summary>This hardware/driver/LHM version doesn't expose this sensor at all (e.g. this CPU's
    /// LibreHardwareMonitorLib version has no real measured-voltage sensor, only a VID target) — a
    /// permanent, not transient, absence.</summary>
    Unsupported,

    /// <summary>The sensor likely exists, but this process doesn't currently have the rights to read
    /// it (e.g. CPU package sensors when not running elevated).</summary>
    PermissionRequired,

    /// <summary>Two or more sources disagree on this value (e.g. DriveInfo vs. WMI vs. a physical
    /// disk aggregate) and neither was silently preferred or averaged — see the storage audit's
    /// "do not silently average" requirement.</summary>
    Conflicting,

    /// <summary>The device this metric belongs to was present and reporting, then stopped (e.g. a
    /// removable drive unplugged, a mapped network drive dropped) — distinct from Unavailable, which
    /// never had a reading to lose.</summary>
    Disconnected,

    /// <summary>The underlying read threw or otherwise failed in a way that isn't one of the more
    /// specific states above.</summary>
    Error
}

public enum OptimisationCategory
{
    Memory,
    Startup,
    Cleanup,
    Gaming
}

public enum RgbBackend
{
    OpenRgb,
    AuraSync,
    CorsairDirect
}

public enum HistoryResolution
{
    Raw,
    Daily,
    Weekly,
    Monthly
}
