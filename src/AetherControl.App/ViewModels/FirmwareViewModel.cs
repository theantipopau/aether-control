using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherControl.App.ViewModels;

public sealed partial class FirmwareViewModel : ObservableObject
{
    private readonly IFirmwareDriverService _firmwareDriverService;

    [ObservableProperty] private IReadOnlyList<DriverStatus> statuses = [];
    [ObservableProperty] private bool isRefreshing;
    [ObservableProperty] private bool isCheckingUpdates;
    [ObservableProperty] private bool hasCheckedForUpdates;
    [ObservableProperty] private DriverUpdateCheck? updateCheck;

    public FirmwareViewModel(IFirmwareDriverService firmwareDriverService)
    {
        _firmwareDriverService = firmwareDriverService;
        _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        IsRefreshing = true;
        try
        {
            Statuses = await _firmwareDriverService.GetStatusAsync();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    /// <summary>Explicit, user-initiated Windows Update search — deliberately never runs on page
    /// load: it can take tens of seconds against the WU service, and the product rule is that
    /// Aether only ever *lists* what Windows offers.</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        try
        {
            UpdateCheck = await _firmwareDriverService.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            // The service already catches everything it can — this is the belt-and-braces path so
            // an unexpected failure still lands as an honest on-screen state, not a silent no-op.
            UpdateCheck = new DriverUpdateCheck { Succeeded = false, Message = $"Windows Update search failed: {ex.Message}" };
        }
        finally
        {
            HasCheckedForUpdates = true;
            IsCheckingUpdates = false;
        }
    }
}
