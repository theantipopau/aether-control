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

public sealed class StorageDriveInfo
{
    public string DeviceId { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public DriveHealthStatus Health { get; set; } = DriveHealthStatus.Unknown;
    public double CapacityBytes { get; set; }
    public double FreeBytes { get; set; }
    public double TemperatureCelsius { get; set; }
    public bool IsNvme { get; set; }

    public double CapacityGb => CapacityBytes / 1024 / 1024 / 1024;
    public double FreeGb => FreeBytes / 1024 / 1024 / 1024;
    public double UsedPercent => CapacityBytes <= 0 ? 0 : (CapacityBytes - FreeBytes) / CapacityBytes * 100.0;
}

public sealed class MotherboardInfo
{
    public string Model { get; set; } = string.Empty;
    public string BiosVersion { get; set; } = string.Empty;
    public IReadOnlyList<NamedSensorValue> Voltages { get; set; } = [];
    public IReadOnlyList<NamedSensorValue> FanSpeeds { get; set; } = [];
    public IReadOnlyList<NamedSensorValue> VrmTemperatures { get; set; } = [];
}

public sealed class NamedSensorValue
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
