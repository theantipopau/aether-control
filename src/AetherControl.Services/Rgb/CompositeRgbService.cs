using System.Collections.Concurrent;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Merges every RGB backend Aether Control knows about behind the single <see cref="IRgbService"/>
/// contract the UI already binds to — OpenRGB (needs its server running) and now Matt's own
/// direct-HID Corsair implementation (needs nothing installed at all). Adding a backend means
/// adding a case here, not touching <c>RgbViewModel</c>/<c>RgbPage</c>.
/// </summary>
public sealed class CompositeRgbService(OpenRgbService openRgb, CorsairHidDirectService corsairDirect) : IRgbService
{
    private readonly ConcurrentDictionary<string, RgbBackend> _deviceBackends = new();

    public async Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync(CancellationToken ct = default)
    {
        _deviceBackends.Clear();

        var openRgbDevices = await openRgb.DiscoverDevicesAsync(ct).ConfigureAwait(false);
        var corsairDevices = await corsairDirect.DiscoverDevicesAsync().ConfigureAwait(false);

        var all = new List<RgbDeviceInfo>(openRgbDevices.Count + corsairDevices.Count);
        all.AddRange(openRgbDevices);
        all.AddRange(corsairDevices);

        foreach (var device in all)
        {
            _deviceBackends[device.Id] = device.Backend;
        }

        return all;
    }

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b, CancellationToken ct = default) =>
        _deviceBackends.GetValueOrDefault(deviceId) == RgbBackend.CorsairDirect
            ? corsairDirect.SetColorAsync(deviceId, r, g, b)
            : openRgb.SetColorAsync(deviceId, r, g, b, ct);

    public Task SetBrightnessAsync(string deviceId, int percent, CancellationToken ct = default)
    {
        if (_deviceBackends.GetValueOrDefault(deviceId) == RgbBackend.CorsairDirect)
        {
            corsairDirect.SetBrightness(percent);
            return Task.CompletedTask;
        }

        return openRgb.SetBrightnessAsync(deviceId, percent, ct);
    }

    public Task SetEffectAsync(string deviceId, string effectName, CancellationToken ct = default) =>
        _deviceBackends.GetValueOrDefault(deviceId) == RgbBackend.CorsairDirect
            ? corsairDirect.SetEffectAsync(deviceId, effectName)
            : openRgb.SetEffectAsync(deviceId, effectName, ct);

    public bool IsAuraSyncInstalled() => openRgb.IsAuraSyncInstalled();

    public void LaunchAuraSync() => openRgb.LaunchAuraSync();
}
