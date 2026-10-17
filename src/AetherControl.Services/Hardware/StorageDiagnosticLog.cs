namespace AetherControl.Services.Hardware;

/// <summary>
/// Temporary diagnostic logging to root-cause the storage free-space figures still drifting
/// (~0.85x correlated across all physical disks between two close-together polls, even after
/// deduplicating WMI associator rows) — see ROADMAP.md Phase 20/21. Two screenshots weren't enough
/// evidence last time either; this captures the actual per-poll associator query results so the
/// next recurrence can be diagnosed from a real trace instead of a fourth guess. Remove once resolved.
/// Until then the file is truncated at <see cref="MaxLogBytes"/> — this fires every few seconds
/// in the shipped app, so an unbounded append would grow forever on a machine that never hits
/// the recurrence being chased.
/// </summary>
internal static class StorageDiagnosticLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "storage-trace.log");
    private const long MaxLogBytes = 256 * 1024;

    // Test hook: lets StorageDiagnosticLogTests point writes at a temp file instead of the real
    // %APPDATA% log. Null (default) is normal operation.
    internal static string? LogPathOverride { get; set; }

    public static void Write(string message)
    {
        try
        {
            var path = LogPathOverride ?? LogPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (File.Exists(path) && new FileInfo(path).Length > MaxLogBytes)
            {
                File.WriteAllText(path, string.Empty);
            }

            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
        }
        catch
        {
            // Best-effort diagnostic logging only.
        }
    }
}
