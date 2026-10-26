using System.Diagnostics;

namespace AetherControl.App.Services;

/// <summary>
/// Lets <c>AetherControl.exe --exit</c> close a running instance the same way tray → Exit does.
/// The installer and uninstaller need this: the window usually hides to the tray, so WM_CLOSE
/// (plain taskkill) only re-hides it, and a forced kill skips the ordered shutdown that hands
/// every fan channel back to BIOS control.
/// </summary>
internal static class ExitSignal
{
    private const string EventName = "Local\\AetherControl.Exit.v1";

    private static EventWaitHandle? _event;
    private static RegisteredWaitHandle? _registration;

    /// <summary>Primary instance: runs <paramref name="onExit"/> (on a pool thread) when signalled.</summary>
    public static void Listen(Action onExit)
    {
        _event = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(_event, (_, _) => onExit(), null, Timeout.Infinite, executeOnlyOnce: true);
    }

    /// <summary>Second instance: signals the primary and waits for it to exit, so the caller
    /// (the installer) doesn't start replacing files while they're still loaded.</summary>
    public static void SignalAndWait(TimeSpan timeout)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                return;
            }

            using (handle)
            {
                handle.Set();
            }

            var self = Environment.ProcessId;
            foreach (var process in Process.GetProcessesByName("AetherControl"))
            {
                using (process)
                {
                    if (process.Id != self)
                    {
                        process.WaitForExit(timeout);
                    }
                }
            }
        }
        catch
        {
            // Best-effort — the installer's Restart Manager fallback still closes it.
        }
    }
}
