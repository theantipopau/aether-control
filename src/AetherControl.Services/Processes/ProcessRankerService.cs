using System.Diagnostics;
using System.Text.RegularExpressions;
using AetherControl.Core.Models;

namespace AetherControl.Services.Processes;

/// <summary>
/// Ranks running processes by CPU and GPU usage, the same way Task Manager's Processes tab does:
/// CPU from per-process processor time deltas, GPU from the "GPU Engine" performance counter
/// category (summed across engine instances per pid). Ported from Portrait Stats'
/// <c>ProcessRankerService</c> verbatim — this logic was already proven there, no reason to
/// reinvent it for Aether Control's own "what's using my machine" dashboard section.
/// </summary>
public sealed partial class ProcessRankerService
{
    // A rate-based PerformanceCounter (which "GPU Engine\Utilization Percentage" is) computes its
    // value from the delta between this call and its *own* last NextValue() call — Microsoft's own
    // docs confirm calling it again too soon after the previous call produces an unstable/garbage
    // result, not just a stale one. HardwareMonitorService's poll interval is user-configurable down
    // to 250ms, well under the ~1s a PDH rate counter needs between samples to stay accurate — so
    // this can't just sample on every caller's tick. Its own minimum cadence here, independent of
    // however often callers ask, is what keeps it stable regardless of the dashboard refresh setting.
    private static readonly TimeSpan MinEngineSampleInterval = TimeSpan.FromMilliseconds(950);

    private readonly Dictionary<int, (TimeSpan cpuTime, DateTime sampledAt)> _lastCpuSample = new();
    // Two independent counter sets, not one shared between the two GPU read paths: a rate-based
    // PerformanceCounter's NextValue() computes its delta from *its own* last call regardless of who
    // made that call, so a per-process ranking tick (SampleGpu, Portrait Mode's ~2s cadence) and the
    // headline-total tick (GetTotalEngineUtilization, ~1s) sharing one PerformanceCounter per instance
    // would desync each other's effective sampling interval the moment both are active at once —
    // exactly the kind of irregular-interval noise this whole change is trying to eliminate. Doubling
    // the PDH registrations is cheap (a handful of GPU-active processes at most).
    private readonly GpuEngineCounterSet _perProcessCounters = new();
    private readonly GpuEngineCounterSet _totalCounters = new();
    private DateTime _lastEngineTotalSampledAt = DateTime.MinValue;
    private double _lastEngineTotal;
    private bool? _gpuEngineAvailable;
    // This is now called from three independent timers (Dashboard's process-ranking tick, Portrait
    // Mode's, and HardwareMonitorService's own 1-second poll for the headline GPU% card) all hitting
    // the same singleton instance — none of the state above is thread-safe on its own. One lock
    // around each sample is simpler than making every collection concurrent for what's a once-a-second
    // call, not a hot path.
    private readonly object _sampleLock = new();

    /// <summary>Whether this session's driver exposes the "GPU Engine" PDH category at all (checked
    /// once, lazily, on first use) — false on some older/basic drivers, in which case callers should
    /// fall back to another source rather than treat a 0% reading as a real idle GPU.</summary>
    public bool IsGpuEngineCounterAvailable => _gpuEngineAvailable ??= PerformanceCounterCategory.Exists("GPU Engine");

    public IReadOnlyList<ProcessUsageInfo> GetTopByCpu(int count)
    {
        var samples = SampleCpu();
        if (samples.Count == 0)
        {
            return [];
        }

        return samples
            .OrderByDescending(kv => kv.Value.Percent)
            .Take(count)
            .Select(kv => new ProcessUsageInfo { Pid = kv.Key, Name = kv.Value.Name, CpuPercent = kv.Value.Percent, GpuPercent = 0 })
            .ToList();
    }

    public IReadOnlyList<ProcessUsageInfo> GetTopByGpu(int count)
    {
        var usageByPid = SampleGpu();
        if (usageByPid.Count == 0)
        {
            return [];
        }

        var results = new List<ProcessUsageInfo>(usageByPid.Count);
        foreach (var (pid, percent) in usageByPid)
        {
            var name = TryGetProcessName(pid);
            if (name is null)
            {
                continue;
            }

            results.Add(new ProcessUsageInfo { Pid = pid, Name = name, CpuPercent = 0, GpuPercent = percent });
        }

        return results.OrderByDescending(p => p.GpuPercent).Take(count).ToList();
    }

