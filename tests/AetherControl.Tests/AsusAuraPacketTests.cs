using AetherControl.Services.Rgb;

namespace AetherControl.Tests;

/// <summary>Pins the ASUS Aura USB wire format byte-for-byte (see AsusAuraUsbService's doc comment
/// for the protocol facts). A wrong byte here is a wrong command to real hardware.</summary>
public sealed class AsusAuraPacketTests
{
    private static byte[] Table(byte argb, byte mainboardLeds, byte rgbHeaders)
    {
        var table = new byte[60];
        table[0x02] = argb;
        table[0x1B] = mainboardLeds;
        table[0x1D] = rgbHeaders;
        return table;
    }

    [Fact]
    public void Requests_AreReportIdEC_65Bytes()
    {
        Assert.Equal([0xEC, 0x82], AuraPackets.FirmwareRequest()[..2]);
        Assert.Equal([0xEC, 0xB0], AuraPackets.ConfigTableRequest()[..2]);
        Assert.Equal([0xEC, 0x52, 0x53, 0x00, 0x01], AuraPackets.Init()[..5]);
        Assert.Equal([0xEC, 0x3F, 0x55], AuraPackets.Commit()[..3]);
        Assert.All(new[] { AuraPackets.Init(), AuraPackets.Commit() }, r => Assert.Equal(65, r.Length));
    }

    [Fact]
    public void Effect_And_Color_Layout()
    {
        Assert.Equal([0xEC, 0x35, 0x02, 0x00, 0x00, 0x01], AuraPackets.Effect(2, 1)[..6]);

        // Mainboard zone: LEDs 0-2 → mask 0x0007, RGB triples from byte 5.
        var color = AuraPackets.Color(0, 3, (0x11, 0x22, 0x33));
        Assert.Equal([0xEC, 0x36, 0x00, 0x07, 0x00, 0x11, 0x22, 0x33, 0x11, 0x22, 0x33, 0x11, 0x22, 0x33], color[..14]);

        // An ARGB header after 3 mainboard LEDs: slot 3 → mask 0x0008, colour at byte 5 + 9.
        var header = AuraPackets.Color(3, 1, (0xAA, 0xBB, 0xCC));
        Assert.Equal(0x00, header[2]);
        Assert.Equal(0x08, header[3]);
        Assert.Equal([0xAA, 0xBB, 0xCC], header[14..17]);
        Assert.Equal(0, header[5]);
    }

    [Fact]
    public void Layout_MainboardOnChannel0_ArgbFrom1_ExceptAula3Ar32()
    {
        var layout = AuraLayout.FromConfigTable(Table(argb: 2, mainboardLeds: 3, rgbHeaders: 1), "AULA3-6K75");
        Assert.Equal([(byte)0, (byte)1, (byte)2], layout.Zones.Select(z => z.EffectChannel));
        Assert.Equal([0, 3, 4], layout.Zones.Select(z => z.StartLed));

        var ar32 = AuraLayout.FromConfigTable(Table(argb: 2, mainboardLeds: 3, rgbHeaders: 1), "AULA3-AR32-0207");
        Assert.Equal([(byte)2, (byte)0, (byte)1], ar32.Zones.Select(z => z.EffectChannel));
    }

    // Live capture, test rig PRIME B650EM-A WIFI (PID 0x19AF), 2026-11-14, read-only probe:
    // firmware "AULA3-AR32-0309", config table below. No onboard LEDs, 3 ARGB headers.
    private static readonly byte[] TestRigConfigTable = Convert.FromHexString(
        "1E9F03010000783C" + "00010000783C0001" + "0000783C00000000" + "00000000030301F4" + new string('0', 56));

    [Fact]
    public void Layout_TestRig_ThreeArgbHeaders_OnChannels0To2()
    {
        Assert.Equal(60, TestRigConfigTable.Length);
        var layout = AuraLayout.FromConfigTable(TestRigConfigTable, "AULA3-AR32-0309");

        Assert.Equal(0, layout.MainboardLeds);
        Assert.Equal(0, layout.RgbHeaders); // table says 3, but with 0 mainboard LEDs that's not real
        Assert.Equal(["ARGB header 1", "ARGB header 2", "ARGB header 3"], layout.Zones.Select(z => z.Name));
        Assert.Equal([(byte)0, (byte)1, (byte)2], layout.Zones.Select(z => z.EffectChannel));
        Assert.Equal([0, 1, 2], layout.Zones.Select(z => z.StartLed));
    }

    [Fact]
    public void SetAllZones_Off_SendsNoColourPackets()
    {
        var layout = AuraLayout.FromConfigTable(Table(argb: 1, mainboardLeds: 2, rgbHeaders: 0), "AULA3-6K75");
        var off = AuraPackets.SetAllZones(layout, mode: 0, (255, 255, 255)).ToList();
        Assert.Equal(2, off.Count);
        Assert.All(off, p => Assert.Equal(0x35, p[1]));

        var staticPackets = AuraPackets.SetAllZones(layout, mode: 1, (255, 0, 0)).ToList();
        Assert.Equal([0x35, 0x36, 0x35, 0x36], staticPackets.Select(p => p[1]));
    }

    [Fact]
    public void Parse_RejectsWrongReplyId()
    {
        var reply = new byte[65];
        reply[0] = 0xEC;
        reply[1] = 0x30;
        reply[4 + 0x1B] = 5;
        Assert.Equal(5, AuraPackets.ParseConfigTable(reply)![0x1B]);

        reply[1] = 0x31;
        Assert.Null(AuraPackets.ParseConfigTable(reply));
    }
}
