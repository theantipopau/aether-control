using System.Diagnostics;
using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Processes;
using LibreHardwareMonitor.Hardware;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Owns the single LibreHardwareMonitor <see cref="Computer"/> instance for
/// the process (LHM does not support multiple concurrent instances safely)
/// and polls it on a background <see cref="Timer"/>, publishing a normalised
/// <see cref="HardwareSnapshot"/> on every tick. CPU package sensors and some
/// SMBus-based motherboard sensors require the process to run elevated;
/// when it isn't, those readings simply come back as zero rather than throwing.
/// </summary>
public sealed class HardwareMonitorService : IHardwareMonitorService, IFanControlService
{
    private static readonly TimeSpan FanProbeInterval = TimeSpan.FromSeconds(3);

    // Best-effort — exact service names aren't publicly documented and vary by ASUS software
    // version. "AsusFanControlService" is the one confirmed by this board's own empty fan-RPM
    // readings (see FanRpmProbeService); the others are commonly reported Armoury Crate services.
    // A miss here just means the warning doesn't show, not that control silently fails worse.
    private static readonly string[] ConflictingProcessNames =
    [
        "AsusFanControlService",
        "ArmouryCrateService",
        "ArmouryCrateControlInterface",
        "AsusOptimizationStartupTask"
    ];

    // Never let software control switch a channel fully off — if Aether Control crashes or is
    // killed mid-session with no automatic-fallback watchdog, a stuck 0% fan is a real thermal
    // risk in a way a stuck-at-20% fan simply isn't.
    private const int MinSoftwareFanPercent = 20;

    private readonly ILogger<HardwareMonitorService> _logger;
    private readonly INetworkMonitorService _networkMonitor;
    private readonly FanRpmProbeService _fanProbe;
    private readonly ProcessRankerService _processRanker;
    private readonly Computer _computer;
    private readonly HardwareUpdateVisitor _visitor = new();

    private Timer? _timer;
    private readonly object _pollLock = new();
    private readonly Lazy<double> _memorySpeedMhz = new(MemorySpeedProbe.QuerySpeedMhz);
    private readonly EmaSmoother _cpuClockSmoother = new();
    private readonly Dictionary<int, EmaSmoother> _coreClockSmoothers = new();
    // Same "confidently-wrong-vs-legitimately-jittery" story as CPU clock: LHM's "CPU Total" and
    // "GPU Core" Load sensors are read once per second directly off the driver/OS, which genuinely
    // swings hard poll-to-poll (e.g. GPU idle-vs-compositing-burst) — Portrait Stats reads the exact
    // same sensor via the exact same LHM call and shows it just as raw, it just isn't displayed
    // side-by-side with a fixed dashboard card the way Aether's is, which is what made the jitter
    // look like an Aether-only bug. Damping the *display* the same way clock speed already is.
    private readonly EmaSmoother _cpuLoadSmoother = new();
    private readonly EmaSmoother _gpuLoadSmoother = new();

    public HardwareMonitorService(
        ILogger<HardwareMonitorService> logger,
        INetworkMonitorService networkMonitor,
        FanRpmProbeService fanProbe,
        ProcessRankerService processRanker)
    {
        _logger = logger;
        _networkMonitor = networkMonitor;
        _fanProbe = fanProbe;
        _processRanker = processRanker;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
            IsNetworkEnabled = false // handled by NetworkMonitorService for finer-grained throughput/latency
        };

