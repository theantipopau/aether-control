using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Native control of the ASUS motherboard "AURA LED Controller" (USB HID, VID 0x0B05) — onboard LEDs,
/// 12 V RGB headers and ARGB headers — without Armoury Crate or OpenRGB. This is what lets Matt
/// uninstall Armoury Crate: the test rig's PRIME B650EM-A WIFI exposes it as PID 0x19AF
/// (PnP: "AURA LED Controller", 2026-11-14).
/// <para>
/// Wire format (independent implementation; protocol facts as documented by OpenRGB's
/// AsusAuraUSBController/AsusAuraMainboardController): 65-byte output reports, report ID 0xEC.
/// 0x82 firmware string (reply byte 1 = 0x02), 0xB0 config table (reply byte 1 = 0x30, table from
/// byte 4: [0x02] ARGB headers, [0x1B] mainboard LEDs, [0x1D] 12 V RGB headers), 0x52 0x53 0x00 0x01
/// init, 0x35 set effect (channel, shutdown flag, mode), 0x36 set effect colour (16-bit LED mask,
/// shutdown flag, RGB triples from byte 5 at 3 × LED index), 0x3F 0x55 commit to flash.
/// </para>
/// <para>
/// Colour changes are NOT committed to the controller's flash — they last until power-off. Commit
/// only happens on an explicit "Save to board" (<see cref="SaveToDeviceAsync"/>), so routine colour
/// clicks never wear the flash. ASUS's own LightingService rewrites the controller while it runs,
/// so changes made here get overridden until it's stopped; discovery says so on the device.
/// </para>
/// </summary>
public sealed class AsusAuraUsbService(ILogger<AsusAuraUsbService> logger)
{
    private const int AsusVendorId = 0x0B05;
    private const int ReportLength = 65;
    private const byte ReportId = 0xEC;

    // Mainboard Aura controller PIDs (X570 onward), same set OpenRGB treats as mainboard controllers.
    private static readonly HashSet<int> MainboardProductIds = [0x18F3, 0x1939, 0x19AF, 0x1AA6, 0x1BED];

    private sealed record TrackedController(HidDevice Device, AuraLayout Layout, string Firmware)
    {
        public (byte R, byte G, byte B) LastColor { get; set; } = (0, 229, 255);
    }

    private readonly ConcurrentDictionary<string, TrackedController> _controllers = new();
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private int _brightness = 100;

    public Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync() => Task.Run(Discover);

