using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class FirmwarePage : Page
{
    public FirmwareViewModel ViewModel { get; }

    public FirmwarePage()
    {
        InitializeComponent();
        ViewModel = new FirmwareViewModel(App.Services.GetRequiredService<IFirmwareDriverService>());
    }
}
