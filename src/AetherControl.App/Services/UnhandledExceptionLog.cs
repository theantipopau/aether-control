namespace AetherControl.App.Services;

/// <summary>
/// Last-resort logging for App.UnhandledException — deliberately independent of the DI container
/// (Services isn't built yet if this fires during startup) and of Microsoft.Extensions.Logging (its
/// own console sink writes nowhere visible for a windowed app anyway). Exists because a single
/// missed catch anywhere in the app can otherwise take the whole process down with it — confirmed:
/// an uncaught PathTooLongException from a background directory scan (WindowsCleanupService) crossed
/// the async-to-UI-thread boundary and became a fatal WinRT stowed exception with no record beyond a
/// Windows Error Reporting crash dump. This writes what actually threw before the handler tries to
/// keep the app alive.
/// </summary>
internal static class UnhandledExceptionLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "unhandled-exceptions.log");

    public static void Write(Exception? exception, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}\n{exception}\n\n");
        }
        catch
        {
            // Best-effort — if logging itself fails, don't let that mask the original exception.
        }
    }
}
