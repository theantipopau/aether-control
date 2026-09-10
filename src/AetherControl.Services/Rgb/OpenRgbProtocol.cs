using System.Text;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Wire types for the OpenRGB SDK network protocol (default port 6742). This
/// implements the subset needed to enumerate controllers and drive solid
/// colour / brightness / mode changes — the protocol OpenRGB itself, OpenRGB.NET
/// and every third-party integration (SignalRGB included) speak. Structure
/// mirrors the C++ reference server in the OpenRGB project; validate against
/// a running OpenRGB instance before relying on it in production, since exact
/// byte layouts have drifted slightly across SDK protocol versions (this
/// targets protocol version 4).
/// </summary>
internal static class OpenRgbPacketType
{
    public const uint RequestControllerCount = 0;
    public const uint RequestControllerData = 1;
    public const uint RequestProtocolVersion = 40;
    public const uint SetClientName = 50;
    public const uint RgbControllerUpdateLeds = 1050;
    public const uint RgbControllerUpdateMode = 1101;
}

internal sealed record OpenRgbZone(string Name, uint Type, uint LedsCount);

internal sealed record OpenRgbMode(int Index, string Name, uint Value, byte[] RawBytes);

internal sealed record OpenRgbController(
    string Name,
    string Description,
    string Version,
    string Serial,
    uint LedCount,
    IReadOnlyList<OpenRgbZone> Zones,
    IReadOnlyList<OpenRgbMode> Modes,
    int ActiveMode);

internal sealed class OpenRgbBinaryReader(byte[] buffer)
{
    private int _offset;

    public uint ReadUInt32() => BitConverter.ToUInt32(Advance(4));

    public ushort ReadUInt16() => BitConverter.ToUInt16(Advance(2));

    public byte ReadByte() => Advance(1)[0];

    public string ReadLengthPrefixedString()
    {
        var length = ReadUInt16();
        if (length == 0)
        {
            return string.Empty;
        }

        var bytes = Advance(length);
        // OpenRGB strings are null-terminated within their declared length.
        var nullIndex = Array.IndexOf(bytes, (byte)0);
        var effectiveLength = nullIndex >= 0 ? nullIndex : bytes.Length;
        return Encoding.ASCII.GetString(bytes, 0, effectiveLength);
    }

    public void Skip(int count) => Advance(count);

    public bool HasMore => _offset < buffer.Length;

    public int Position => _offset;

    public byte[] Slice(int start, int end) => buffer[start..end];

    private byte[] Advance(int count)
    {
        var slice = buffer[_offset..(_offset + count)];
        _offset += count;
        return slice;
    }
}

internal static class OpenRgbControllerParser
{
    public static OpenRgbController Parse(byte[] data)
    {
        var reader = new OpenRgbBinaryReader(data);
        reader.Skip(4); // data_size (already known from packet header)

        var deviceType = reader.ReadUInt32();
        var name = reader.ReadLengthPrefixedString();
        var _vendor = reader.ReadLengthPrefixedString();
        var description = reader.ReadLengthPrefixedString();
        var version = reader.ReadLengthPrefixedString();
        var serial = reader.ReadLengthPrefixedString();
        var _location = reader.ReadLengthPrefixedString();

        var modeCount = reader.ReadUInt16();
        var activeMode = (int)reader.ReadUInt32();
        var modes = new List<OpenRgbMode>();
        for (var i = 0; i < modeCount; i++)
        {
            var modeStart = reader.Position;
            var modeName = reader.ReadLengthPrefixedString();
            var value = reader.ReadUInt32();
            var flags = reader.ReadUInt32();
            reader.Skip(4 * 6); // speed_min/max, colors_min/max, speed, direction — not needed for mode listing
            var colorMode = reader.ReadUInt32();
            var colorCount = reader.ReadUInt16();
            reader.Skip((int)colorCount * 4);
            _ = flags;
            _ = colorMode;

            // Captured verbatim so a mode can be re-selected later by resending this exact
            // block with only the surrounding UPDATEMODE index changed — OpenRGB's protocol
            // has no "just activate mode N" packet, only "here is the full mode struct".
            var rawBytes = reader.Slice(modeStart, reader.Position);
            modes.Add(new OpenRgbMode(i, modeName, value, rawBytes));
        }

        var zoneCount = reader.ReadUInt16();
        var zones = new List<OpenRgbZone>();
        for (var i = 0; i < zoneCount; i++)
        {
            var zoneName = reader.ReadLengthPrefixedString();
            var zoneType = reader.ReadUInt32();
            reader.Skip(4 + 4); // leds_min, leds_max
            var ledsCount = reader.ReadUInt32();
            var matrixLength = reader.ReadUInt16();
            reader.Skip(matrixLength);
            zones.Add(new OpenRgbZone(zoneName, zoneType, ledsCount));
        }

        var ledCount = reader.ReadUInt16();
        for (var i = 0; i < ledCount; i++)
        {
            reader.ReadLengthPrefixedString(); // led name
            reader.Skip(4); // led value
        }

        var colorCountTotal = reader.ReadUInt16();
        reader.Skip(colorCountTotal * 4);

        _ = deviceType;

        return new OpenRgbController(name, description, version, serial, ledCount, zones, modes, activeMode);
    }
}
