using Microsoft.Extensions.Logging;

namespace AetherControl.App.Services;

/// <summary>
/// Live-updated switches for <see cref="AppFileLoggerProvider"/>, set once from
/// <c>AppSettings.LoggingEnabled</c>/<c>LogLevel</c> after Settings loads (see <c>App.xaml.cs</c>)
/// and again whenever <c>SettingsViewModel.SaveAsync</c> runs — so toggling logging off/on or
/// changing the level takes effect immediately, without a restart.
/// </summary>
public static class LoggingSettings
{
    public static volatile bool Enabled = true;
    public static LogLevel MinLevel = LogLevel.Information;

    public static void Apply(bool enabled, string levelName)
    {
        Enabled = enabled;
        MinLevel = Enum.TryParse<LogLevel>(levelName, ignoreCase: true, out var level) ? level : LogLevel.Information;
    }
}

/// <summary>
/// Writes every <c>ILogger</c> call in the process to a plain daily rolling text file under
/// <c>%APPDATA%\Aether Control\logs\</c>. Genuinely needed, not decorative: <c>Host.CreateDefaultBuilder()</c>
/// sends unhandled log output to the Windows Application Event Log by default, but nothing in this
/// app had ever registered an actual logging provider — meaning every <c>_logger.LogWarning</c>/
/// <c>LogError</c> call throughout the codebase (there are many, e.g. every poll failure in
/// <c>HardwareMonitorService</c>) was effectively going nowhere a user could ever see without
/// attaching a debugger. Also makes real, at last, the <c>LoggingEnabled</c>/<c>LogLevel</c> Settings
/// controls, which were previously saved to the database and never read by anything.
/// </summary>
public sealed class AppFileLoggerProvider : ILoggerProvider
{
    private const int RetainDays = 14;
    private readonly string _logDirectory;
    private readonly object _writeLock = new();

    public AppFileLoggerProvider()
    {
        _logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Aether Control", "logs");
        Directory.CreateDirectory(_logDirectory);
        PruneOldFiles();
    }

    public ILogger CreateLogger(string categoryName) => new AppFileLogger(categoryName, this);

    internal void Write(string categoryName, LogLevel level, string message, Exception? exception)
    {
        if (!LoggingSettings.Enabled || level < LoggingSettings.MinLevel)
        {
            return;
        }

        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] {categoryName}: {message}";
        if (exception is not null)
        {
            line += $"\n{exception}";
        }

        var path = Path.Combine(_logDirectory, $"aether-{DateTime.Now:yyyy-MM-dd}.log");
        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(path, line + "\n");
            }
            catch
            {
                // A failed log write must never itself take down the app.
            }
        }
    }

    private void PruneOldFiles()
    {
        try
        {
            var cutoff = DateTime.Now.Date.AddDays(-RetainDays);
            foreach (var file in Directory.EnumerateFiles(_logDirectory, "aether-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch
        {
            // Best-effort housekeeping only.
        }
    }

    public void Dispose()
    {
    }
}

file sealed class AppFileLogger(string categoryName, AppFileLoggerProvider provider) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        provider.Write(categoryName, logLevel, formatter(state, exception), exception);
}
