using System.Net.Sockets;
using System.Text;

namespace AetherControl.Services.Rgb;

/// <summary>Minimal TCP transport for the OpenRGB SDK server (default 127.0.0.1:6742).</summary>
internal sealed class OpenRgbClient(string host = "127.0.0.1", int port = 6742) : IAsyncDisposable
{
    private const string Magic = "ORGB";
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _tcpClient = new TcpClient();
        await _tcpClient.ConnectAsync(host, port, ct).ConfigureAwait(false);
        _stream = _tcpClient.GetStream();
        await SendAsync(0, OpenRgbPacketType.SetClientName, Encoding.ASCII.GetBytes("Aether Control\0"), ct).ConfigureAwait(false);
    }

    public async Task<uint> GetControllerCountAsync(CancellationToken ct = default)
    {
        await SendAsync(0, OpenRgbPacketType.RequestControllerCount, [], ct).ConfigureAwait(false);
        var (_, data) = await ReceiveAsync(ct).ConfigureAwait(false);
        return BitConverter.ToUInt32(data, 0);
    }

    public async Task<OpenRgbController> GetControllerDataAsync(uint deviceId, CancellationToken ct = default)
    {
        await SendAsync(deviceId, OpenRgbPacketType.RequestControllerData, BitConverter.GetBytes(4u), ct).ConfigureAwait(false);
        var (_, data) = await ReceiveAsync(ct).ConfigureAwait(false);
        return OpenRgbControllerParser.Parse(data);
    }

    public Task SetSolidColorAsync(uint deviceId, uint ledCount, byte r, byte g, byte b, CancellationToken ct = default)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write((ushort)ledCount);
        for (var i = 0; i < ledCount; i++)
        {
            writer.Write(r);
            writer.Write(g);
            writer.Write(b);
            writer.Write((byte)0);
        }

        return SendAsync(deviceId, OpenRgbPacketType.RgbControllerUpdateLeds, payload.ToArray(), ct);
    }

    /// <summary>
    /// Activates a mode by resending its own raw struct (captured verbatim during
    /// discovery) alongside the target index — OpenRGB has no simpler "select mode N" packet.
    /// Structurally correct per the documented protocol but not verified against a live
    /// server in this environment; treat as a starting point pending hardware validation.
    /// </summary>
    public Task SetModeAsync(uint deviceId, OpenRgbMode mode, CancellationToken ct = default)
    {
        using var payload = new MemoryStream();
        using var writer = new BinaryWriter(payload);
        writer.Write(mode.Index);
        writer.Write(mode.RawBytes);
        return SendAsync(deviceId, OpenRgbPacketType.RgbControllerUpdateMode, payload.ToArray(), ct);
    }

    private async Task SendAsync(uint deviceId, uint packetType, byte[] data, CancellationToken ct)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Not connected to OpenRGB server.");
        }

        var header = new byte[16];
        Encoding.ASCII.GetBytes(Magic).CopyTo(header, 0);
        BitConverter.GetBytes(deviceId).CopyTo(header, 4);
        BitConverter.GetBytes(packetType).CopyTo(header, 8);
        BitConverter.GetBytes((uint)data.Length).CopyTo(header, 12);

        await _stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (data.Length > 0)
        {
            await _stream.WriteAsync(data, ct).ConfigureAwait(false);
        }
    }

    private async Task<(uint PacketType, byte[] Data)> ReceiveAsync(CancellationToken ct)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Not connected to OpenRGB server.");
        }

        var header = await ReadExactAsync(16, ct).ConfigureAwait(false);
        var packetType = BitConverter.ToUInt32(header, 8);
        var length = BitConverter.ToUInt32(header, 12);
        var data = length > 0 ? await ReadExactAsync((int)length, ct).ConfigureAwait(false) : [];
        return (packetType, data);
    }

    private async Task<byte[]> ReadExactAsync(int count, CancellationToken ct)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = await _stream!.ReadAsync(buffer.AsMemory(offset, count - offset), ct).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("OpenRGB server closed the connection unexpectedly.");
            }

            offset += read;
        }

        return buffer;
    }

    public ValueTask DisposeAsync()
    {
        _stream?.Dispose();
        _tcpClient?.Dispose();
        return ValueTask.CompletedTask;
    }
}
