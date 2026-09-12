namespace AetherControl.App.Controls;

/// <summary>
/// Traces a bound value from the moment it reaches a <see cref="MetricCard"/>'s dependency property
/// through the tween decision to what actually got written to the rendered <c>TextBlock</c> — added
/// during a storage-flicker audit that proved the backend (StorageHealthProbe) was stable but had
/// never actually confirmed what the UI layer did with a value once bound. Only emits for cards whose
/// <see cref="MetricCard.DiagnosticTag"/> is set (opt-in per instance, e.g. a drive's DeviceId), so
/// this doesn't log every tile in the app every second. Correlates by tag + a per-card instance id
/// (assigned once, in the card's constructor) rather than a full poll-sequence id threaded through
/// the whole snapshot pipeline — deliberately scoped down from the full correlation-id set that would
/// require touching HardwareMonitorService/DashboardViewModel/the binding engine, none of which this
/// diagnostic needed to answer "what did this card actually receive and display."
/// </summary>
internal static class UiValueTraceLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "ui-value-trace.log");

    public static void Write(string tag, Guid cardInstanceId, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath,
                $"[{DateTime.Now:HH:mm:ss.fff}] tag={tag} card={cardInstanceId:N} thread={Environment.CurrentManagedThreadId} {message}\n");
        }
        catch
        {
            // Best-effort diagnostic logging only.
        }
    }
}
