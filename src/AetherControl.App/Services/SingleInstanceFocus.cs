using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AetherControl.App.Services;

/// <summary>
/// Foregrounds the already-running instance's main window when a second launch is refused by
/// <c>SingleInstanceGuard</c> — so double-clicking the exe while Aether Control runs (e.g. hidden
/// in the tray) reveals it instead of silently exiting. Same-elevation to same-elevation, so no
/// UIPI foreground restrictions apply between the two processes.
/// </summary>
internal static partial class SingleInstanceFocus
{
    private const int SwRestore = 9;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    public static void FocusExisting()
    {
        try
        {
            var self = Process.GetCurrentProcess().Id;
            foreach (var process in Process.GetProcessesByName("AetherControl"))
            {
                using (process)
                {
                    if (process.Id == self)
                    {
                        continue;
                    }

                    var handle = process.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SwRestore);
                        SetForegroundWindow(handle);
                        return;
                    }
                }
            }
        }
        catch
        {
            // Best-effort — the second instance still exits either way.
        }
    }
}
