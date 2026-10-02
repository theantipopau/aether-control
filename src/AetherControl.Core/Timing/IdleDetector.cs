using System;

namespace AetherControl.Core.Timing;

/// <summary>
/// Decides whether the machine counts as "idle" for polling-cadence purposes: CPU <b>and</b> GPU
/// both staying under a busy threshold for a sustained stretch. Hysteresis is one-sided on
/// purpose — entering idle requires <see cref="MinimumIdleDuration"/> of continuously low usage
/// (a momentary lull between bursts never slows polling), while a single busy sample exits idle
/// immediately (a real spike is captured at full cadence on the very next tick). Pure and
/// clock-injected so tests drive the timeline directly.
/// </summary>
public sealed class IdleDetector
{
    public const double DefaultBusyThresholdPercent = 10;

    private readonly double _busyThresholdPercent;
    private readonly TimeSpan _minimumIdleDuration;
    private DateTimeOffset? _idleSince;

    public IdleDetector(
        double busyThresholdPercent = DefaultBusyThresholdPercent,
        TimeSpan? minimumIdleDuration = null)
    {
        _busyThresholdPercent = busyThresholdPercent;
        _minimumIdleDuration = minimumIdleDuration ?? TimeSpan.FromSeconds(15);
    }

    public TimeSpan MinimumIdleDuration => _minimumIdleDuration;

    /// <summary>Observes one sample and reports whether idle conditions have held long enough to
    /// throttle. Must be called on every fast tick regardless of the answer — the streak itself
    /// needs the samples. Non-monotonic timestamps (clock adjustments) simply read as "not yet".</summary>
    public bool IsIdle(DateTimeOffset now, double cpuUtilisationPercent, double gpuUtilisationPercent)
    {
        var busy = cpuUtilisationPercent >= _busyThresholdPercent
                   || gpuUtilisationPercent >= _busyThresholdPercent;

        if (busy)
        {
            _idleSince = null;
            return false;
        }

        if (_idleSince is null)
        {
            _idleSince = now;
            return false; // quiet so far, but not yet for long enough
        }

        return now - _idleSince >= _minimumIdleDuration;
    }
}
