using System.Collections.Concurrent;
using System.Net.Sockets;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Rgb;

/// <summary>
/// RGB ecosystem entry point. Devices reachable through the OpenRGB SDK
/// server (motherboards, RAM, fans, strips, keyboards, mice — whatever
/// OpenRGB itself detects) are controlled directly over its network
/// protocol. ASUS Aura Sync is detected and can be launched, but per the
/// spec its proprietary control surface is never reimplemented here.
/// </summary>
public sealed class OpenRgbService(ILogger<OpenRgbService> logger) : IRgbService
{
    private readonly ConcurrentDictionary<string, (uint DeviceId, OpenRgbController Controller)> _devices = new();

    public async Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync(CancellationToken ct = default)
    {
        _devices.Clear();
        var results = new List<RgbDeviceInfo>();

        try
        {
            await using var client = new OpenRgbClient();
            await client.ConnectAsync(ct).ConfigureAwait(false);

            var count = await client.GetControllerCountAsync(ct).ConfigureAwait(false);
            for (uint i = 0; i < count; i++)
            {
                var controller = await client.GetControllerDataAsync(i, ct).ConfigureAwait(false);
                var id = $"openrgb:{i}";
                _devices[id] = (i, controller);

                results.Add(new RgbDeviceInfo
                {
                    Id = id,
                    Name = controller.Name,
                    Backend = RgbBackend.OpenRgb,
                    DeviceType = controller.Description,
                    LedCount = (int)controller.LedCount,
                    SupportedModes = controller.Modes.Select(m => m.Name).ToList()
                });
            }
        }
        catch (Exception ex) when (ex is SocketException or IOException or OperationCanceledException)
        {
            logger.LogInformation(ex, "OpenRGB server not reachable at 127.0.0.1:6742 — is OpenRGB running with its SDK server enabled?");
        }

        return results;
    }

    public async Task SetColorAsync(string deviceId, byte r, byte g, byte b, CancellationToken ct = default)
    {
        if (!_devices.TryGetValue(deviceId, out var entry))
        {
            return;
        }

        await using var client = new OpenRgbClient();
        await client.ConnectAsync(ct).ConfigureAwait(false);
        await client.SetSolidColorAsync(entry.DeviceId, entry.Controller.LedCount, r, g, b, ct).ConfigureAwait(false);
    }

    public async Task SetBrightnessAsync(string deviceId, int percent, CancellationToken ct = default)
    {
        // OpenRGB's protocol has no standalone brightness channel; brightness is expressed by
        // scaling the RGB values themselves, so this dims relative to full white.
        var scale = Math.Clamp(percent, 0, 100) / 100.0;
        var value = (byte)Math.Round(255 * scale);
        await SetColorAsync(deviceId, value, value, value, ct).ConfigureAwait(false);
    }

    public async Task SetEffectAsync(string deviceId, string effectName, CancellationToken ct = default)
    {
        if (!_devices.TryGetValue(deviceId, out var entry))
        {
            return;
        }

        var mode = entry.Controller.Modes.FirstOrDefault(m => string.Equals(m.Name, effectName, StringComparison.OrdinalIgnoreCase));
        if (mode is null)
        {
            logger.LogWarning("Mode '{Mode}' not found on device {Device}", effectName, deviceId);
            return;
        }

        await using var client = new OpenRgbClient();
        await client.ConnectAsync(ct).ConfigureAwait(false);
        await client.SetModeAsync(entry.DeviceId, mode, ct).ConfigureAwait(false);
    }

    public bool IsAuraSyncInstalled() => AuraSyncDetectionService.IsInstalled();

    public void LaunchAuraSync() => AuraSyncDetectionService.Launch();
}
