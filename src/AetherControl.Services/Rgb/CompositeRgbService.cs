using System.Collections.Concurrent;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;

namespace AetherControl.Services.Rgb;

/// <summary>
/// Merges every RGB backend Aether Control knows about behind the single <see cref="IRgbService"/>
/// contract the UI already binds to — OpenRGB (needs its server running), Matt's own direct-HID
/// Corsair implementation, and the native ASUS Aura motherboard controller (both need nothing
/// installed). Adding a backend means adding a case here, not touching <c>RgbViewModel</c>/<c>RgbPage</c>.
/// </summary>
public sealed class CompositeRgbService(OpenRgbService openRgb, CorsairHidDirectService corsairDirect, AsusAuraUsbService asusAura) : IRgbService
{
    private readonly ConcurrentDictionary<string, RgbBackend> _deviceBackends = new();

    public async Task<IReadOnlyList<RgbDeviceInfo>> DiscoverDevicesAsync(CancellationToken ct = default)
    {
        _deviceBackends.Clear();

        var openRgbDevices = await openRgb.DiscoverDevicesAsync(ct).ConfigureAwait(false);
        var corsairDevices = await corsairDirect.DiscoverDevicesAsync().ConfigureAwait(false);
        var auraDevices = await asusAura.DiscoverDevicesAsync().ConfigureAwait(false);

        var all = new List<RgbDeviceInfo>(openRgbDevices.Count + corsairDevices.Count + auraDevices.Count);
        all.AddRange(auraDevices);
        all.AddRange(corsairDevices);
        all.AddRange(openRgbDevices);

        foreach (var device in all)
        {
            _deviceBackends[device.Id] = device.Backend;
        }

        return all;
    }

    private RgbBackend BackendOf(string deviceId) => _deviceBackends.GetValueOrDefault(deviceId);

    public Task SetColorAsync(string deviceId, byte r, byte g, byte b, CancellationToken ct = default) => BackendOf(deviceId) switch
    {
        RgbBackend.CorsairDirect => corsairDirect.SetColorAsync(deviceId, r, g, b),
        RgbBackend.AsusAuraDirect => asusAura.SetColorAsync(deviceId, r, g, b),
        _ => openRgb.SetColorAsync(deviceId, r, g, b, ct)
    };

    public Task SetBrightnessAsync(string deviceId, int percent, CancellationToken ct = default)
    {
        switch (BackendOf(deviceId))
        {
            case RgbBackend.CorsairDirect:
                corsairDirect.SetBrightness(percent);
                return Task.CompletedTask;
            case RgbBackend.AsusAuraDirect:
                asusAura.SetBrightness(percent);
                return Task.CompletedTask;
            default:
                return openRgb.SetBrightnessAsync(deviceId, percent, ct);
        }
    }

    public Task SetEffectAsync(string deviceId, string effectName, CancellationToken ct = default) => BackendOf(deviceId) switch
    {
        RgbBackend.CorsairDirect => corsairDirect.SetEffectAsync(deviceId, effectName),
        RgbBackend.AsusAuraDirect => asusAura.SetEffectAsync(deviceId, effectName),
        _ => openRgb.SetEffectAsync(deviceId, effectName, ct)
    };

    public Task SaveToDeviceAsync(string deviceId, CancellationToken ct = default) =>
        BackendOf(deviceId) == RgbBackend.AsusAuraDirect ? asusAura.SaveToDeviceAsync(deviceId) : Task.CompletedTask;

    public bool IsAuraSyncInstalled() => openRgb.IsAuraSyncInstalled();

    public void LaunchAuraSync() => openRgb.LaunchAuraSync();
}
