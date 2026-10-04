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

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);
}
