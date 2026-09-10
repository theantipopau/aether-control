using System.Runtime.InteropServices;

namespace AetherControl.Services.Optimisation;

/// <summary>
/// P/Invoke surface for the undocumented-but-widely-used technique behind
/// tools like RAMMap's "Empty Standby List": ask the kernel to purge the
/// standby page list via <c>NtSetSystemInformation</c>. Requires the process
/// to hold and enable <c>SeProfileSingleProcessPrivilege</c>, which in turn
/// requires running elevated — Aether Control surfaces a clear error rather
/// than failing silently when that privilege can't be acquired.
/// </summary>
internal static class NativeMemoryMethods
{
    private const int SystemMemoryListInformation = 0x50;
    private const int MemoryPurgeStandbyList = 4;
    private const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    private const uint TOKEN_QUERY = 0x0008;
    private const uint SE_PRIVILEGE_ENABLED = 0x0002;
    private const string SE_PROFILE_SINGLE_PROCESS_NAME = "SeProfileSingleProcessPrivilege";

    [DllImport("ntdll.dll")]
    private static extern int NtSetSystemInformation(int infoClass, ref int info, int length);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out LUID lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        IntPtr tokenHandle, bool disableAllPrivileges, ref TOKEN_PRIVILEGES newState, int bufferLength,
        IntPtr previousState, IntPtr returnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct LUID
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TOKEN_PRIVILEGES
    {
        public uint PrivilegeCount;
        public LUID Luid;
        public uint Attributes;
    }

    /// <summary>Enables the privilege and purges the standby list. Throws with a descriptive message on failure.</summary>
    public static void PurgeStandbyList()
    {
        if (!TryEnablePrivilege(SE_PROFILE_SINGLE_PROCESS_NAME))
        {
            throw new InvalidOperationException(
                "Could not enable SeProfileSingleProcessPrivilege. Aether Control must be run as Administrator to clear standby memory.");
        }

        var command = MemoryPurgeStandbyList;
        var status = NtSetSystemInformation(SystemMemoryListInformation, ref command, sizeof(int));
        if (status != 0)
        {
            throw new InvalidOperationException($"NtSetSystemInformation failed with NTSTATUS 0x{status:X8}.");
        }
    }

    private static bool TryEnablePrivilege(string privilegeName)
    {
        var process = GetCurrentProcess();
        if (!OpenProcessToken(process, TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
        {
            return false;
        }

        try
        {
            if (!LookupPrivilegeValue(null, privilegeName, out var luid))
            {
                return false;
            }

            var privileges = new TOKEN_PRIVILEGES
            {
                PrivilegeCount = 1,
                Luid = luid,
                Attributes = SE_PRIVILEGE_ENABLED
            };

            return AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero)
                   && Marshal.GetLastWin32Error() == 0;
        }
        finally
        {
            CloseHandle(token);
        }
    }
}
