using System.Collections.ObjectModel;
using AetherControl.Core.Collections;
using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Processes;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;

namespace AetherControl.App.ViewModels;

public sealed partial class DashboardViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan ProcessRankingInterval = TimeSpan.FromSeconds(2);
    // Matches Portrait Mode's own sparkline window — a minute of history at the default 1s poll.
    private const int TrendHistoryCapacity = 60;

    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly ProcessRankerService _processRanker;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Timer _processRankingTimer;
    private readonly PortraitHistory _cpuUsageHistory = new(TrendHistoryCapacity);
    private readonly PortraitHistory _gpuUsageHistory = new(TrendHistoryCapacity);

    [ObservableProperty] private string cpuName = "—";
    [ObservableProperty] private double cpuTemperature;
    [ObservableProperty] private double cpuPackagePower;
    [ObservableProperty] private double cpuUtilisation;
    [ObservableProperty] private double cpuClockSpeed;
    [ObservableProperty] private double cpuVoltage;
    // Reassigned wholesale each poll like the scalar properties above, not merged like the
    // ObservableCollections below — safe here because PortraitSparkline is one long-lived control
    // instance bound directly (Mode=OneWay), not an ItemsControl generating a container per item, so
    // there's no container-recreation cost to a fresh IReadOnlyList<double> reference every second.
    [ObservableProperty] private IReadOnlyList<double> cpuUsageHistory = [];

    [ObservableProperty] private string gpuName = "—";
    [ObservableProperty] private double gpuTemperature;
    [ObservableProperty] private double gpuHotspotTemperature;
    [ObservableProperty] private double gpuUtilisation;
    [ObservableProperty] private double gpuPowerDraw;
    [ObservableProperty] private double gpuVramUsedMb;
    [ObservableProperty] private double gpuFanSpeedPercent;
    [ObservableProperty] private IReadOnlyList<double> gpuUsageHistory = [];

    [ObservableProperty] private double ramUsedGb;
    [ObservableProperty] private double ramAvailableGb;
    [ObservableProperty] private double ramUtilisationPercent;
    [ObservableProperty] private double ramSpeedMhz;

    [ObservableProperty] private double networkUploadMbps;
    [ObservableProperty] private double networkDownloadMbps;
    [ObservableProperty] private double networkLatencyMs;
    [ObservableProperty] private string networkExternalIp = "—";

    [ObservableProperty] private string motherboardModel = "—";

    // Each device/sensor/process gets a long-lived view model, created once per stable identity and
    // updated in place forever after (LiveCollectionSync.Sync issues Add only for a new key and
    // Remove only once a key's been absent for several consecutive polls — never Replace for an
    // existing one). This replaced an earlier ObservableCollectionMergeExtensions.MergeFrom approach
    // that compared whole immutable snapshot records for equality: any single naturally-volatile
    // field (temperature, a voltage's ripple, CPU% jitter) made two otherwise-identical readings
    // compare unequal, which degenerated into repeatedly recreating the bound MetricCard/NumberTween
    // for a device under continuous real activity (confirmed via a live UI-layer trace — see
    // ROADMAP.md Phase 32). A view model raising PropertyChanged only for the specific field that
    // actually changed doesn't have this failure mode at all.
    private readonly LiveCollectionSync<StorageDriveViewModel, StorageDriveInfo, string> _drivesSync =
        new(snapshotKey: d => d.DeviceId, viewModelKey: vm => vm.DeviceId, create: s => new StorageDriveViewModel(s), apply: (vm, s) => vm.Apply(s));

    private readonly LiveCollectionSync<NamedSensorValueViewModel, NamedSensorValue, string> _voltagesSync =
        new(snapshotKey: v => v.Name, viewModelKey: vm => vm.Name, create: s => new NamedSensorValueViewModel(s), apply: (vm, s) => vm.Apply(s));

    private readonly LiveCollectionSync<NamedSensorValueViewModel, NamedSensorValue, string> _fanSpeedsSync =
        new(snapshotKey: v => v.Name, viewModelKey: vm => vm.Name, create: s => new NamedSensorValueViewModel(s), apply: (vm, s) => vm.Apply(s));

    private readonly LiveCollectionSync<NamedSensorValueViewModel, NamedSensorValue, string> _vrmTemperaturesSync =
        new(snapshotKey: v => v.Name, viewModelKey: vm => vm.Name, create: s => new NamedSensorValueViewModel(s), apply: (vm, s) => vm.Apply(s));

    private readonly LiveCollectionSync<ProcessUsageViewModel, ProcessUsageInfo, int> _topProcessesSync =
        new(snapshotKey: p => p.Pid, viewModelKey: vm => vm.Pid, create: s => new ProcessUsageViewModel(s), apply: (vm, s) => vm.Apply(s));

    public ObservableCollection<StorageDriveViewModel> Drives => _drivesSync.Items;
    public ObservableCollection<NamedSensorValueViewModel> MotherboardVoltages => _voltagesSync.Items;
    public ObservableCollection<NamedSensorValueViewModel> MotherboardFanSpeeds => _fanSpeedsSync.Items;
    public ObservableCollection<NamedSensorValueViewModel> MotherboardVrmTemperatures => _vrmTemperaturesSync.Items;
    public ObservableCollection<ProcessUsageViewModel> TopProcessesByCpu => _topProcessesSync.Items;

    // Not every CPU's own sensor set exposes a real core-voltage measurement (confirmed via a real
    // sensor dump: this AMD Ryzen 7 9800X3D only reports "VID" from the CPU side — the VRM's
    // target, not a measurement). HardwareMonitorService.Poll falls back to the motherboard Super
    // I/O chip's own Vcore reading when that happens (same physical rail, different sensor group);
    // this stays 0 — hiding the card rather than showing "0.00V" — only when neither source has a
    // real reading at all.
    public bool HasCpuVoltage => CpuVoltage > 0;

    partial void OnCpuVoltageChanged(double value) => OnPropertyChanged(nameof(HasCpuVoltage));

    public DashboardViewModel(IHardwareMonitorService hardwareMonitor, ProcessRankerService processRanker)
    {
        _hardwareMonitor = hardwareMonitor;
        _processRanker = processRanker;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;

        if (_hardwareMonitor.LatestSnapshot is { } snapshot)
        {
            Apply(snapshot);
        }

        // Its own slower-cadence timer rather than piggybacking on the per-second hardware poll —
        // Process.GetProcesses() over the full process list is real work, and running it on every
        // hardware tick would compete with the smooth per-second dashboard refresh this app has
        // already spent a lot of effort getting right.
        _processRankingTimer = new Timer(_ => RefreshTopProcesses(), null, TimeSpan.Zero, ProcessRankingInterval);
    }

    private void RefreshTopProcesses()
    {
        var top = _processRanker.GetTopByCpu(5);
        _dispatcherQueue.TryEnqueue(() => _topProcessesSync.Sync(top));
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e)
    {
        _dispatcherQueue.TryEnqueue(() => Apply(e.Snapshot));
    }

    private void Apply(HardwareSnapshot snapshot)
    {
        CpuName = snapshot.Cpu.Name;
        CpuTemperature = snapshot.Cpu.TemperatureCelsius;
        CpuPackagePower = snapshot.Cpu.PackagePowerWatts;
        CpuUtilisation = snapshot.Cpu.UtilisationPercent;
        CpuClockSpeed = snapshot.Cpu.ClockSpeedMhz;
        CpuVoltage = snapshot.Cpu.CoreVoltage;
        _cpuUsageHistory.Add(snapshot.Cpu.UtilisationPercent);
        CpuUsageHistory = _cpuUsageHistory.Snapshot();

        GpuName = snapshot.Gpu.Name;
        GpuTemperature = snapshot.Gpu.TemperatureCelsius;
        GpuHotspotTemperature = snapshot.Gpu.HotspotTemperatureCelsius;
        GpuUtilisation = snapshot.Gpu.UtilisationPercent;
        GpuPowerDraw = snapshot.Gpu.PowerDrawWatts;
        GpuVramUsedMb = snapshot.Gpu.VramUsedMb;
        GpuFanSpeedPercent = snapshot.Gpu.FanSpeedPercent;
        _gpuUsageHistory.Add(snapshot.Gpu.UtilisationPercent);
        GpuUsageHistory = _gpuUsageHistory.Snapshot();

        RamUsedGb = snapshot.Memory.UsedBytes / 1024 / 1024 / 1024;
        RamAvailableGb = snapshot.Memory.AvailableBytes / 1024 / 1024 / 1024;
        RamUtilisationPercent = snapshot.Memory.UtilisationPercent;
        RamSpeedMhz = snapshot.Memory.SpeedMhz;

        NetworkUploadMbps = snapshot.Network.UploadKbps / 1000;
        NetworkDownloadMbps = snapshot.Network.DownloadKbps / 1000;
        NetworkLatencyMs = snapshot.Network.LatencyMs;
        NetworkExternalIp = string.IsNullOrEmpty(snapshot.Network.ExternalIpAddress) ? "—" : snapshot.Network.ExternalIpAddress;

        _drivesSync.Sync(snapshot.Drives);
        MotherboardModel = snapshot.Motherboard.Model;
        _voltagesSync.Sync(snapshot.Motherboard.Voltages);
        _fanSpeedsSync.Sync(snapshot.Motherboard.FanSpeeds);
        _vrmTemperaturesSync.Sync(snapshot.Motherboard.VrmTemperatures);
    }

    public void Dispose()
    {
        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
        _processRankingTimer.Dispose();
    }
}
