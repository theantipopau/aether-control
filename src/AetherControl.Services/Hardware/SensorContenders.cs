using System.Diagnostics;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Other programs known to poll the motherboard's Super I/O chip. Only one owner can read it
/// cleanly: a concurrent reader makes both sides read 0xFF / poisoned values (live A/B test,
/// 2026-09-23; periodic ~11 s re-poisoning with Armoury Crate + iCUE running). The Overview's
/// "stale" warning names whichever of these is running, rather than a generic "contention".
/// Matched by process name (not service name: ArmouryCrateService's process is
/// ArmouryCrate.Service, verified 2026-09-26). A miss only means the warning can't name the culprit.
/// RTSS and AMD Adrenalin are not on this list: neither touches Super I/O.
/// </summary>
public static class SensorContenders
{
    private static readonly (string Process, string Label)[] Known =
    [
        ("AsusFanControlService", "Armoury Crate"),
        ("ArmouryCrate.Service", "Armoury Crate"),
        ("ArmouryCrateService", "Armoury Crate"),
        ("ArmouryCrateControlInterface", "Armoury Crate"),
        ("AsusOptimizationStartupTask", "Armoury Crate"),
        ("iCUE", "Corsair iCUE"),
        ("PortraitStats", "Portrait Stats"),
        ("HWiNFO64", "HWiNFO"),
        ("HWiNFO32", "HWiNFO"),
        ("FanControl", "FanControl"),
        ("LibreHardwareMonitor", "LibreHardwareMonitor"),
        ("OpenHardwareMonitor", "OpenHardwareMonitor"),
        ("HWMonitor_x64", "HWMonitor"),
        ("HWMonitor", "HWMonitor"),
        ("aida64", "AIDA64"),
        ("SignalRgb", "SignalRGB"),
    ];

    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(10);
    private static readonly object Gate = new();
    private static IReadOnlyList<string> _cached = [];
    private static DateTime _cachedAtUtc = DateTime.MinValue;

    /// <summary>Distinct friendly names of contenders running now. Cached for 10 s: callers check
    /// this from the per-second poll path, and enumerating processes isn't free.</summary>
    public static IReadOnlyList<string> Running()
    {
        lock (Gate)
        {
            if (DateTime.UtcNow - _cachedAtUtc < CacheFor)
            {
                return _cached;
            }

            try
            {
                var running = Process.GetProcesses().Select(p => { using (p) { return p.ProcessName; } });
                _cached = Match(running);
            }
            catch
            {
                _cached = [];
            }

            _cachedAtUtc = DateTime.UtcNow;
            return _cached;
        }
    }

    /// <summary>Pure mapping from running process names to friendly labels (testable).</summary>
    public static IReadOnlyList<string> Match(IEnumerable<string> runningProcessNames)
    {
        var names = new HashSet<string>(runningProcessNames, StringComparer.OrdinalIgnoreCase);
        return Known.Where(k => names.Contains(k.Process)).Select(k => k.Label).Distinct().ToList();
    }
}
