namespace AetherControl.Core.Diagnostics;

/// <summary>
/// Append-only log file with a size cap: once the file passes <paramref name="maxBytes"/> it is
/// moved to <c>&lt;name&gt;.old.log</c> (replacing the previous one) and a fresh file starts. Keeps one
/// generation of evidence instead of wiping it. Exists because the uncapped writers grew without
/// bound on the test rig — <c>unhandled-exceptions.log</c> had reached 37 MB by 0.9.6.
/// </summary>
public static class BoundedLogFile
{
    public const long DefaultMaxBytes = 1024 * 1024;

    private static readonly object Gate = new();

    /// <summary>Best-effort: never throws, since a failed log write must not take the app down.</summary>
    public static void Append(string path, string text, long maxBytes = DefaultMaxBytes)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var info = new FileInfo(path);
                if (info.Exists && info.Length > maxBytes)
                {
                    File.Move(path, Path.ChangeExtension(path, ".old.log"), overwrite: true);
                }

                File.AppendAllText(path, text);
            }
        }
        catch
        {
            // Best-effort diagnostic logging only.
        }
    }
}
