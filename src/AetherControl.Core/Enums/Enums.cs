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
