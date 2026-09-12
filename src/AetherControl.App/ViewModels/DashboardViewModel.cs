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

    // Persistent collections, mutated in place (see ObservableCollectionMergeExtensions) rather than
    // reassigned every poll — reassigning the reference (as these were before, via [ObservableProperty])
    // forces every bound ItemsControl to tear down and recreate its MetricCard containers from
    // scratch each time, which restarts each card's NumberTween glide from zero every single poll.
    public ObservableCollection<StorageDriveInfo> Drives { get; } = [];
    public ObservableCollection<NamedSensorValue> MotherboardVoltages { get; } = [];
    public ObservableCollection<NamedSensorValue> MotherboardFanSpeeds { get; } = [];
    public ObservableCollection<NamedSensorValue> MotherboardVrmTemperatures { get; } = [];
    public ObservableCollection<ProcessUsageInfo> TopProcessesByCpu { get; } = [];

    // Not every board/CPU/LHM-version combination exposes a real core-voltage sensor (confirmed via
    // a real sensor dump: this AMD Ryzen 7 9800X3D only reports "VID" — the VRM's target, not a
    // measurement — on LibreHardwareMonitorLib 0.9.4). Showing "0.00V" instead of hiding the card
    // would still read as a working-but-wrong sensor rather than an honestly unavailable one.
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
        _dispatcherQueue.TryEnqueue(() => TopProcessesByCpu.MergeFrom(top, p => p.Pid));
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

        Drives.MergeFrom(snapshot.Drives, d => d.DeviceId);
        MotherboardModel = snapshot.Motherboard.Model;
        MotherboardVoltages.MergeFrom(snapshot.Motherboard.Voltages, v => v.Name);
        MotherboardFanSpeeds.MergeFrom(snapshot.Motherboard.FanSpeeds, f => f.Name);
        MotherboardVrmTemperatures.MergeFrom(snapshot.Motherboard.VrmTemperatures, t => t.Name);
    }

    public void Dispose()
    {
        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
        _processRankingTimer.Dispose();
    }
}
