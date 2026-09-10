using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class RgbPage : Page
{
    public RgbViewModel ViewModel { get; }

    public RgbPage()
    {
        InitializeComponent();
        ViewModel = new RgbViewModel(
            App.Services.GetRequiredService<IRgbService>(),
            App.Services.GetRequiredService<IPeripheralDetectionService>());
    }
}