    private IReadOnlyList<RgbDeviceInfo> Discover()
    {
        _controllers.Clear();
        var results = new List<RgbDeviceInfo>();
        try
        {
            var lightingServiceRunning = Process.GetProcessesByName("LightingService").Length > 0;
            foreach (var device in DeviceList.Local.GetHidDevices(AsusVendorId))
            {
                // The controller is a composite device; only its vendor-defined interface takes
                // 65-byte 0xEC reports (PID 0x19AF: usage page 0xFF72).
                if (!MainboardProductIds.Contains(device.ProductID) || device.GetMaxOutputReportLength() != ReportLength)
                {
                    continue;
                }

                var probe = Probe(device);
                if (probe is null)
                {
                    continue;
                }

                var (firmware, layout) = probe.Value;
                var id = $"asus-aura:{device.ProductID:X4}";
                _controllers[id] = new TrackedController(device, layout, firmware);
                results.Add(new RgbDeviceInfo
                {
                    Id = id,
                    Name = "ASUS Aura motherboard lighting",
                    Backend = RgbBackend.AsusAuraDirect,
                    DeviceType = $"{layout.Describe()} · firmware {firmware}",
                    LedCount = layout.Zones.Sum(z => z.LedCount),
                    SupportedModes = [.. AuraModes.Keys],
                    CanSaveToDevice = true,
                    Warning = lightingServiceRunning
                        ? "Armoury Crate's lighting service (LightingService) is running and will override changes made here. Stop it to take control."
                        : string.Empty
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ASUS Aura USB discovery failed");
        }

        return results;
    }

    /// <summary>Reads firmware + config table. Read-only: proves the protocol matches before any write.</summary>
    private (string Firmware, AuraLayout Layout)? Probe(HidDevice device)
    {
        try
        {
            using var stream = device.Open();
            stream.ReadTimeout = 1000;
            var firmwareReply = Request(stream, AuraPackets.FirmwareRequest());
            var configReply = Request(stream, AuraPackets.ConfigTableRequest());
            var firmware = AuraPackets.ParseFirmware(firmwareReply) ?? "unknown";
            var table = AuraPackets.ParseConfigTable(configReply);
            if (table is null)
            {
                logger.LogInformation("ASUS Aura PID 0x{Pid:X4}: no config table reply; not offering control", device.ProductID);
                return null;
            }

            var layout = AuraLayout.FromConfigTable(table, firmware);
            logger.LogInformation("ASUS Aura PID 0x{Pid:X4} firmware {Firmware}: {Layout}; config {Table}",
                device.ProductID, firmware, layout.Describe(), Convert.ToHexString(table));
            return (firmware, layout);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "ASUS Aura PID 0x{Pid:X4} probe failed", device.ProductID);
            return null;
        }
    }

    private static byte[] Request(HidStream stream, byte[] request)
    {
        stream.Write(request);
        // Skip unrelated input reports; the reply to our request is the next 0xEC report.
        for (var i = 0; i < 4; i++)
        {
            var reply = stream.Read();
            if (reply.Length > 1 && reply[0] == ReportId)
            {
                return reply;
            }
        }

        return [];
    }

    public void SetBrightness(int percent) => _brightness = Math.Clamp(percent, 0, 100);

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b)
    {
        if (!_controllers.TryGetValue(deviceId, out var controller))
        {
            return Task.CompletedTask;
        }

        controller.LastColor = (r, g, b);
        var isOff = r == 0 && g == 0 && b == 0;
        return SendAsync(controller, isOff ? AuraMode.Off : AuraMode.Static);
    }

    public Task SetEffectAsync(string deviceId, string effectName) =>
        _controllers.TryGetValue(deviceId, out var controller) && AuraModes.TryGetValue(effectName, out var mode)
            ? SendAsync(controller, mode)
            : Task.CompletedTask;

    /// <summary>Commits the current lighting to the controller's flash so it survives reboots
    /// without any software running. Explicit user action only (flash wear).</summary>
    public Task SaveToDeviceAsync(string deviceId) =>
        _controllers.TryGetValue(deviceId, out var controller)
            ? WriteAsync(controller, [AuraPackets.Commit()])
            : Task.CompletedTask;

    private Task SendAsync(TrackedController controller, byte mode)
    {
        var (r, g, b) = controller.LastColor;
        var scale = _brightness / 100.0;
        var color = ((byte)(r * scale), (byte)(g * scale), (byte)(b * scale));
        return WriteAsync(controller, AuraPackets.SetAllZones(controller.Layout, mode, color));
    }

    private async Task WriteAsync(TrackedController controller, IEnumerable<byte[]> reports)
    {
        await _ioLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                using var stream = controller.Device.Open();
                stream.Write(AuraPackets.Init());
                foreach (var report in reports)
                {
                    stream.Write(report);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Surface, don't swallow: RgbViewModel reports the failure instead of "Sent".
            logger.LogWarning(ex, "ASUS Aura write failed");
            throw;
        }
        finally
        {
            _ioLock.Release();
        }
    }

    internal static readonly IReadOnlyDictionary<string, byte> AuraModes = new Dictionary<string, byte>
    {
        ["Static"] = AuraMode.Static,
        ["Breathing"] = AuraMode.Breathing,
        ["Flashing"] = AuraMode.Flashing,
        ["Spectrum"] = AuraMode.SpectrumCycle,
        ["Rainbow"] = AuraMode.Rainbow,
        ["Off"] = AuraMode.Off,
    };
}

internal static class AuraMode
{
    public const byte Off = 0, Static = 1, Breathing = 2, Flashing = 3, SpectrumCycle = 4, Rainbow = 5;
}

/// <summary>One effect channel: the mainboard zone (onboard LEDs + 12 V headers) or an ARGB header.
/// <paramref name="StartLed"/>/<paramref name="LedCount"/> locate it in the colour packet's LED mask.</summary>
internal sealed record AuraZone(string Name, byte EffectChannel, int StartLed, int LedCount);

internal sealed record AuraLayout(IReadOnlyList<AuraZone> Zones, int MainboardLeds, int RgbHeaders, int ArgbHeaders)
{
    public static AuraLayout FromConfigTable(byte[] table, string firmware)
    {
        int mainboardLeds = table[0x1B], rgbHeaders = table[0x1D], argbHeaders = table[0x02];
        if (mainboardLeds < rgbHeaders)
        {
            rgbHeaders = 0;
        }

        // AULA3-AR32 firmware numbers ARGB effect channels from 0 with the mainboard after them;
        // other firmwares put the mainboard on channel 0 and ARGB headers from 1.
        var argbFromZero = firmware.StartsWith("AULA3-AR32", StringComparison.Ordinal);
        var zones = new List<AuraZone>();
        byte nextChannel = 0;
        var nextLed = 0;
        if (mainboardLeds > 0)
        {
            zones.Add(new AuraZone("Mainboard", argbFromZero ? (byte)argbHeaders : nextChannel, 0, mainboardLeds));
            nextLed = mainboardLeds;
            if (!argbFromZero)
            {
                nextChannel++;
            }
        }

        // In effect mode each ARGB header takes one colour slot after the mainboard LEDs.
        for (var i = 0; i < argbHeaders; i++)
        {
            zones.Add(new AuraZone($"ARGB header {i + 1}", nextChannel++, nextLed++, 1));
        }

        return new AuraLayout(zones, mainboardLeds, rgbHeaders, argbHeaders);
    }

