using System.Collections.ObjectModel;
using AetherControl.Core.Collections;
using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using AetherControl.Services.Processes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Backs Portrait Mode's real layout (ported from Portrait Stats' own <c>MainWindow</c>) with
/// Aether Control's existing hardware/process/FPS services. Deliberately a separate ViewModel
/// from <see cref="DashboardViewModel"/> rather than a reuse — Portrait Mode's shape (history
/// buffers for sparklines, per-metric severity, vendor badges, a clock) doesn't overlap enough
/// with the dashboard's own card-grid shape to share one class cleanly.
/// </summary>
public sealed partial class PortraitViewModel : ObservableObject, IDisposable
{
    private const int HistoryCapacity = 60;
    private static readonly TimeSpan TickerInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ProcessRankingInterval = TimeSpan.FromSeconds(2);

    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly ProcessRankerService _processRanker;
    private readonly IFpsSource _fpsSource;
    private readonly FanLabelStore _fanLabelStore;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Timer _tickerTimer;
    private readonly Timer _processRankingTimer;
    private readonly PortraitHistory _cpuHistory = new(HistoryCapacity);
    private readonly PortraitHistory _gpuHistory = new(HistoryCapacity);

    [ObservableProperty] private string currentTimeText = "--:--";
    [ObservableProperty] private string currentDateText = "—";

    [ObservableProperty] private string cpuName = "—";
    [ObservableProperty] private PortraitVendor cpuVendor;
    [ObservableProperty] private string cpuUsageText = "--";
    [ObservableProperty] private double cpuUsagePercent;
    [ObservableProperty] private IReadOnlyList<double> cpuUsageHistory = [];
    [ObservableProperty] private string cpuTempText = "--";
    [ObservableProperty] private PortraitSeverity cpuTempSeverity;
    [ObservableProperty] private string cpuClockText = "--";
    [ObservableProperty] private string cpuPowerText = "--";

    [ObservableProperty] private string gpuName = "—";
    [ObservableProperty] private PortraitVendor gpuVendor;
    [ObservableProperty] private string gpuUsageText = "--";
    [ObservableProperty] private double gpuUsagePercent;
    [ObservableProperty] private IReadOnlyList<double> gpuUsageHistory = [];
    [ObservableProperty] private string gpuTempText = "--";
    [ObservableProperty] private PortraitSeverity gpuTempSeverity;
    [ObservableProperty] private string gpuHotspotText = "--";
    [ObservableProperty] private PortraitSeverity gpuHotspotSeverity;
    [ObservableProperty] private string gpuFanText = "--";
    [ObservableProperty] private string gpuWattageText = "--";
    [ObservableProperty] private string gpuCoreClockText = "--";
    [ObservableProperty] private string gpuMemClockText = "--";
    [ObservableProperty] private string vramUsedText = "--";
    [ObservableProperty] private double vramPercent;

    [ObservableProperty] private string ramUsedText = "--";
    [ObservableProperty] private double ramPercent;
    [ObservableProperty] private PortraitSeverity ramSeverity;

    [ObservableProperty] private string driveTempText = "--";
    [ObservableProperty] private PortraitSeverity driveTempSeverity;

    [ObservableProperty] private string fpsText = "--";

    // Persistent, mutated in place (see ObservableCollectionMergeExtensions) — reassigning the
    // reference every poll (as these were before) forces the bound ItemsControl to recreate every
    // row from scratch each time, which for Fans meant a mid-rename TextBox got its in-progress
    // edit wiped roughly every second.
    public ObservableCollection<PortraitFanRow> Fans { get; } = [];

    // Long-lived per-process view models (see StorageDriveViewModel's doc comment for why) — a
    // process that stays in the ranking across polls has a CpuPercent/GpuPercent that legitimately
    // jitters by fractions of a percent almost every poll, which made plain-record MergeFrom
    // recreate its row every time.
    private readonly LiveCollectionSync<ProcessUsageViewModel, ProcessUsageInfo, int> _topCpuProcessesSync =
        new(snapshotKey: p => p.Pid, viewModelKey: vm => vm.Pid, create: s => new ProcessUsageViewModel(s), apply: (vm, s) => vm.Apply(s));

    private readonly LiveCollectionSync<ProcessUsageViewModel, ProcessUsageInfo, int> _topGpuProcessesSync =
        new(snapshotKey: p => p.Pid, viewModelKey: vm => vm.Pid, create: s => new ProcessUsageViewModel(s), apply: (vm, s) => vm.Apply(s));

    public ObservableCollection<ProcessUsageViewModel> TopCpuProcesses => _topCpuProcessesSync.Items;
    public ObservableCollection<ProcessUsageViewModel> TopGpuProcesses => _topGpuProcessesSync.Items;

    public PortraitViewModel(
        IHardwareMonitorService hardwareMonitor,
        ProcessRankerService processRanker,
        IFpsSource fpsSource,
        FanLabelStore fanLabelStore)
    {
        _hardwareMonitor = hardwareMonitor;
        _processRanker = processRanker;
        _fpsSource = fpsSource;
        _fanLabelStore = fanLabelStore;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;
        if (_hardwareMonitor.LatestSnapshot is { } snapshot)
        {
            Apply(snapshot);
        }

        _tickerTimer = new Timer(_ => Tick(), null, TimeSpan.Zero, TickerInterval);
        _processRankingTimer = new Timer(_ => RefreshTopProcesses(), null, TimeSpan.Zero, ProcessRankingInterval);
    }

