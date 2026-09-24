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
/// <para>
/// This is the ONLY thing <em>in this process</em> allowed to touch Super I/O (motherboard
/// voltages/fans). A prior design (<c>AetherControl.FanHelper.exe</c>, a second process spawned
/// every 3 seconds to read fan RPM) was removed after a live A/B test (2026-09-23) proved two
/// concurrent Super I/O readers/writers on the same Nuvoton chip make BOTH sides read back 0xFF
/// persistently — every voltage pinned at 2.04V/4.08V, every fan 0 RPM. Fan RPM is now read
/// directly from this session's own SuperIO sensors, same as every other motherboard sensor, via
/// <see cref="HardwareSnapshotMapper.MapMotherboard"/>.
/// </para>
/// <para>
/// That alone isn't sufficient, though: real ASUS/vendor software already installed on the machine
/// (Armoury Crate, iCUE, etc. — see <see cref="ConflictingProcessNames"/>) polls the same chip on
/// its own schedule regardless of anything Aether does. Confirmed by live testing (2026-09-23) with
/// nothing of Aether's own running concurrently: a fresh <c>Computer.Open()</c> sometimes gets a
/// poisoned first read, and a poisoned read never self-corrects — 30 consecutive one-second polls
/// all reproduced the identical invalid pattern — until the whole session is closed and reopened.
/// A live system also re-poisons roughly every ~11 seconds on an otherwise idle poll loop, an
/// unmistakably periodic cadence, not random noise. <see cref="EnsureSuperIoReadsAreValid"/> and
/// the mid-session check in <see cref="Poll"/> handle both: detect via <see cref="LooksPoisoned"/>,
/// serve the last known-good motherboard reading rather than publish impossible values, and reopen
/// (rate-limited by <see cref="ReopenCooldown"/> so sustained contention can't turn into a Close/Open
/// every single poll) until a clean read comes back.
/// </para>
/// </summary>
public sealed class HardwareMonitorService : IHardwareMonitorService, IFanControlService
{
    // Best-effort — exact service names aren't publicly documented and vary by ASUS software
    // version. An earlier version of this comment claimed "AsusFanControlService" specifically was
    // confirmed to blank out fan RPM readings and that this was disproven; the disproof was correct
    // (the empty-readings bug then was a LibreHardwareMonitorLib version gap) but the general
    // premise — vendor software on this list contending for the same Super I/O chip — was later
    // confirmed true a different way: a live idle system (2026-09-23, nothing of Aether's own
    // running concurrently) re-poisoned its own Super I/O reads on an unmistakably periodic ~11
    // second cadence with Armoury Crate + iCUE both running, which is what EnsureSuperIoReadsAreValid
    // and Poll's mid-session check now self-heal from (see this class's own doc comment). This list
    // is a best-effort UI warning for *write* conflicts specifically (two programs both trying to
    // drive the same fan-control channel) — a miss here just means the warning doesn't show.
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
    private readonly ProcessRankerService _processRanker;
    private readonly Computer _computer;
    private readonly HardwareUpdateVisitor _visitor = new();

    private static readonly TimeSpan ReopenCooldown = TimeSpan.FromSeconds(10);

    private Timer? _timer;
    private readonly object _pollLock = new();
    private bool _reopenPending;
    private DateTime _lastReopenAttemptUtc = DateTime.MinValue;
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
    // A single wrong sample (one bad instantaneous read, not a real level change — e.g. LHM's driver
    // call landing mid-transition) still leaks partway through an EmaSmoother and takes a couple of
    // seconds to fade. A median-of-3 ahead of it throws a genuine one-off spike out completely (it's
    // never the middle value of three) before smoothing ever sees it.
    private readonly MedianFilter _cpuLoadMedian = new();
    private readonly MedianFilter _gpuLoadMedian = new();

    public HardwareMonitorService(
        ILogger<HardwareMonitorService> logger,
        INetworkMonitorService networkMonitor,
        ProcessRankerService processRanker)
    {
        _logger = logger;
        _networkMonitor = networkMonitor;
        _processRanker = processRanker;
        _computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            // Deliberately off — see MemoryStatusProbe. LibreHardwareMonitorLib's Memory group (its
            // RAM SPD/thermal detection) reported physically impossible usage on this machine and
            // crashed outright with a NullReferenceException when unelevated; RAM is read directly
            // via Win32 instead, so there's no reason to pay for this group's cost or risk at all.
            IsMemoryEnabled = false,
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
            // No ILogger provider is wired up anywhere in the App project, so LogError above goes
            // nowhere — Open() failing here otherwise means every sensor reads zero for the rest of
            // the process's life with zero visible error anywhere. A version-mismatch regression
            // (LibreHardwareMonitorLib bumped without also bumping the System.Management it needs)
            // took real diagnostic effort to find specifically because of this silence, so this one
            // startup-critical failure point gets a permanent, minimal, best-effort file log — same
            // idea as AetherControl.App's own UnhandledExceptionLog, just for Services, which can't
            // reference the App project.
            LogStartupDiagnostic("hardware-open-failed.log", ex.ToString());
        }

