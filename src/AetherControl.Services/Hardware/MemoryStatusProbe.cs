using System.Runtime.InteropServices;

namespace AetherControl.Services.Hardware;

/// <summary>
/// LibreHardwareMonitorLib 0.9.5-pre429's own Memory hardware node was found (via a live diagnostic)
/// to report physically impossible numbers on this machine — ~42.8GB "Memory Used" on a 31GB-total
/// system, and it crashed outright with a NullReferenceException when run unelevated. Both point to
/// its newer RAM SPD/thermal detection (RAMSPDToolkit's SMBus probing, added after 0.9.4) rather than
/// a mapping bug on our side. Reading total/available physical memory directly via the same Win32 API
/// Task Manager itself is built on sidesteps LHM's Memory group entirely instead of trying to work
/// around its numbers — same idea as MemorySpeedProbe (WMI) and the GPU% PDH counter: when LHM's own
/// reading for a metric isn't trustworthy on real hardware, read it a different way instead of
/// patching around the wrong number.
/// </summary>
internal static class MemoryStatusProbe
{
    public static (double TotalBytes, double AvailableBytes) Query()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (status.TotalPhysical, status.AvailablePhysical) : (0, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
}
