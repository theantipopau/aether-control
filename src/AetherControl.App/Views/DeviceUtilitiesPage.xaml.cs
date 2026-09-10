using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class DeviceUtilitiesPage : Page
{
    public DeviceUtilitiesViewModel ViewModel { get; }

    public DeviceUtilitiesPage()
    {
        InitializeComponent();
        ViewModel = new DeviceUtilitiesViewModel(App.Services.GetRequiredService<IPeripheralDetectionService>());
    }
}
