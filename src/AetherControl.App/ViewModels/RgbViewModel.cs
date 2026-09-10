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

    [RelayCommand]
    private void LaunchAuraSync() => _rgbService.LaunchAuraSync();
}
