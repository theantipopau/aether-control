using AetherControl.Core.Enums;

namespace AetherControl.Core.Models;

public sealed class PortraitLayoutDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public PortraitLayoutType Type { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool TransparentBackground { get; set; }
    public bool OledFriendly { get; set; }
    public int RefreshRateMs { get; set; } = 1000;
    public string WidgetLayoutJson { get; set; } = "[]";
    public bool IsBuiltIn { get; set; }
}

public sealed class RgbDeviceInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public RgbBackend Backend { get; set; }
    public string DeviceType { get; set; } = string.Empty;
    public int LedCount { get; set; }
    public IReadOnlyList<string> SupportedModes { get; set; } = [];
}

public sealed class DriverStatus
{
    public string Component { get; set; } = string.Empty;
    public string CurrentVersion { get; set; } = string.Empty;
    public string? RecommendedVersion { get; set; }
    public string? VendorPageUrl { get; set; }
    public bool UpdateAvailable => !string.IsNullOrEmpty(RecommendedVersion)
        && !string.Equals(RecommendedVersion, CurrentVersion, StringComparison.OrdinalIgnoreCase);
}

public sealed class OptimisationTaskDescriptor
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public OptimisationCategory Category { get; set; }
    public bool RequiresElevation { get; set; }
    public bool IsReversible { get; set; } = true;
}

public sealed class OptimisationResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public long? BytesReclaimed { get; set; }
    public string? RestorePointDescription { get; set; }
    public double? MemoryBeforeGb { get; set; }
    public double? MemoryAfterGb { get; set; }
    public int? ProcessesScanned { get; set; }
    public int? ProcessesTrimmed { get; set; }
}

public sealed class StartupEntry
{
    public string Name { get; set; } = string.Empty;
    public string Command { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Publisher { get; set; } = string.Empty;
    public string ImpactEstimate { get; set; } = "Unknown";
    public bool IsEnabled { get; set; } = true;
}

public sealed class SensorSample
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; }
    public MetricKind Metric { get; set; }
    public double Value { get; set; }
}

public sealed class TrayMetricPreference
{
    public MetricKind Metric { get; set; }
    public bool Enabled { get; set; }
    public int Order { get; set; }
}

public sealed class GameProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ExecutableName { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool IsRunning { get; set; }
}

// Record for the same reason as StorageDriveInfo (AetherControl.Core.Models.HardwareModels) — backs
// the Top Processes ItemsControls, same "recreated every poll" exposure via
// ObservableCollectionMergeExtensions.MergeFrom.
public sealed record ProcessUsageInfo
{
    public int Pid { get; init; }
    public string Name { get; init; } = string.Empty;
    public double CpuPercent { get; init; }
    public double GpuPercent { get; init; }
}

public sealed class CleanupLocationPreview
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public bool SafeToDeleteWhileRunning { get; set; }
}

public sealed class FanControlChannel
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double CurrentPercent { get; set; }
    public bool IsSoftwareControlled { get; set; }
}

public sealed class DetectedPeripheralInfo
{
    public string VendorName { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int VendorId { get; set; }
    public int ProductId { get; set; }
    public string DeviceKind { get; set; } = string.Empty;
    public string IdLabel => $"VID_{VendorId:X4} · PID_{ProductId:X4}";
}

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Dark;
    public AccentColor Accent { get; set; } = AccentColor.Cyan;
    public int DashboardRefreshMs { get; set; } = 1000;
    public bool StartWithWindows { get; set; }
    public bool StartMinimisedToTray { get; set; } = true;
    public bool MinimiseToTrayOnClose { get; set; } = true;
    public int HistoryRetentionDays { get; set; } = 90;
    public bool LoggingEnabled { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}