    public void RenameFan(string fanId, string newLabel) => _fanLabelStore.SetLabel(fanId, newLabel);

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e) =>
        _dispatcherQueue.TryEnqueue(() => Apply(e.Snapshot));

    private void Apply(HardwareSnapshot snapshot)
    {
        var cpu = snapshot.Cpu;
        CpuName = cpu.Name;
        CpuVendor = PortraitVendorDetector.FromName(cpu.Name);
        CpuUsagePercent = cpu.UtilisationPercent;
        CpuUsageText = $"{cpu.UtilisationPercent:F0}%";
        _cpuHistory.Add(cpu.UtilisationPercent);
        CpuUsageHistory = _cpuHistory.Snapshot();
        CpuTempText = $"{cpu.TemperatureCelsius:F0}°C";
        CpuTempSeverity = PortraitSeverityThresholds.ForTemp(cpu.TemperatureCelsius);
        CpuClockText = $"{cpu.ClockSpeedMhz:F0} MHz";
        CpuPowerText = $"{cpu.PackagePowerWatts:F0} W";

        var gpu = snapshot.Gpu;
        GpuName = gpu.Name;
        GpuVendor = PortraitVendorDetector.FromName(gpu.Name);
        GpuUsagePercent = gpu.UtilisationPercent;
        GpuUsageText = $"{gpu.UtilisationPercent:F0}%";
        _gpuHistory.Add(gpu.UtilisationPercent);
        GpuUsageHistory = _gpuHistory.Snapshot();
        GpuTempText = $"{gpu.TemperatureCelsius:F0}°C";
        GpuTempSeverity = PortraitSeverityThresholds.ForTemp(gpu.TemperatureCelsius);
        GpuHotspotText = $"{gpu.HotspotTemperatureCelsius:F0}°C";
        GpuHotspotSeverity = PortraitSeverityThresholds.ForTemp(gpu.HotspotTemperatureCelsius);
        GpuFanText = $"{gpu.FanSpeedPercent:F0}%";
        GpuWattageText = $"{gpu.PowerDrawWatts:F0} W";
        GpuCoreClockText = $"{gpu.CoreClockMhz:F0} MHz";
        GpuMemClockText = $"{gpu.MemoryClockMhz:F0} MHz";
        VramUsedText = $"{gpu.VramUsedMb / 1024:F1} GB";
        VramPercent = gpu.VramTotalMb <= 0 ? 0 : gpu.VramUsedMb / gpu.VramTotalMb * 100.0;

        var memory = snapshot.Memory;
        RamUsedText = $"{memory.UsedBytes / 1024 / 1024 / 1024:F1} GB";
        RamPercent = memory.UtilisationPercent;
        RamSeverity = PortraitSeverityThresholds.ForPercent(memory.UtilisationPercent);

        var primaryDrive = snapshot.Drives.FirstOrDefault();
        if (primaryDrive is not null)
        {
            DriveTempText = $"{primaryDrive.TemperatureCelsius:F0}°C";
            DriveTempSeverity = PortraitSeverityThresholds.ForTemp(primaryDrive.TemperatureCelsius, warn: 50, critical: 60);
        }

        var fanRows = snapshot.Motherboard.FanSpeeds
            .Select(f => new PortraitFanRow(f.Name, _fanLabelStore.GetLabel(f.Name, f.Name), $"{f.Value:F0} RPM"))
            .ToList();
        Fans.MergeFrom(fanRows, f => f.FanId);
    }

    private void Tick()
    {
        var now = DateTime.Now;
        _fpsSource.Refresh();
        var fps = _fpsSource.CurrentFps;

        _dispatcherQueue.TryEnqueue(() =>
        {
            CurrentTimeText = now.ToString("HH:mm:ss");
            CurrentDateText = now.ToString("dddd, d MMMM");
            FpsText = fps is { } value ? $"{value:F0}" : "--";
        });
    }

    private void RefreshTopProcesses()
    {
        var byCpu = _processRanker.GetTopByCpu(5);
        var byGpu = _processRanker.GetTopByGpu(5);
        _dispatcherQueue.TryEnqueue(() =>
        {
            _topCpuProcessesSync.Sync(byCpu);
            _topGpuProcessesSync.Sync(byGpu);
        });
    }

    public void Dispose()
    {
        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
        _tickerTimer.Dispose();
        _processRankingTimer.Dispose();
        // _fpsSource is a shared DI singleton, not owned here — disposing it would break every
        // other consumer of the same instance for the rest of the app's lifetime.
    }
}

/// <summary>A fan RPM readout paired with its (possibly user-renamed) display label. <c>FanId</c>
/// is the stable underlying sensor name used as the <see cref="FanLabelStore"/> key — renaming
/// never touches it, only <see cref="DisplayName"/>.</summary>
public sealed record PortraitFanRow(string FanId, string DisplayName, string RpmText);
