using AetherControl.Core.Enums;
using AetherControl.Core.Models;

namespace AetherControl.Core.Interfaces;

public interface ISettingsService
{
    Task InitializeAsync(CancellationToken ct = default);

    AppSettings Current { get; }

    Task SaveAsync(AppSettings settings, CancellationToken ct = default);

    Task<IReadOnlyList<TrayMetricPreference>> GetTrayMetricPreferencesAsync(CancellationToken ct = default);

    Task SaveTrayMetricPreferencesAsync(IReadOnlyList<TrayMetricPreference> preferences, CancellationToken ct = default);

    Task<string> ExportConfigurationAsync(CancellationToken ct = default);

    Task ImportConfigurationAsync(string json, CancellationToken ct = default);

    event EventHandler<AppSettings>? SettingsChanged;
}

public interface IHistoryService
{
    Task RecordAsync(HardwareSnapshot snapshot, CancellationToken ct = default);

    Task<IReadOnlyList<SensorSample>> QueryAsync(
        MetricKind metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        HistoryResolution resolution,
        CancellationToken ct = default);

    Task PurgeOlderThanAsync(TimeSpan age, CancellationToken ct = default);
}

public interface INetworkMonitorService : IDisposable
{
    NetworkInfo? Latest { get; }

    event EventHandler<NetworkInfo>? Updated;

    void Start(TimeSpan interval);

    void Stop();
}

public interface IOptimisationService
{
    IReadOnlyList<OptimisationTaskDescriptor> GetAvailableTasks();

    Task<OptimisationResult> RunAsync(string taskId, bool createRestorePoint, CancellationToken ct = default);

    Task<IReadOnlyList<StartupEntry>> GetStartupEntriesAsync(CancellationToken ct = default);

    Task SetStartupEntryEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct = default);

    Task<OptimisationResult> ApplyGamingProfileAsync(bool enable, CancellationToken ct = default);

    /// <summary>Reports each known cleanup location's actual on-disk size rather than deleting
    /// blind — replaces a one-click "clean everything" task with an inspectable, selectable one.</summary>
    Task<IReadOnlyList<CleanupLocationPreview>> ScanCleanupLocationsAsync(CancellationToken ct = default);

    Task<OptimisationResult> CleanLocationsAsync(IReadOnlyList<string> paths, CancellationToken ct = default);
}

public interface IRgbService
{
    Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync(CancellationToken ct = default);

    Task SetColorAsync(string deviceId, byte r, byte g, byte b, CancellationToken ct = default);

    Task SetBrightnessAsync(string deviceId, int percent, CancellationToken ct = default);

    Task SetEffectAsync(string deviceId, string effectName, CancellationToken ct = default);

    bool IsAuraSyncInstalled();

    void LaunchAuraSync();
}

public interface IFirmwareDriverService
{
    Task<IReadOnlyList<DriverStatus>> GetStatusAsync(CancellationToken ct = default);
}

public interface IPeripheralDetectionService
{
    IReadOnlyList<DetectedPeripheralInfo> Detect();
}

/// <summary>Ported from Portrait Stats — reads live FPS from RTSS (RivaTuner Statistics Server)
/// shared memory, the same mechanism MSI Afterburner's OSD/CapFrameX/Special K all use.</summary>
public interface IFpsSource
{
    bool IsConnected { get; }
    double? CurrentFps { get; }
    string? TargetProcessName { get; }
    void Refresh();
}

/// <summary>
/// Detects tracked game executables launching/exiting and applies/reverts the existing
/// <see cref="IOptimisationService.ApplyGamingProfileAsync"/> automatically — the "launch a game,
/// performance mode kicks in, close it, everything reverts" pattern every competitor gaming suite
/// has. Deliberately scoped to what Aether Control actually controls today (Gaming Profile) rather
/// than a laptop-specific feature set (GPU mux switching, per-game undervolt offsets, keyboard
/// lighting profiles) that doesn't apply to a custom desktop.
/// </summary>
public interface IGameProfileService : IDisposable
{
    void Start();

    IReadOnlyList<GameProfile> GetProfiles();

    Task AddProfileAsync(string name, string executableName, CancellationToken ct = default);

    Task RemoveProfileAsync(string id, CancellationToken ct = default);

    Task SetEnabledAsync(string id, bool enabled, CancellationToken ct = default);

    /// <summary>Fires after a profile is added/removed/toggled, or a tracked game's running state
    /// changes — from a background thread, so subscribers must marshal to the UI thread themselves.</summary>
    event EventHandler? ProfilesChanged;
}

public interface IFanControlService
{
    /// <summary>Best-effort check for vendor fan software (e.g. ASUS Armoury Crate) that would
    /// fight over the same Super I/O ports — the UI should warn before a custom curve is applied.</summary>
    bool IsConflictingVendorSoftwareRunning();

    /// <summary>False while a second process (previously <c>AetherControl.FanHelper.exe</c>) could
    /// still be sharing Super I/O access with this session — confirmed by live A/B testing (2026-09-23)
    /// that a second reader/writer on the same Nuvoton chip makes BOTH sides read back 0xFF (fans 0,
    /// voltages pinned at 2.04V/4.08V) persistently, not just transiently. Writing a PWM duty cycle
    /// while readback is scrambled risks writing to the wrong register. The UI must disable manual
    /// control while this is false rather than let a write through silently.</summary>
    bool IsSoftwareControlSafe { get; }

    IReadOnlyList<FanControlChannel> GetChannels();

    /// <summary>Applies a manual duty cycle to one channel. Implementations clamp away from 0% —
    /// software fan control has no thermal-protection fallback if the app crashes mid-curve. No-op
    /// (does not throw) when <see cref="IsSoftwareControlSafe"/> is false.</summary>
    void SetPercent(string channelId, int percent);

    void ResetToAutomatic(string channelId);

    void ResetAllToAutomatic();
}

public interface ITrayService : IDisposable
{
    void Initialize();

    void UpdateReadout(HardwareSnapshot snapshot);

    void Show();

    void Hide();
}