        try
        {
            _computer.Open();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open LibreHardwareMonitor computer session");
        }
    }

    public HardwareSnapshot? LatestSnapshot { get; private set; }

    public event EventHandler<SensorsUpdatedEventArgs>? SnapshotUpdated;

    public void Start(TimeSpan pollingInterval)
    {
        _networkMonitor.Start(pollingInterval);
        _fanProbe.Start(FanProbeInterval);
        _timer?.Dispose();
        _timer = new Timer(_ => SafePoll(), null, TimeSpan.Zero, pollingInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _networkMonitor.Stop();
        _fanProbe.Stop();
    }

    public void SetPollingInterval(TimeSpan interval)
    {
        _timer?.Change(TimeSpan.Zero, interval);
        _networkMonitor.Start(interval);
    }

    private void SafePoll()
    {
        if (!Monitor.TryEnter(_pollLock))
        {
            return; // previous poll still running (e.g. slow WMI call) — skip this tick rather than pile up
        }

        try
        {
            Poll();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Hardware poll failed");
        }
        finally
        {
            Monitor.Exit(_pollLock);
        }
    }

    private void Poll()
    {
        _computer.Accept(_visitor);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = SelectPrimaryGpu();
        var memory = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Memory);
        var motherboard = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
        // _computer.Hardware's enumeration order isn't guaranteed stable poll-to-poll. Each drive's
        // own values are correctly matched by model name (see StorageHealthProbe), but if the LIST
        // ORDER itself varies, the card at a fixed screen position still ends up showing a different
        // physical drive's numbers on each tick — visually indistinguishable from a single value
        // flickering. Sorting by a stable identifier fixes the position, not just the content.
        var storageDevices = _computer.Hardware
            .Where(h => h.HardwareType == HardwareType.Storage)
            .OrderBy(h => h.Identifier.ToString(), StringComparer.Ordinal)
            .ToList();

        var rawStorage = HardwareSnapshotMapper.MapStorage(storageDevices);
        var motherboardInfo = HardwareSnapshotMapper.MapMotherboard(motherboard);
        if (_fanProbe.LatestFanSpeeds.Count > 0)
        {
            // Overlay the out-of-process probe's readings — see FanRpmProbeService for why the
            // long-lived Computer instance's own Fan sensors aren't trusted for this.
            motherboardInfo.FanSpeeds = _fanProbe.LatestFanSpeeds;
        }

        var memoryInfo = HardwareSnapshotMapper.MapMemory(memory);
        memoryInfo.SpeedMhz = _memorySpeedMhz.Value; // WMI SPD data — doesn't change at runtime, queried once and cached

        var cpuInfo = HardwareSnapshotMapper.MapCpu(cpu);
        SmoothClockSpeeds(cpuInfo);
        cpuInfo.UtilisationPercent = (float)_cpuLoadSmoother.Update(cpuInfo.UtilisationPercent);

        var gpuInfo = HardwareSnapshotMapper.MapGpu(gpu);
        // Prefer the same "GPU Engine" PDH counter Task Manager's headline GPU% is built from over
        // LHM's ADL/NVAPI "GPU Core" load sensor — confirmed via a real side-by-side that the two
        // measure genuinely different things (LHM's driver-level figure ran ~2x Task Manager's at
        // the same instant, not just noise), and matching what Task Manager shows is what "accurate"
        // means to someone comparing the two side by side. Falls back to the LHM value only when this
        // driver doesn't expose the counter category at all.
        var gpuLoadPercent = _processRanker.IsGpuEngineCounterAvailable
            ? _processRanker.GetTotalEngineUtilization()
            : gpuInfo.UtilisationPercent;
        gpuInfo.UtilisationPercent = (float)_gpuLoadSmoother.Update(gpuLoadPercent);

        var snapshot = new HardwareSnapshot
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Cpu = cpuInfo,
            Gpu = gpuInfo,
            Memory = memoryInfo,
            Motherboard = motherboardInfo,
            Drives = StorageHealthProbe.Enrich(rawStorage),
            Network = _networkMonitor.Latest ?? new NetworkInfo()
        };

        LatestSnapshot = snapshot;
        SnapshotUpdated?.Invoke(this, new SensorsUpdatedEventArgs(snapshot));
    }

    /// <summary>
    /// Applies <see cref="EmaSmoother"/> to the package/average clock and each per-core clock.
    /// The underlying sensor value is real and correctly matched (see <see cref="HardwareSnapshotMapper"/>'s
    /// own fallback fixes) — this only damps the display so a genuine base/boost P-state transition
    /// doesn't read as an instantaneous flick between two numbers a card apart.
    /// </summary>
    private void SmoothClockSpeeds(CpuInfo cpuInfo)
    {
        cpuInfo.ClockSpeedMhz = _cpuClockSmoother.Update(cpuInfo.ClockSpeedMhz);

        foreach (var core in cpuInfo.Cores)
        {
            if (!_coreClockSmoothers.TryGetValue(core.Index, out var smoother))
            {
                smoother = new EmaSmoother();
                _coreClockSmoothers[core.Index] = smoother;
            }

            core.ClockSpeedMhz = smoother.Update(core.ClockSpeedMhz);
        }
    }

    public bool IsConflictingVendorSoftwareRunning() =>
        ConflictingProcessNames.Any(name => Process.GetProcessesByName(name).Length > 0);

    public IReadOnlyList<FanControlChannel> GetChannels()
    {
        lock (_pollLock)
        {
            var superIo = FindSuperIo();
            if (superIo is null)
            {
                return [];
            }

            superIo.Update();
            return superIo.Sensors
                .Where(s => s.SensorType == SensorType.Control && s.Control is not null)
                .Select(s => new FanControlChannel
                {
                    Id = s.Identifier.ToString(),
                    Name = s.Name,
                    CurrentPercent = s.Value ?? 0,
                    IsSoftwareControlled = s.Control!.ControlMode == ControlMode.Software
                })
                .OrderBy(c => c.Name, StringComparer.Ordinal)
                .ToList();
        }
    }

    public void SetPercent(string channelId, int percent)
    {
        var clamped = Math.Clamp(percent, MinSoftwareFanPercent, 100);
        lock (_pollLock)
        {
            FindControlSensor(channelId)?.Control?.SetSoftware(clamped);
        }
    }

    public void ResetToAutomatic(string channelId)
    {
        lock (_pollLock)
        {
            FindControlSensor(channelId)?.Control?.SetDefault();
        }
    }

    public void ResetAllToAutomatic()
    {
        lock (_pollLock)
        {
            var superIo = FindSuperIo();
            if (superIo is null)
            {
                return;
            }

            foreach (var sensor in superIo.Sensors.Where(s => s.SensorType == SensorType.Control && s.Control is not null))
            {
                sensor.Control!.SetDefault();
            }
        }
    }

    private ISensor? FindControlSensor(string channelId)
    {
        var superIo = FindSuperIo();
        return superIo?.Sensors.FirstOrDefault(s => s.Identifier.ToString() == channelId);
    }

    private IHardware? FindSuperIo()
    {
        var motherboard = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Motherboard);
        return motherboard?.SubHardware.FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO);
    }

    /// <summary>
    /// On systems with both an integrated and a discrete GPU, picks the one
    /// reporting the most VRAM rather than whichever LibreHardwareMonitor
    /// happens to enumerate first — otherwise the dashboard can end up
    /// showing the iGPU's near-idle numbers on a machine that's actually
    /// under a discrete-GPU gaming load.
    /// </summary>
    private IHardware? SelectPrimaryGpu()
    {
        IHardware? best = null;
        float bestVram = -1;

        foreach (var hardware in _computer.Hardware)
        {
            if (hardware.HardwareType is not (HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel))
            {
                continue;
            }

            var vram = hardware.Sensors
                .FirstOrDefault(s => s.SensorType == SensorType.SmallData && s.Name.Contains("GPU Memory Total", StringComparison.OrdinalIgnoreCase))
                ?.Value ?? 0;

            if (vram > bestVram)
            {
                bestVram = vram;
                best = hardware;
            }
        }

        return best;
    }

    public void Dispose()
    {
        Stop();
        try
        {
            ResetAllToAutomatic(); // never leave a fan stuck on a manual curve when the app exits
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error resetting fan channels to automatic on shutdown");
        }

        try
        {
            _computer.Close();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing LibreHardwareMonitor computer session");
        }
    }
}
