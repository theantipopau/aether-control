using System.Globalization;
using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
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

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);

    private async void OnSwatchClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: RgbDeviceInfo device, Tag: string hex }
            && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            await ViewModel.SetColorAsync(device, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
    }

    // Flyout content doesn't reliably inherit the item's DataContext, so hand it the device from
    // the button that opened it; Apply then reads it back like the swatches do.
    private void OnCustomFlyoutOpening(object sender, object e)
    {
        if (sender is Flyout { Target: FrameworkElement target, Content: FrameworkElement content })
        {
            content.DataContext = target.DataContext;
        }
    }

    private async void OnApplyCustomColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Parent: Panel panel, DataContext: RgbDeviceInfo device })
        {
            return;
        }

        var picker = panel.Children.OfType<ColorPicker>().FirstOrDefault();
        if (picker is null)
        {
            return;
        }

        var color = picker.Color;
        await ViewModel.SetColorAsync(device, color.R, color.G, color.B);
    }
}
