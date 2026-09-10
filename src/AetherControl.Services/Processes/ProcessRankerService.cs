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
    private readonly Dictionary<int, (TimeSpan cpuTime, DateTime sampledAt)> _lastCpuSample = new();
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private DateTime _lastGpuRefresh = DateTime.MinValue;
    private bool? _gpuEngineAvailable;

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
        var result = new Dictionary<int, double>();

        // The category's existence can't change while we're running, so only pay for the
        // registry/perflib lookup once instead of every poll.
        _gpuEngineAvailable ??= PerformanceCounterCategory.Exists("GPU Engine");
        if (_gpuEngineAvailable != true)
        {
            return result;
        }

        RefreshGpuCounterInstances();

        foreach (var (instanceName, counter) in _gpuCounters)
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

    private void RefreshGpuCounterInstances()
    {
        // Instance names churn as processes start/stop using the GPU; re-scan periodically.
        if ((DateTime.UtcNow - _lastGpuRefresh).TotalSeconds < 2)
        {
            return;
        }

        _lastGpuRefresh = DateTime.UtcNow;

        var category = new PerformanceCounterCategory("GPU Engine");
        var currentInstances = category.GetInstanceNames().ToHashSet();

        foreach (var stale in _gpuCounters.Keys.Where(k => !currentInstances.Contains(k)).ToList())
        {
            _gpuCounters[stale].Dispose();
            _gpuCounters.Remove(stale);
        }

        foreach (var instanceName in currentInstances)
        {
            if (_gpuCounters.ContainsKey(instanceName))
            {
                continue;
            }

            try
            {
                _gpuCounters[instanceName] = new PerformanceCounter("GPU Engine", "Utilization Percentage", instanceName, readOnly: true);
            }
            catch
            {
                // Instance disappeared between enumeration and construction — ignore.
            }
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
