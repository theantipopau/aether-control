using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;
using AetherControl.Core.Interfaces;

namespace AetherControl.Services.Processes;

/// <summary>
/// Ported verbatim from Portrait Stats' <c>RtssFpsSource</c>. Reads live FPS from RTSS
/// (RivaTuner Statistics Server) shared memory — RTSS's hook DLL injects into any game it's
/// tracking and publishes a "RTSSSharedMemoryV2" memory-mapped file with a per-process
/// frame-time table. No RTSS SDK/API reference needed, the layout is a small stable struct.
/// </summary>
public sealed class RtssFpsSource : IFpsSource, IDisposable
{
    private const string MapName = "RTSSSharedMemoryV2";
    private const int NameFieldSize = 260; // MAX_PATH

    private const int OffsetProcessId = 0;
    private const int OffsetName = 4;
    private const int OffsetFrameTime = OffsetName + NameFieldSize + 4 + 4 + 4 + 4; // skip flags/time0/time1/frames

    private MemoryMappedFile? _mmf;

    public bool IsConnected { get; private set; }
    public double? CurrentFps { get; private set; }
    public string? TargetProcessName { get; private set; }

    public void Refresh()
    {
        if (_mmf is null && !TryOpen())
        {
            IsConnected = false;
            CurrentFps = null;
            return;
        }

        try
        {
            using var accessor = _mmf!.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            var appEntrySize = accessor.ReadUInt32(8);
            var appArrOffset = accessor.ReadUInt32(12);
            var appArrSize = accessor.ReadUInt32(16);

            IsConnected = true;

            var foregroundPid = GetForegroundProcessId();
            if (foregroundPid == 0 || appEntrySize == 0)
            {
                CurrentFps = null;
                TargetProcessName = null;
                return;
            }

            for (uint i = 0; i < appArrSize; i++)
            {
                var entryOffset = (long)appArrOffset + i * appEntrySize;
                var pid = accessor.ReadUInt32(entryOffset + OffsetProcessId);
                if (pid != foregroundPid)
                {
                    continue;
                }

                var frameTimeMicros = accessor.ReadUInt32(entryOffset + OffsetFrameTime);
                if (frameTimeMicros == 0)
                {
                    CurrentFps = null;
                    TargetProcessName = null;
                    return;
                }

                var nameBytes = new byte[NameFieldSize];
                accessor.ReadArray(entryOffset + OffsetName, nameBytes, 0, NameFieldSize);
                var nullIndex = Array.IndexOf(nameBytes, (byte)0);
                var name = Encoding.ASCII.GetString(nameBytes, 0, nullIndex < 0 ? NameFieldSize : nullIndex);

                CurrentFps = 1_000_000.0 / frameTimeMicros;
                TargetProcessName = name;
                return;
            }

            // Foreground app isn't one RTSS is tracking (e.g. desktop, or an un-hooked window).
            CurrentFps = null;
            TargetProcessName = null;
        }
        catch
        {
            // RTSS was closed or the segment went away — drop the handle so we retry opening next tick.
            _mmf?.Dispose();
            _mmf = null;
            IsConnected = false;
            CurrentFps = null;
        }
    }

    private bool TryOpen()
    {
        try
        {
            _mmf = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static uint GetForegroundProcessId()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return 0;
        }

        GetWindowThreadProcessId(hwnd, out var pid);
        return pid;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    public void Dispose() => _mmf?.Dispose();
}
