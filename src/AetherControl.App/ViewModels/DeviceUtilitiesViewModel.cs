using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

public sealed record VendorLinkSet(string Vendor, string DpiSoftwareUrl, string WebsiteUrl, string FirmwareUrl);

public sealed partial class DeviceUtilitiesViewModel : ObservableObject
{
    private readonly IPeripheralDetectionService _peripheralDetection;

    public DeviceUtilitiesViewModel(IPeripheralDetectionService peripheralDetection)
    {
        _peripheralDetection = peripheralDetection;
        DetectedPeripherals = _peripheralDetection.Detect();
    }

    public IReadOnlyList<DetectedPeripheralInfo> DetectedPeripherals { get; private set; }

    public bool HasDetectedPeripherals => DetectedPeripherals.Count > 0;

    public bool HasNoDetectedPeripherals => !HasDetectedPeripherals;

    public IReadOnlyList<VendorLinkSet> Vendors { get; } =
    [
        new("Logitech", "https://www.logitechg.com/en-us/innovation/g-hub.html", "https://www.logitechg.com", "https://support.logi.com"),
        new("Razer", "https://www.razer.com/synapse-3", "https://www.razer.com", "https://mysupport.razer.com"),
        new("SteelSeries", "https://steelseries.com/gg", "https://steelseries.com", "https://support.steelseries.com"),
        new("ASUS", "https://www.asus.com/campaign/aura/us/armoury-crate.html", "https://www.asus.com", "https://www.asus.com/support/"),
        new("Corsair", "https://www.corsair.com/us/en/icue", "https://www.corsair.com", "https://www.corsair.com/us/en/support"),
        new("Glorious", "https://www.gloriousgaming.com/pages/glorious-core", "https://www.gloriousgaming.com", "https://www.gloriousgaming.com/pages/support")
    ];

    public void Refresh()
    {
        DetectedPeripherals = _peripheralDetection.Detect();
        OnPropertyChanged(nameof(DetectedPeripherals));
        OnPropertyChanged(nameof(HasDetectedPeripherals));
        OnPropertyChanged(nameof(HasNoDetectedPeripherals));
    }
}