        EnsureSuperIoReadsAreValid();
    }

    private const int MaxSuperIoOpenAttempts = 5;

    /// <summary>
    /// A fresh <see cref="Computer.Open"/> on this board (Nuvoton NCT6701D) sometimes gets a
    /// "poisoned" first Super I/O read that never self-corrects for that session's lifetime — proven
    /// via a live capture (2026-09-23): 30 consecutive one-second polls all read back the exact same
    /// invalid pattern (every voltage rail collapsed to one of two identical values — Vcore = AVCC =
    /// CMOS Battery = 2.04V or 4.08V, which is physically impossible; real boards report many
    /// genuinely distinct rails). A real hardware/driver quirk, not a multi-process contention issue —
    /// it reproduced with nothing else touching the chip. Detected by counting distinct voltage
    /// values; if too few for this many sensors, the whole <see cref="Computer"/> session is closed
    /// and reopened (a fresh native driver handle) and re-checked, up to a few times.
    /// </summary>
    private void EnsureSuperIoReadsAreValid()
    {
        for (var attempt = 1; attempt <= MaxSuperIoOpenAttempts; attempt++)
        {
            var superIo = FindSuperIo();
            if (superIo is null)
            {
                return; // no Super I/O chip on this board — nothing to validate
            }

            superIo.Update();
            var (poisoned, distinctCount, sensorCount) = LooksPoisoned(VoltageValues(superIo.Sensors));
            if (!poisoned)
            {
                if (attempt > 1)
                {
                    LogStartupDiagnostic("superio-reopen.log", $"Recovered after {attempt} attempt(s) — {distinctCount} distinct voltage values.");
                }

                return;
            }

            LogStartupDiagnostic("superio-reopen.log",
                $"Startup attempt {attempt}/{MaxSuperIoOpenAttempts}: poisoned read ({distinctCount} distinct value(s) across {sensorCount} voltage sensors) — reopening the hardware session.");

            if (attempt == MaxSuperIoOpenAttempts)
            {
                break; // don't reopen again just to fall out of the loop unused
            }

            _computer.Close();
            Thread.Sleep(300);
            _computer.Open();
        }

        LogStartupDiagnostic("superio-reopen.log", $"Still poisoned after {MaxSuperIoOpenAttempts} attempts — voltages/fans will read wrong until Aether Control is restarted.");
    }

    private static IEnumerable<double> VoltageValues(IEnumerable<ISensor> sensors) =>
        sensors.Where(s => s.SensorType == SensorType.Voltage && s.Value.HasValue).Select(s => (double)s.Value!.Value);

    /// <summary>
    /// Real hardware reports many genuinely distinct voltage rails (Vcore, +3.3V, AVCC, CPU
    /// termination, ...) — the poisoned read this guards against collapses ALL of them into one or
    /// two identical values instead. &gt;= 4 distinct values is a conservative real-hardware threshold
    /// (this board's 15 voltage sensors cover at least 6 genuinely different rails when reading
    /// correctly). Boards with very few voltage sensors to begin with just won't trip this check.
    /// Pure and <c>internal</c> (not tied to <see cref="ISensor"/>) specifically so it's directly
    /// unit-testable against the exact values captured from the live poisoned/healthy reads this
    /// guards against (see <c>SuperIoPoisonDetectionTests</c>).
    /// </summary>
    internal static (bool Poisoned, int DistinctCount, int SensorCount) LooksPoisoned(IEnumerable<double> voltageValues)
    {
        var values = voltageValues.Select(v => Math.Round(v, 2)).ToList();
        var distinct = values.Distinct().Count();
        return (values.Count >= 4 && distinct < 4, distinct, values.Count);
    }

    private static void LogStartupDiagnostic(string fileName, string message)
    {
        try
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // best-effort diagnostic only
        }
    }

    public HardwareSnapshot? LatestSnapshot { get; private set; }

    public event EventHandler<SensorsUpdatedEventArgs>? SnapshotUpdated;

    public void Start(TimeSpan pollingInterval)
    {
        _networkMonitor.Start(pollingInterval);
        _timer?.Dispose();
        _timer = new Timer(_ => SafePoll(), null, TimeSpan.Zero, pollingInterval);
    }

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
        _networkMonitor.Stop();
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
        if (_reopenPending)
        {
            // Deferred from the end of the previous poll rather than done immediately when detected —
            // this poll's cpu/gpu/motherboard/storage IHardware references are all about to be
            // re-fetched fresh below, so there's no risk of using objects from a session that's
            // already been closed underneath them. Reuses the same verify-and-retry loop startup
            // uses rather than a single blind reopen — a bare reopen with no verification was found
            // (2026-09-23, live test) to sometimes re-poison itself on its own very next read (the
            // same "first read after Open() can be bad" quirk EnsureSuperIoReadsAreValid guards
            // against), producing an infinite poison→reopen→poison loop with no external cause at all.
            _reopenPending = false;
            EnsureSuperIoReadsAreValid();
        }

        _computer.Accept(_visitor);

        var cpu = _computer.Hardware.FirstOrDefault(h => h.HardwareType == HardwareType.Cpu);
        var gpu = SelectPrimaryGpu();
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
        var superIoSensors = motherboard?.SubHardware.FirstOrDefault(h => h.HardwareType == HardwareType.SuperIO)?.Sensors;
        if (superIoSensors is not null && LooksPoisoned(VoltageValues(superIoSensors)) is { Poisoned: true } poisonCheck)
        {
            // Same signature EnsureSuperIoReadsAreValid checks at startup, but found mid-session —
            // proven (2026-09-23) to happen with nothing else touching the chip and to never
            // self-correct without a full Close/Open. Falls back to the last known-good motherboard
            // reading for this one tick (same "stale beats garbage" convention as storage free-space)
            // rather than publish impossible voltages/fans.
            if (LatestSnapshot is not null)
            {
                motherboardInfo = LatestSnapshot.Motherboard;
            }

            // Cooldown, not "reopen on every poisoned poll" — sustained real contention (another
            // program polling Super I/O continuously) would otherwise mean a Close/Open every single
            // second forever. Serving last-known-good between attempts is the safe degraded state;
            // there's no need to hammer the driver to get there.
            if (DateTime.UtcNow - _lastReopenAttemptUtc >= ReopenCooldown)
            {
                LogStartupDiagnostic("superio-reopen.log",
                    $"Mid-session poisoned read ({poisonCheck.DistinctCount} distinct value(s) across {poisonCheck.SensorCount} voltage sensors) — using last-known-good reading, reopening before next poll.");
                _reopenPending = true;
                _lastReopenAttemptUtc = DateTime.UtcNow;
            }
        }

        // Not sourced from LibreHardwareMonitorLib's own Memory hardware node — see MemoryStatusProbe
        // for why (its RAM SPD/thermal detection reported physically impossible numbers on this
        // machine and crashed outright when unelevated).
        var (totalMemoryBytes, availableMemoryBytes) = MemoryStatusProbe.Query();
        var memoryInfo = new MemoryInfo
        {
            TotalBytes = totalMemoryBytes,
            AvailableBytes = availableMemoryBytes,
            UsedBytes = totalMemoryBytes - availableMemoryBytes,
            SpeedMhz = _memorySpeedMhz.Value // WMI SPD data — doesn't change at runtime, queried once and cached
        };

        var cpuInfo = HardwareSnapshotMapper.MapCpu(cpu);
        if (cpuInfo.CoreVoltage <= 0)
        {
            // The CPU's own AMD SMU sensor set has no real voltage measurement for this chip (only
            // a requested VID — see MapCpu's own comment), but the motherboard's Super I/O chip DOES
            // measure it directly off the VRM output (confirmed live: 1.376V, a plausible real Vcore,
            // vs. VID's ~0.19V) — same physical rail, just exposed through a different sensor group.
            var motherboardVcore = motherboardInfo.Voltages.FirstOrDefault(v => v.Name.Equals("Vcore", StringComparison.OrdinalIgnoreCase));
            if (motherboardVcore is not null)
            {
                cpuInfo.CoreVoltage = motherboardVcore.Value;
            }
        }

        SmoothClockSpeeds(cpuInfo);
        cpuInfo.UtilisationPercent = (float)_cpuLoadSmoother.Update(_cpuLoadMedian.Update(cpuInfo.UtilisationPercent));

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
        gpuInfo.UtilisationPercent = (float)_gpuLoadSmoother.Update(_gpuLoadMedian.Update(gpuLoadPercent));

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

    // Always true now that this session is the only thing in the process touching Super I/O — see
    // this class's own doc comment. Kept as a real property (not a literal `true` at every call
    // site) so a future second reader/writer (e.g. a plugin) has one obvious place to turn this off.
    public bool IsSoftwareControlSafe => true;

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
        if (!IsSoftwareControlSafe)
        {
            return;
        }

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
