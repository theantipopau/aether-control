using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherControl.App.ViewModels;

public sealed partial class RgbViewModel : ObservableObject
{
    private readonly IRgbService _rgbService;

    [ObservableProperty] private IReadOnlyList<RgbDeviceInfo> devices = [];
    [ObservableProperty] private bool isScanning;
    [ObservableProperty] private bool auraSyncInstalled;

    public RgbViewModel(IRgbService rgbService, IPeripheralDetectionService peripheralDetection)
    {
        _rgbService = rgbService;
        AuraSyncInstalled = _rgbService.IsAuraSyncInstalled();
        // Detection (what's plugged in) and control (what OpenRGB can drive) are separate problems —
        // this surfaces detected hardware even for devices Aether Control can't yet control natively.
        DetectedPeripherals = peripheralDetection.Detect();
        _ = ScanAsync();
    }

    public IReadOnlyList<DetectedPeripheralInfo> DetectedPeripherals { get; }

    public bool HasDetectedPeripherals => DetectedPeripherals.Count > 0;

    [RelayCommand]
    private async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            Devices = await _rgbService.DiscoverDevicesAsync();
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private Task SetCyanAsync(RgbDeviceInfo device) => _rgbService.SetColorAsync(device.Id, 0, 229, 255);

    [RelayCommand]
    private Task SetWhiteAsync(RgbDeviceInfo device) => _rgbService.SetColorAsync(device.Id, 255, 255, 255);

    [ObservableProperty] private string statusMessage = string.Empty;

    /// <summary>Any colour from the swatch row or picker. Reports the backend's failure instead of
    /// claiming success on an exception — OpenRGB can drop a device between scan and write.</summary>
    public async Task SetColorAsync(RgbDeviceInfo device, byte r, byte g, byte b)
    {
        try
        {
            await _rgbService.SetColorAsync(device.Id, r, g, b);
            // "Sent", not "set": the backends write without a read-back, so success is the write completing.
            StatusMessage = $"Sent #{r:X2}{g:X2}{b:X2} to {device.Name}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't set {device.Name}: {ex.Message}";
        }
    }

    [RelayCommand]
    private void LaunchAuraSync() => _rgbService.LaunchAuraSync();
}
