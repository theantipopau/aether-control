using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// Trims the working set of every accessible process via the documented
/// <c>EmptyWorkingSet</c> API — pages out resident private memory so Windows
/// can reclaim it, without touching or terminating anything. Ported from
/// Radium PCs Companion's <c>trim_all_working_sets</c> (Rust), which notes
/// the one real gotcha: opening a process with only <c>PROCESS_SET_QUOTA</c>
/// makes every trim silently fail — <c>PROCESS_QUERY_LIMITED_INFORMATION</c>
/// has to be requested too. Complements (but doesn't replace)
/// <see cref="NativeMemoryMethods.PurgeStandbyList"/>: trimming moves pages
/// to standby, purging standby is what actually returns them to "available."
/// Unlike the standby-list purge, this needs no special privilege — it works
/// for any process the caller can already open, so it still does something
/// useful even when the app isn't running elevated.
/// </summary>
internal static class RamTrimmer
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetQuota = 0x0100;

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    public static (int Trimmed, int Total) TrimAllWorkingSets()
    {
        EmptyWorkingSet(GetCurrentProcess());

        var processes = Process.GetProcesses();
        var trimmed = 0;

        try
        {
            foreach (var process in processes)
            {
                using (process)
                {
                    var handle = OpenProcess(ProcessQueryLimitedInformation | ProcessSetQuota, false, process.Id);
                    if (handle == IntPtr.Zero)
                    {
                        continue; // protected/system process we don't have rights to — expected for a chunk of them
                    }

                    try
                    {
                        if (EmptyWorkingSet(handle))
                        {
                            trimmed++;
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            // Best-effort sweep — a process exiting mid-enumeration shouldn't abort the whole trim.
        }

        return (trimmed, processes.Length);
    }

    public static double GetUsedMemoryGb()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status))
        {
            return 0;
        }

        var usedBytes = status.TotalPhys - status.AvailPhys;
        return usedBytes / 1024.0 / 1024 / 1024;
    }
}
