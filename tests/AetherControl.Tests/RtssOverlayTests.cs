using System.IO.MemoryMappedFiles;
using System.Text;
using AetherControl.Core.Models;
using AetherControl.Services.Processes;

namespace AetherControl.Tests;

public sealed class RtssOverlayTests
{
    private const int EntrySize = 256 + 256 + 4096; // szOSD, szOSDOwner, szOSDEx (RTSS v2.7+)
    private const int ArrOffset = 64;
    private const int Slots = 4;

    [Fact]
    public void Writer_ClaimsSlotOne_WritesOsdEx_BumpsFrame_AndReleaseClears()
    {
        var name = $"AetherTestRtss{Guid.NewGuid():N}";
        using var map = MemoryMappedFile.CreateNew(name, ArrOffset + Slots * EntrySize);
        using var view = map.CreateViewAccessor();
        view.Write(0, 0x52545353u);      // 'RTSS'
        view.Write(4, 0x00020007u);      // v2.7: text goes to szOSDEx
        view.Write(20, (uint)EntrySize);
        view.Write(24, (uint)ArrOffset);
        view.Write(28, (uint)Slots);

        var writer = new RtssOsdWriter(name);
        Assert.True(writer.Write("Aether  Board 41°C"));
        Assert.True(writer.Write("Aether  Board 42°C")); // reuses its own slot, doesn't claim a second

        long slot1 = ArrOffset + EntrySize; // slot 0 belongs to RTSS itself
        Assert.Equal("AetherControl", ReadZ(view, slot1 + 256, 256));
        Assert.Equal("Aether  Board 42°C", ReadZ(view, slot1 + 512, 4096));
        Assert.Equal("", ReadZ(view, ArrOffset + 2 * EntrySize + 256, 256));
        Assert.Equal(2u, view.ReadUInt32(32));

        writer.Release();
        Assert.Equal("", ReadZ(view, slot1 + 256, 256));
        Assert.Equal(3u, view.ReadUInt32(32));
    }

    [Fact]
    public void Writer_WithoutRtss_ReturnsFalse() =>
        Assert.False(new RtssOsdWriter($"AetherTestMissing{Guid.NewGuid():N}").Write("x"));

    [Fact]
    public void Text_SkipsUnconnectedProbesAndIdleHeaders_AndSaysStale()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = new HardwareSnapshot
        {
            TimestampUtc = now,
            MotherboardReadUtc = now,
            Motherboard = new MotherboardInfo
            {
                // Values seen live on the test rig: 3 °C unconnected thermistors, 0 RPM empty headers.
                VrmTemperatures = [new() { Name = "Temperature #3", Value = 3 }, new() { Name = "System", Value = 41.4 }, new() { Name = "VRM", Value = 52 }],
                FanSpeeds = [new() { Name = "Fan #1", Value = 0 }, new() { Name = "Fan #2", Value = 1744 }, new() { Name = "Fan #7", Value = 2606 }]
            }
        };

        Assert.Equal("Aether  Board 52°C  Fans 1744 2606 rpm", RtssOsdText.Build(snapshot));

        snapshot.MotherboardReadUtc = now.AddSeconds(-8);
        Assert.Equal("Aether  Board: stale", RtssOsdText.Build(snapshot));
    }

    private static string ReadZ(MemoryMappedViewAccessor view, long offset, int size)
    {
        var buffer = new byte[size];
        view.ReadArray(offset, buffer, 0, size);
        var length = Array.IndexOf(buffer, (byte)0);
        return Encoding.Latin1.GetString(buffer, 0, length < 0 ? size : length);
    }
}
