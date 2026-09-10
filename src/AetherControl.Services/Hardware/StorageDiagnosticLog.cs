namespace AetherControl.Services.Hardware;

/// <summary>
/// Temporary diagnostic logging to root-cause the storage free-space figures still drifting
/// (~0.85x correlated across all physical disks between two close-together polls, even after
/// deduplicating WMI associator rows) — see ROADMAP.md Phase 20/21. Two screenshots weren't enough
/// evidence last time either; this captures the actual per-poll associator query results so the
/// next recurrence can be diagnosed from a real trace instead of a fourth guess. Remove once resolved.
/// </summary>
internal static class StorageDiagnosticLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "storage-trace.log");

    public static void Write(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // Best-effort diagnostic logging only.
        }
    }
}