    private static string? TryGetProcessName(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<int, (double Percent, string Name)> SampleCpu()
    {
        lock (_sampleLock)
        {
            return SampleCpuLocked();
        }
    }

    private Dictionary<int, (double Percent, string Name)> SampleCpuLocked()
    {
        var now = DateTime.UtcNow;
        var result = new Dictionary<int, (double, string)>();
        var seenPids = new HashSet<int>();
        var processorCount = Environment.ProcessorCount;

        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    seenPids.Add(process.Id);
                    var cpuTime = process.TotalProcessorTime;
                    if (_lastCpuSample.TryGetValue(process.Id, out var last))
                    {
                        var cpuDelta = (cpuTime - last.cpuTime).TotalMilliseconds;
                        var wallDelta = (now - last.sampledAt).TotalMilliseconds;
                        if (wallDelta > 0)
                        {
                            var percent = cpuDelta / wallDelta / processorCount * 100.0;
                            if (percent > 0.05)
                            {
                                result[process.Id] = (percent, process.ProcessName);
                            }
                        }
                    }

                    _lastCpuSample[process.Id] = (cpuTime, now);
                }
                catch
                {
                    // Process exited or access denied (protected system process) — skip.
                }
            }
        }

        // Drop bookkeeping for processes that no longer exist, so this dictionary doesn't grow
        // unbounded over a long-running session (e.g. short-lived launcher/updater churn).
        foreach (var stalePid in _lastCpuSample.Keys.Where(pid => !seenPids.Contains(pid)).ToList())
        {
            _lastCpuSample.Remove(stalePid);
        }

        return result;
    }

    private Dictionary<int, double> SampleGpu()
    {
        lock (_sampleLock)
        {
            return SampleGpuLocked();
        }
    }

    private Dictionary<int, double> SampleGpuLocked()
    {
        var result = new Dictionary<int, double>();

        if (!IsGpuEngineCounterAvailable)
        {
            return result;
        }

        _perProcessCounters.Refresh();

        foreach (var (instanceName, counter) in _perProcessCounters.Counters)
        {
            float value;
            try
            {
                value = counter.NextValue();
            }
            catch
            {
                continue;
            }

            if (value <= 0)
            {
                continue;
            }

            var pid = ExtractPid(instanceName);
            if (pid is null)
            {
                continue;
            }

            result[pid.Value] = result.GetValueOrDefault(pid.Value) + value;
        }

        foreach (var key in result.Keys.ToList())
        {
            result[key] = Math.Min(result[key], 100.0);
        }

        return result;
    }

    /// <summary>Owns one set of "GPU Engine" PerformanceCounter instances, refreshed periodically as
    /// process/engine instance names churn. A private nested type rather than a shared dictionary so
    /// two independent consumers (per-process ranking vs. the headline total) never interleave
    /// NextValue() calls on the same counter object — see the fields' own comment for why that
    /// matters for a rate-based counter.</summary>
    private sealed class GpuEngineCounterSet
    {
        private readonly Dictionary<string, PerformanceCounter> _counters = new();
        private DateTime _lastRefresh = DateTime.MinValue;

        public IReadOnlyDictionary<string, PerformanceCounter> Counters => _counters;

        public void Refresh()
        {
            // Instance names churn as processes start/stop using the GPU; re-scan periodically.
            if ((DateTime.UtcNow - _lastRefresh).TotalSeconds < 2)
            {
                return;
            }

            _lastRefresh = DateTime.UtcNow;

            var category = new PerformanceCounterCategory("GPU Engine");
            var currentInstances = category.GetInstanceNames().ToHashSet();

            foreach (var stale in _counters.Keys.Where(k => !currentInstances.Contains(k)).ToList())
            {
                _counters[stale].Dispose();
                _counters.Remove(stale);
            }

            foreach (var instanceName in currentInstances)
            {
                if (_counters.ContainsKey(instanceName))
                {
                    continue;
                }

                try
                {
                    _counters[instanceName] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, readOnly: true);
                }
                catch
                {
                    // Instance disappeared between enumeration and construction — ignore.
                }
            }
        }
    }

    /// <summary>
    /// Total GPU utilisation across all processes for one engine type, summed the same way Task
    /// Manager computes its headline GPU% (it shows one engine — "3D" by default — totalled across
    /// every process using it). LibreHardwareMonitor's "GPU Core" sensor reads the driver's own
    /// broader load figure (all engines/clocks via ADL/NVAPI), which is a real, legitimate number
    /// but a different one — confirmed by a real side-by-side where LHM read ~15% against Task
    /// Manager's ~8% at the same instant, consistently, not just noise. Uses its own
    /// <see cref="GpuEngineCounterSet"/>, separate from <see cref="SampleGpu"/>'s — see that field's
    /// comment for why sharing counter objects between two independently-timed callers is unsafe.
    /// </summary>
    public double GetTotalEngineUtilization(string engineTypeContains = "engtype_3D")
    {
        lock (_sampleLock)
        {
            if (!IsGpuEngineCounterAvailable)
            {
                return 0;
            }

            // Sampling more often than this doesn't make the reading more current — it makes it
            // less accurate, since a rate counter queried too soon after its own last query has too
            // little elapsed time to compute a meaningful delta. Callers polling faster than this
            // (e.g. a dashboard refresh rate turned down to 250ms) just get the last good sample.
            var now = DateTime.UtcNow;
            if (now - _lastEngineTotalSampledAt < MinEngineSampleInterval)
            {
                return _lastEngineTotal;
            }

            _lastEngineTotalSampledAt = now;
            _totalCounters.Refresh();

            double total = 0;
            foreach (var (instanceName, counter) in _totalCounters.Counters)
            {
                if (!instanceName.Contains(engineTypeContains, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    total += counter.NextValue();
                }
                catch
                {
                    // Instance disappeared mid-read — skip it for this sample.
                }
            }

            _lastEngineTotal = Math.Min(total, 100.0);
            return _lastEngineTotal;
        }
    }

    private static int? ExtractPid(string instanceName)
    {
        var match = PidRegex().Match(instanceName);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"pid_(\d+)_")]
    private static partial Regex PidRegex();
}
