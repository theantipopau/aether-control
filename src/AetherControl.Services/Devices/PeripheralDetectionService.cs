using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using HidSharp;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Devices;

/// <summary>
/// Answers "what gaming peripherals are plugged in" without any vendor software
/// installed and without OpenRGB: every USB HID device advertises a Vendor ID in
/// its own descriptor, which Windows exposes to any process via SetupAPI (HidSharp
/// wraps that). No iCUE/G HUB/Synapse/Armoury Crate needed to see that a device is
/// there — only to control its RGB, which is a separate, per-device-protocol problem
/// tracked in <see cref="Rgb.OpenRgbService"/>. This only reads descriptors, never
/// writes, so it's safe to run continuously without contending with vendor software.
/// </summary>
public sealed class PeripheralDetectionService : IPeripheralDetectionService
{
    // USB-IF assigned vendor IDs for brands explicitly called out as targets.
    private static readonly Dictionary<int, string> KnownVendors = new()
    {
        [0x1B1C] = "Corsair",
        [0x046D] = "Logitech",
        [0x1532] = "Razer",
        [0x1038] = "SteelSeries",
        [0x0B05] = "ASUS",
        [0x258A] = "Glorious",
        [0x0951] = "HyperX / Kingston",
        [0x3554] = "Corsair (iCUE Link)"
    };

    private readonly ILogger<PeripheralDetectionService> _logger;

    public PeripheralDetectionService(ILogger<PeripheralDetectionService> logger)
    {
        _logger = logger;
    }

    public IReadOnlyList<DetectedPeripheralInfo> Detect()
    {
        var results = new List<DetectedPeripheralInfo>();
        try
        {
            // Group by (vendor, product): a single physical device commonly exposes several
            // HID interfaces (keyboard + consumer-control + vendor-proprietary RGB channel),
            // which would otherwise show up as duplicate entries for the same product.
            var devices = DeviceList.Local.GetHidDevices()
                .Where(d => KnownVendors.ContainsKey(d.VendorID))
                .GroupBy(d => (d.VendorID, d.ProductID));

            foreach (var group in devices)
            {
                var device = group.First();
                var productName = TryGetProductName(device);
                results.Add(new DetectedPeripheralInfo
                {
                    VendorName = KnownVendors[device.VendorID],
                    // Confirmed on real hardware: a proprietary/vendor-tool-only interface often
                    // returns an empty string rather than throwing, which "?? fallback" alone misses.
                    ProductName = string.IsNullOrWhiteSpace(productName) ? "Unknown device" : productName,
                    VendorId = device.VendorID,
                    ProductId = device.ProductID,
                    DeviceKind = "HID"
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Peripheral detection failed");
        }

        return results;
    }

    private static string? TryGetProductName(HidDevice device)
    {
        try
        {
            return device.GetProductName();
        }
        catch (Exception)
        {
            // Some HID interfaces (esp. vendor-proprietary ones used for RGB/DPI control)
            // refuse the string descriptor request outright — fall back to the vendor label.
            return null;
        }
    }
}
