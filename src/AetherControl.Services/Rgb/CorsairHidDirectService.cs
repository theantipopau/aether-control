using System.Collections.Concurrent;
using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Direct-HID Corsair RGB/DPI control — no iCUE, no OpenRGB. Ported from Matt's own OmenCore
/// project (<c>OmenCoreApp/Services/Corsair/CorsairHidDirect.cs</c>), which talks to Corsair's own
/// vendor-specific USB HID report format directly. Unlike porting OpenRGB's own device protocol
/// code, there's no GPL question here at all — this is Matt's original, previously-shipped,
/// already-tested implementation, not a derivative of someone else's copyleft codebase.
/// Report byte layouts below are per-product heuristics learned empirically in OmenCore, not from
/// any public Corsair spec — that's why they're organised by exact PID rather than derived generically.
/// </summary>
public sealed class CorsairHidDirectService
{
    private const int CorsairVendorId = 0x1B1C;
    private const int WriteMaxAttempts = 3;
    private const int WriteRetryDelayMs = 120;

    // Known Corsair product IDs — carried over verbatim from OmenCore, where this list was built up
    // against Matt's own real hardware and bug reports rather than guessed from a spec sheet.
    private static readonly Dictionary<int, (string Name, RgbCorsairDeviceType Type)> KnownProducts = new()
    {
        { 0x1B2D, ("K95 RGB Platinum", RgbCorsairDeviceType.Keyboard) },
        { 0x1B11, ("K70 RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B13, ("K70 LUX RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B17, ("K70 RGB MK.2", RgbCorsairDeviceType.Keyboard) },
        { 0x1B36, ("K70 RGB MK.2 SE", RgbCorsairDeviceType.Keyboard) },
        { 0x1B49, ("K70 RGB MK.2 LP", RgbCorsairDeviceType.Keyboard) },
        { 0x1B38, ("K70 RGB TKL", RgbCorsairDeviceType.Keyboard) },
        { 0x1B55, ("K70 RGB PRO", RgbCorsairDeviceType.Keyboard) },
        { 0x1B6B, ("K70 RGB PRO X", RgbCorsairDeviceType.Keyboard) },
        { 0x1B4F, ("K65 RGB MINI", RgbCorsairDeviceType.Keyboard) },
        { 0x1B39, ("K65 RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B37, ("K65 LUX RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B07, ("K65 RGB Rapidfire", RgbCorsairDeviceType.Keyboard) },
        { 0x1B09, ("K55 RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B3D, ("K55 RGB PRO", RgbCorsairDeviceType.Keyboard) },
        { 0x1B6E, ("K55 RGB PRO XT", RgbCorsairDeviceType.Keyboard) },
        { 0x1B60, ("K100 RGB", RgbCorsairDeviceType.Keyboard) },
        { 0x1B2E, ("Dark Core RGB", RgbCorsairDeviceType.Mouse) },
        { 0x1B4B, ("Dark Core RGB PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B4C, ("Dark Core RGB PRO SE", RgbCorsairDeviceType.Mouse) },
        { 0x1B80, ("Dark Core RGB PRO Wireless", RgbCorsairDeviceType.Mouse) },
        { 0x1BF0, ("Dark Core RGB PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B34, ("Ironclaw RGB", RgbCorsairDeviceType.Mouse) },
        { 0x1B3C, ("Nightsword RGB", RgbCorsairDeviceType.Mouse) },
        { 0x1B1E, ("M65 RGB Elite", RgbCorsairDeviceType.Mouse) },
        { 0x1B12, ("M65 PRO RGB", RgbCorsairDeviceType.Mouse) },
        { 0x1B5B, ("M55 RGB PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B3F, ("Harpoon RGB PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B75, ("Sabre RGB PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B66, ("Katar PRO", RgbCorsairDeviceType.Mouse) },
        { 0x1B6F, ("Katar PRO XT", RgbCorsairDeviceType.Mouse) },
        { 0x1B3B, ("Scimitar PRO RGB", RgbCorsairDeviceType.Mouse) },
        { 0x1B8B, ("Scimitar Elite Wireless", RgbCorsairDeviceType.Mouse) },
        { 0x1B7A, ("Sabre RGB PRO Champion", RgbCorsairDeviceType.Mouse) },
        { 0x1B96, ("M65 RGB Ultra", RgbCorsairDeviceType.Mouse) },
        { 0x1B99, ("M65 RGB Ultra Wireless", RgbCorsairDeviceType.Mouse) },
        { 0x1BA4, ("Dark Core RGB PRO SE Wireless", RgbCorsairDeviceType.Mouse) },
        { 0x1B81, ("Dark Core RGB PRO Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B5D, ("Ironclaw RGB Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B3E, ("Harpoon RGB Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B65, ("Katar PRO Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B9A, ("M65 RGB Ultra Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B8C, ("Scimitar Elite Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x1B94, ("Sabre RGB PRO Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x0A14, ("VOID RGB", RgbCorsairDeviceType.Headset) },
        { 0x0A55, ("VOID RGB Elite Wireless", RgbCorsairDeviceType.Headset) },
        { 0x0A52, ("VOID RGB Elite USB", RgbCorsairDeviceType.Headset) },
        { 0x0A4E, ("HS70 PRO Wireless Receiver", RgbCorsairDeviceType.WirelessDongle) },
        { 0x0A4F, ("HS70 PRO Wireless", RgbCorsairDeviceType.Headset) },
        { 0x0A51, ("HS60 PRO", RgbCorsairDeviceType.Headset) },
        { 0x0A61, ("Virtuoso RGB Wireless", RgbCorsairDeviceType.Headset) },
        { 0x0A64, ("Virtuoso RGB Wireless SE", RgbCorsairDeviceType.Headset) },
        { 0x0A6A, ("Virtuoso RGB Wireless XT", RgbCorsairDeviceType.Headset) },
        { 0x0B00, ("MM800 RGB Polaris", RgbCorsairDeviceType.MouseMat) },
        { 0x0B04, ("MM800 RGB Polaris Cloth", RgbCorsairDeviceType.MouseMat) },
        { 0x0B05, ("MM1000 Qi Wireless", RgbCorsairDeviceType.MouseMat) },
        { 0x0C00, ("ST100 RGB Headset Stand", RgbCorsairDeviceType.Accessory) },
        { 0x1D00, ("LT100 RGB Tower", RgbCorsairDeviceType.Accessory) },
        { 0x0A34, ("Commander PRO", RgbCorsairDeviceType.Accessory) },
        { 0x0A3E, ("Lighting Node PRO", RgbCorsairDeviceType.Accessory) },
    };

    private enum RgbCorsairDeviceType { Keyboard, Mouse, Headset, MouseMat, Accessory, WirelessDongle }

    private sealed record TrackedDevice(HidDevice HidDevice, int ProductId, RgbCorsairDeviceType Type, string Name);

    private readonly ILogger<CorsairHidDirectService> _logger;
    private readonly ConcurrentDictionary<string, TrackedDevice> _devices = new();
    private int _brightness = 100;

    public CorsairHidDirectService(ILogger<CorsairHidDirectService> logger)
    {
        _logger = logger;
    }

    public Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync()
    {
        _devices.Clear();
        var results = new List<RgbDeviceInfo>();
        var seenProducts = new HashSet<int>();

        try
        {
            foreach (var hidDevice in DeviceList.Local.GetHidDevices())
            {
                if (hidDevice.VendorID != CorsairVendorId || !seenProducts.Add(hidDevice.ProductID))
                {
                    continue;
                }

                var (name, type) = KnownProducts.TryGetValue(hidDevice.ProductID, out var info)
                    ? info
                    : (TryGetDescriptorName(hidDevice) ?? $"Corsair Device (0x{hidDevice.ProductID:X4})", GuessDeviceType(hidDevice.ProductID));

                // A wireless receiver has no LEDs of its own to light — it's already visible in the
                // Detected Hardware section (Phase 11); no point offering dead Cyan/White buttons here.
                if (type == RgbCorsairDeviceType.WirelessDongle)
                {
                    continue;
                }

                var id = $"corsair-hid:{hidDevice.ProductID:X4}";
                _devices[id] = new TrackedDevice(hidDevice, hidDevice.ProductID, type, name);

                results.Add(new RgbDeviceInfo
                {
                    Id = id,
                    Name = name,
                    Backend = RgbBackend.CorsairDirect,
                    DeviceType = type.ToString(),
                    LedCount = 0,
                    SupportedModes = ["Static", "Breathing", "Spectrum", "Wave"]
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Corsair direct-HID discovery failed");
        }

        return Task.FromResult<IReadOnlyList<RgbDeviceInfo>>(results);
    }

    public void SetBrightness(int percent) => _brightness = Math.Clamp(percent, 0, 100);

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b) =>
        _devices.TryGetValue(deviceId, out var device) ? SendColorAsync(device, r, g, b) : Task.CompletedTask;

    public Task SetEffectAsync(string deviceId, string effectName)
    {
        if (!_devices.TryGetValue(deviceId, out var device))
        {
            return Task.CompletedTask;
        }

        return effectName switch
        {
            "Breathing" => SendEffectAsync(device, EffectId.Breathing, (255, 255, 255), (0, 0, 255)),
            "Spectrum" => SendEffectAsync(device, EffectId.Spectrum, (0, 0, 0), (0, 0, 0)),
            "Wave" when device.Type == RgbCorsairDeviceType.Keyboard => SendEffectAsync(device, EffectId.Wave, (0, 0, 0), (0, 0, 0)),
            "Off" => SendColorAsync(device, 0, 0, 0),
            _ => SendColorAsync(device, 255, 255, 255)
        };
    }

    private byte ApplyBrightness(byte value) => _brightness >= 100 ? value : (byte)(value * _brightness / 100);

    private async Task SendColorAsync(TrackedDevice device, byte r, byte g, byte b)
    {
        r = ApplyBrightness(r);
        g = ApplyBrightness(g);
        b = ApplyBrightness(b);

        for (var attempt = 1; attempt <= WriteMaxAttempts; attempt++)
        {
            try
            {
                await WriteReportAsync(device, BuildSetColorReport(device, r, g, b));
                await WriteReportAsync(device, BuildCommitReport(device));
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt} failed writing colour to {Device} (PID 0x{Pid:X4})", attempt, device.Name, device.ProductId);
                if (attempt < WriteMaxAttempts)
                {
                    await Task.Delay(WriteRetryDelayMs);
                }
            }
        }
    }

    private static class EffectId
    {
        public const byte Breathing = 0x02;
        public const byte Spectrum = 0x03;
        public const byte Wave = 0x04;
    }

    private async Task SendEffectAsync(TrackedDevice device, byte effectId, (byte r, byte g, byte b) primary, (byte r, byte g, byte b) secondary)
    {
        for (var attempt = 1; attempt <= WriteMaxAttempts; attempt++)
        {
            try
            {
                var report = BuildEffectReport(device, effectId, primary, secondary);
                await WriteReportAsync(device, report);
                await WriteReportAsync(device, BuildCommitReport(device));
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Attempt {Attempt} failed writing effect to {Device} (PID 0x{Pid:X4})", attempt, device.Name, device.ProductId);
                if (attempt < WriteMaxAttempts)
                {
                    await Task.Delay(WriteRetryDelayMs);
                }
                else
                {
                    // Effect report rejected (or device doesn't support SW-mode effects) — a plain
                    // static colour is always safer than leaving the device in an unknown state.
                    await SendColorAsync(device, primary.r, primary.g, primary.b);
                }
            }
        }
    }

    /// <summary>Per-product report layout heuristics, carried over from OmenCore as-is — these were
    /// learned empirically against real hardware, not derived from any public Corsair spec.</summary>
    private static byte[] BuildSetColorReport(TrackedDevice device, byte r, byte g, byte b)
    {
        if (device.Type == RgbCorsairDeviceType.Keyboard)
        {
            return BuildKeyboardFullZoneReport(device, r, g, b);
        }

        var report = new byte[65];
        report[0] = 0x00;
        report[1] = 0x05; // mouse set-colour command
        report[2] = 0x00; // start index
        report[3] = 0x01; // count
        report[4] = r;
        report[5] = g;
        report[6] = b;
        return report;
    }

    private static byte[] BuildKeyboardFullZoneReport(TrackedDevice device, byte r, byte g, byte b)
    {
        var report = new byte[65];
        report[0] = 0x00;
        report[1] = 0x09; // keyboard set command
        report[2] = 0x00;
        report[3] = 0xFF; // full-device marker
        report[4] = r;
        report[5] = g;
        report[6] = b;
        report[7] = device.ProductId == 0x1B60 ? (byte)0x02 : (byte)0x01; // K100 special vs standard full-zone

        var idx = 8;
        for (var zone = 0; zone < 4 && idx + 2 < report.Length; zone++)
        {
            report[idx++] = r;
            report[idx++] = g;
            report[idx++] = b;
        }

        return report;
    }

    private static byte[] BuildCommitReport(TrackedDevice device)
    {
        var report = new byte[65];
        report[0] = 0x00;
        report[1] = device.ProductId switch
        {
            0x1B2D or 0x1B11 or 0x1B17 or 0x1B60 => (byte)0x09,
            0x1B2E or 0x1B4B or 0x1B34 => (byte)0x05,
            _ => (byte)0x07
        };
        report[2] = 0x28;
        return report;
    }

    private static byte[] BuildEffectReport(TrackedDevice device, byte effectId, (byte r, byte g, byte b) primary, (byte r, byte g, byte b) secondary)
    {
        var report = new byte[65];
        report[0] = 0x00;
        report[1] = device.Type switch
        {
            RgbCorsairDeviceType.Keyboard => (byte)0x09,
            RgbCorsairDeviceType.Mouse => (byte)0x05,
            _ => (byte)0x07
        };
        report[2] = 0xD0; // effect-mode marker, distinguishes from plain set-colour
        report[3] = effectId;
        report[4] = primary.r;
        report[5] = primary.g;
        report[6] = primary.b;
        report[7] = secondary.r;
        report[8] = secondary.g;
        report[9] = secondary.b;
        report[10] = 0x80; // mid-speed period byte
        return report;
    }

    private static async Task WriteReportAsync(TrackedDevice device, byte[] report)
    {
        using var stream = device.HidDevice.Open();
        await stream.WriteAsync(report, 0, report.Length);
    }

    private static RgbCorsairDeviceType GuessDeviceType(int productId) => productId switch
    {
        >= 0x1B00 and <= 0x1BFF => RgbCorsairDeviceType.Mouse,
        >= 0x0A00 and <= 0x0AFF => RgbCorsairDeviceType.Headset,
        >= 0x0B00 and <= 0x0BFF => RgbCorsairDeviceType.MouseMat,
        _ => RgbCorsairDeviceType.Accessory
    };

    private static string? TryGetDescriptorName(HidDevice device)
    {
        try
        {
            var name = device.GetProductName();
            return string.IsNullOrWhiteSpace(name) ? null : name;
        }
        catch
        {
            return null;
        }
    }
}
