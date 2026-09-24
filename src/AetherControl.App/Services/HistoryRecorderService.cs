using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;

namespace AetherControl.App.Services;

/// <summary>
/// Periodically records hardware snapshots into history and purges old samples according to
/// <c>AppSettings.HistoryRetentionDays</c>. Neither of these was ever wired up anywhere in the app —
/// <see cref="IHistoryService.RecordAsync"/> had zero callers at all, meaning the History page has
/// never actually recorded a single real sample despite being a shipped, advertised feature;
/// <c>HistoryRetentionDays</c> was saved to Settings and never read back by anything.
/// </summary>
public sealed class HistoryRecorderService : IDisposable
{
    // Recording every one-second poll would make sensor_history enormous for no real benefit —
    // History's own resolution concept (Raw/Hourly/Daily rollups) already implies coarser-than-live
    // sampling is the intended shape, not a live per-second feed.
    private static readonly TimeSpan RecordInterval = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan PurgeInterval = TimeSpan.FromHours(24);

    private readonly IHardwareMonitorService _hardwareMonitor;
    private readonly IHistoryService _historyService;
    private readonly ISettingsService _settingsService;
    private readonly Timer _purgeTimer;
    private DateTimeOffset _lastRecordedUtc = DateTimeOffset.MinValue;

    public HistoryRecorderService(IHardwareMonitorService hardwareMonitor, IHistoryService historyService, ISettingsService settingsService)
    {
        _hardwareMonitor = hardwareMonitor;
        _historyService = historyService;
        _settingsService = settingsService;
        _hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;

        // Purge once at startup too (TimeSpan.Zero due time) — otherwise a machine that's rarely
        // left running for a full 24-hour stretch would never actually purge anything.
        _purgeTimer = new Timer(_ => _ = PurgeAsync(), null, TimeSpan.Zero, PurgeInterval);
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e)
    {
        // Fires on HardwareMonitorService's background polling thread.
        if (e.Snapshot.TimestampUtc - _lastRecordedUtc < RecordInterval)
        {
            return;
        }

        _lastRecordedUtc = e.Snapshot.TimestampUtc;
        _ = _historyService.RecordAsync(e.Snapshot);
    }

    private async Task PurgeAsync()
    {
        try
        {
            var days = Math.Max(1, _settingsService.Current.HistoryRetentionDays);
            await _historyService.PurgeOlderThanAsync(TimeSpan.FromDays(days));
        }
        catch
        {
            // Best-effort background maintenance — a failed purge just means old rows linger an
            // extra day, not something worth surfacing to the user or retrying aggressively.
        }
    }

    public void Dispose()
    {
        _hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
        _purgeTimer.Dispose();
    }
}
