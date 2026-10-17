using AetherControl.Core.Events;
using AetherControl.Core.Fans;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// Closed-loop driver for a <see cref="FanCurve"/> preset: on every hardware snapshot it evaluates
/// the active curve against CPU package temperature and writes the target percent to each known fan
/// channel — but only through <see cref="FanCurveApplication.ShouldApply"/>'s anti-hunting gate, so
/// sub-degree wobble around a knot never chatters the Super I/O control registers.
/// <para>
/// Safety, layered the same way manual control is: no writes without a real temperature reading
/// (a missing sensor must never drive fans), no writes while
/// <see cref="IFanControlService.IsSoftwareControlSafe"/> is false, and <see cref="Stop"/> (like
/// process shutdown) hands every channel back to automatic/BIOS control. Channel identities are
/// refreshed at most every 30 s rather than re-enumerated per tick — <c>GetChannels()</c> performs
/// its own Super I/O read, and doubling per-tick chip traffic for a list that changes on the scale
/// of hardware swaps would be pure cost.
/// </para>
/// </summary>
public sealed class FanCurveController : IDisposable
{
    private static readonly TimeSpan ChannelRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IFanControlService _fanControl;
    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly ILogger<FanCurveController> _logger;
    private readonly object _stateLock = new();
    private FanCurve? _activeCurve;
    private readonly Dictionary<string, int> _lastApplied = new(StringComparer.Ordinal);
    private DateTimeOffset _lastChannelRefresh = DateTimeOffset.MinValue;
    private bool _disposed;

    public FanCurveController(
        IFanControlService fanControl,
        IHardwareMonitorService hardwareMonitor,
        ILogger<FanCurveController> logger)
    {
        _fanControl = fanControl;
        _hardwareMonitor = hardwareMonitor;
        _logger = logger;
        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;
    }

    public FanCurve? ActiveCurve
    {
        get
        {
            lock (_stateLock)
            {
                return _activeCurve;
            }
        }
    }

    /// <summary>Starts driving <paramref name="curve"/>. Baselines seed from each channel's live\n    /// percent, so the first tick takes effect promptly without slamming every fan at once.</summary>
    public void Start(FanCurve curve)
    {
        ArgumentNullException.ThrowIfNull(curve);
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _activeCurve = curve;
            _lastApplied.Clear();
            RefreshChannelsLocked();
        }

        _logger.LogInformation("Fan curve started: {CurveName}", curve.Name);
    }

    /// <summary>Stops driving and hands every channel back to automatic/BIOS control.</summary>
    public void Stop()
    {
        lock (_stateLock)
        {
            _activeCurve = null;
            _lastApplied.Clear();
        }

        try
        {
            _fanControl.ResetAllToAutomatic();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset fan channels to automatic on curve stop");
        }
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e)
    {
        FanCurve? curve;
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            curve = _activeCurve;
        }

        if (curve is null)
        {
            return;
        }

        var temperatureC = e.Snapshot.Cpu.TemperatureCelsius;
        if (temperatureC <= 0)
        {
            return; // no reading — never drive fans from a missing sensor
        }

        if (!_fanControl.IsSoftwareControlSafe)
        {
            return;
        }

        var target = curve.Evaluate(temperatureC);
        lock (_stateLock)
        {
            if (_disposed || !ReferenceEquals(_activeCurve, curve))
            {
                return;
            }

            if (DateTimeOffset.UtcNow - _lastChannelRefresh >= ChannelRefreshInterval)
            {
                RefreshChannelsLocked();
            }

            foreach (var channelId in _lastApplied.Keys.ToList())
            {
                var last = _lastApplied[channelId];
                if (!FanCurveApplication.ShouldApply(target, last))
                {
                    continue;
                }

                try
                {
                    _fanControl.SetPercent(channelId, target);
                    _lastApplied[channelId] = target;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Fan curve write failed for channel {ChannelId}", channelId);
                }
            }
        }
    }

    // Caller holds _stateLock. Adds newly-seen channels (seeded from their live percent so the
    // loop doesn't slam them on discovery) and drops disappeared ones (a removed header must not
    // keep a stale target that a future same-named channel would inherit).
    private void RefreshChannelsLocked()
    {
        try
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var channel in _fanControl.GetChannels())
            {
                seen.Add(channel.Id);
                _lastApplied.TryAdd(channel.Id, (int)Math.Round(channel.CurrentPercent));
            }

            foreach (var stale in _lastApplied.Keys.Where(id => !seen.Contains(id)).ToList())
            {
                _lastApplied.Remove(stale);
            }

            _lastChannelRefresh = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Fan channel refresh failed");
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _activeCurve = null;
            _lastApplied.Clear();
        }

        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;

        try
        {
            _fanControl.ResetAllToAutomatic();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reset fan channels to automatic on curve dispose");
        }
    }
}