    public string Describe() =>
        $"{MainboardLeds} onboard LED(s), {RgbHeaders} RGB header(s), {ArgbHeaders} ARGB header(s)";
}

internal static class AuraPackets
{
    private static byte[] Report(params byte[] payload)
    {
        var report = new byte[65];
        report[0] = 0xEC;
        payload.CopyTo(report, 1);
        return report;
    }

    public static byte[] FirmwareRequest() => Report(0x82);
    public static byte[] ConfigTableRequest() => Report(0xB0);
    public static byte[] Init() => Report(0x52, 0x53, 0x00, 0x01);
    public static byte[] Commit() => Report(0x3F, 0x55);

    public static byte[] Effect(byte channel, byte mode) => Report(0x35, channel, 0x00, 0x00, mode);

    public static byte[] Color(int startLed, int ledCount, (byte R, byte G, byte B) color)
    {
        var mask = (ushort)(((1 << ledCount) - 1) << startLed);
        var report = Report(0x36, (byte)(mask >> 8), (byte)(mask & 0xFF), 0x00);
        for (var led = startLed; led < startLed + ledCount && 5 + led * 3 + 2 < report.Length; led++)
        {
            report[5 + led * 3] = color.R;
            report[6 + led * 3] = color.G;
            report[7 + led * 3] = color.B;
        }

        return report;
    }

    /// <summary>Effect + colour packet per zone. Colour is skipped for Off and the colour-cycling
    /// modes, which ignore it.</summary>
    public static IEnumerable<byte[]> SetAllZones(AuraLayout layout, byte mode, (byte R, byte G, byte B) color)
    {
        var usesColor = mode is AuraMode.Static or AuraMode.Breathing or AuraMode.Flashing;
        foreach (var zone in layout.Zones)
        {
            yield return Effect(zone.EffectChannel, mode);
            // ponytail: the colour packet's LED mask is 16 bits; a board with more slots than that
            // gets its effect but keeps its previous colour. Needs direct mode (0x40) if one turns up.
            if (usesColor && zone.StartLed + zone.LedCount <= 16)
            {
                yield return Color(zone.StartLed, zone.LedCount, color);
            }
        }
    }

    public static string? ParseFirmware(byte[] reply) =>
        reply.Length >= 18 && reply[0] == 0xEC && reply[1] == 0x02
            ? Encoding.ASCII.GetString(reply, 2, 16).TrimEnd('\0', ' ')
            : null;

    public static byte[]? ParseConfigTable(byte[] reply) =>
        reply.Length >= 64 && reply[0] == 0xEC && reply[1] == 0x30 ? reply[4..64] : null;
}
