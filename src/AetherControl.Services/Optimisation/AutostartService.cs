using System.Diagnostics;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// Registers/removes a Task Scheduler entry that launches Aether Control at logon — not a plain
/// <c>HKCU\...\Run</c> registry value, because the app's manifest requires administrator (for
/// LibreHardwareMonitorLib's kernel driver and fan control) and Windows will not silently elevate a
/// Run-key entry at sign-in; it would either fail outright or force a UAC prompt on every login,
/// defeating the point of "start with Windows". A Task Scheduler task with
/// <c>/RL HIGHEST</c> is the standard mechanism apps use for exactly this (same approach
/// Task Manager's own "Startup apps" list is built on) — Task Scheduler itself handles the silent
/// elevation for a triggered task, no UAC prompt at sign-in.
/// <para>
/// <c>AppSettings.StartWithWindows</c> was previously saved to the database and never read by
/// anything — this is what makes it a real setting.
/// </para>
/// </summary>
public sealed class AutostartService
{
    private const string TaskName = "AetherControl";

    public bool IsEnabled()
    {
        try
        {
            using var process = RunSchtasks($"/Query /TN \"{TaskName}\"");
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath))
                {
                    return;
                }

                using var process = RunSchtasks($"/Create /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST /F");
                process.WaitForExit(5000);
            }
            else
            {
                using var process = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Best-effort — a failure here means the toggle didn't take, not something worth
            // crashing Settings over. The task-query-based IsEnabled() check next time Settings
            // loads will accurately reflect whatever the real state ended up being.
        }
    }

    private static Process RunSchtasks(string arguments)
    {
        var startInfo = new ProcessStartInfo("schtasks.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start schtasks.exe");
    }
}
