using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;

namespace AetherControl.Services.Processes;

/// <summary>
/// Puts Aether's motherboard readings into the RTSS on-screen display, next to whatever
/// Adrenalin/RTSS already show. Only what those overlays can't read is sent: board temperatures
/// and case-fan RPM come from the Super I/O chip Aether owns (and a second reader would corrupt).
/// CPU/GPU numbers are left to RTSS/Adrenalin, which already show them.
/// Opt-in (Settings → RTSS overlay); off by default.
/// </summary>
public sealed class RtssOverlayService(IHardwareMonitorService hardwareMonitor) : IDisposable
{
    private readonly RtssOsdWriter _writer = new();
    private readonly object _gate = new();
    private bool _enabled;

    public void SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            if (enabled == _enabled)
            {
                return;
            }

            _enabled = enabled;
            if (enabled)
            {
                hardwareMonitor.SnapshotUpdated += OnSnapshotUpdated;
            }
            else
            {
                hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdated;
                _writer.Release();
            }
        }
    }

    private void OnSnapshotUpdated(object? sender, SensorsUpdatedEventArgs e)
    {
        lock (_gate)
        {
            if (_enabled)
            {
                _writer.Write(RtssOsdText.Build(e.Snapshot));
            }
        }
    }

    public void Dispose() => SetEnabled(false);
}

/// <summary>Builds the one-line OSD text. Pure, so it's testable without RTSS.</summary>
public static class RtssOsdText
{
    // Same plausibility window as the Overview's board-probe label: unconnected NCT6701D inputs
    // read ~3 °C on the test rig, and must not show up in a gaming overlay as a real temperature.
    private const double MinPlausibleC = 10, MaxPlausibleC = 125;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(3);

    public static string Build(HardwareSnapshot snapshot)
    {
        if (snapshot.MotherboardAge > StaleAfter)
        {
            return "Aether  Board: stale";
        }

        var text = new StringBuilder("Aether");
        var temps = snapshot.Motherboard.VrmTemperatures
            .Where(t => t.Value is >= MinPlausibleC and <= MaxPlausibleC)
            .ToList();
        if (temps.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"  Board {temps.Max(t => t.Value):0}°C");
        }

        // Unpopulated headers read 0 RPM on the test rig; leave them out.
        var fans = snapshot.Motherboard.FanSpeeds.Where(f => f.Value > 0).Select(f => f.Value.ToString("0", CultureInfo.InvariantCulture)).ToList();
        if (fans.Count > 0)
        {
            text.Append("  Fans ").Append(string.Join(" ", fans)).Append(" rpm");
        }

        return text.ToString();
    }
}

/// <summary>
/// Writes to an OSD slot in RTSS's shared memory ("RTSSSharedMemoryV2"), following the RTSS SDK
/// sample's UpdateOSD/ReleaseOSD: slots from index 1 (0 is RTSS's own), claim one by writing our
/// owner name into an empty slot, write the text (szOSDEx on v2.7+, szOSD before), then bump
/// dwOSDFrame so RTSS redraws. Header offsets match those <see cref="RtssFpsSource"/> already reads.
/// Best-effort: RTSS not running just means nothing is drawn.
/// </summary>
internal sealed class RtssOsdWriter(string mapName = "RTSSSharedMemoryV2")
{
    private const uint Signature = 0x52545353; // 'RTSS'
    private const uint MinVersion = 0x00020000;
    private const uint OsdExVersion = 0x00020007;
    private const string Owner = "AetherControl";

    private const int OffsetVersion = 4;
    private const int OffsetOsdEntrySize = 20;
    private const int OffsetOsdArrOffset = 24;
    private const int OffsetOsdArrSize = 28;
    private const int OffsetOsdFrame = 32;

    private const int OsdSize = 256;
    private const int OwnerSize = 256;
    private const int OsdExSize = 4096;

    /// <summary>True once text has been written into RTSS's memory.</summary>
    public bool Write(string text) => WithView((view, version, slotOffset) =>
    {
        var bytes = Encoding.Latin1.GetBytes(text);
        if (version >= OsdExVersion)
        {
            WriteZ(view, slotOffset + OsdSize + OwnerSize, bytes, OsdExSize);
        }
        else
        {
            WriteZ(view, slotOffset, bytes, OsdSize);
        }
    }, claim: true);

    /// <summary>Clears our slot so RTSS stops drawing it (overlay turned off, app exiting).</summary>
    public void Release() => WithView((view, _, slotOffset) =>
    {
        var entrySize = view.ReadUInt32(OffsetOsdEntrySize);
        for (var i = 0; i < entrySize; i++)
        {
            view.Write(slotOffset + i, (byte)0);
        }
    }, claim: false);

    private bool WithView(Action<MemoryMappedViewAccessor, uint, long> action, bool claim)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.ReadWrite);
            var version = view.ReadUInt32(OffsetVersion);
            if (view.ReadUInt32(0) != Signature || version < MinVersion)
            {
                return false;
            }

            var slot = FindSlot(view, claim);
            if (slot < 0)
            {
                return false;
            }

            action(view, version, slot);
            view.Write(OffsetOsdFrame, view.ReadUInt32(OffsetOsdFrame) + 1);
            return true;
        }
        catch
        {
            // RTSS not running (no map), or its memory changed under us. Nothing to draw.
            return false;
        }
    }

    /// <returns>Byte offset of our slot, or -1. With <paramref name="claim"/>, takes an empty one.</returns>
    private static long FindSlot(MemoryMappedViewAccessor view, bool claim)
    {
        var entrySize = view.ReadUInt32(OffsetOsdEntrySize);
        var arrOffset = view.ReadUInt32(OffsetOsdArrOffset);
        var arrSize = view.ReadUInt32(OffsetOsdArrSize);

        for (var pass = 0; pass < (claim ? 2 : 1); pass++)
        {
            for (uint i = 1; i < arrSize; i++)
            {
                var slot = (long)arrOffset + i * entrySize;
                var owner = ReadZ(view, slot + OsdSize, OwnerSize);
                if (pass == 0 && owner == Owner)
                {
                    return slot;
                }

                if (pass == 1 && owner.Length == 0)
                {
                    WriteZ(view, slot + OsdSize, Encoding.Latin1.GetBytes(Owner), OwnerSize);
                    return slot;
                }
            }
        }

        return -1;
    }

    private static string ReadZ(MemoryMappedViewAccessor view, long offset, int size)
    {
        var buffer = new byte[size];
        view.ReadArray(offset, buffer, 0, size);
        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.Latin1.GetString(buffer, 0, length < 0 ? size : length);
    }

    private static void WriteZ(MemoryMappedViewAccessor view, long offset, byte[] bytes, int size)
    {
        var buffer = new byte[size]; // zero-filled: terminates the string and clears any old tail
        Array.Copy(bytes, buffer, Math.Min(bytes.Length, size - 1));
        view.WriteArray(offset, buffer, 0, size);
    }
}
