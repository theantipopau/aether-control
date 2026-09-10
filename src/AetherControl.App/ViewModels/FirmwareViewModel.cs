using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherControl.App.ViewModels;

public sealed partial class FirmwareViewModel : ObservableObject
{
    private readonly IFirmwareDriverService _firmwareDriverService;

    [ObservableProperty] private IReadOnlyList<DriverStatus> statuses = [];

    public FirmwareViewModel(IFirmwareDriverService firmwareDriverService)
    {
        _firmwareDriverService = firmwareDriverService;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Statuses = await _firmwareDriverService.GetStatusAsync();
    }
}
