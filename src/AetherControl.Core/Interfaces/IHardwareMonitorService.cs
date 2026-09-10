using AetherControl.Core.Events;
using AetherControl.Core.Models;

namespace AetherControl.Core.Interfaces;

/// <summary>
/// Polls the hardware backend (LibreHardwareMonitor) on a background timer and
/// publishes normalised snapshots. Implementations must be safe to start/stop
/// repeatedly and must never block the UI thread.
/// </summary>
public interface IHardwareMonitorService : IDisposable
{
    HardwareSnapshot? LatestSnapshot { get; }

    event EventHandler<SensorsUpdatedEventArgs>? SnapshotUpdated;

    void Start(TimeSpan pollingInterval);

    void Stop();

    void SetPollingInterval(TimeSpan interval);
}
