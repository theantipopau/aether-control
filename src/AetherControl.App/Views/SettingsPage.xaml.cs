using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Services.Hardware;
using AetherControl.Services.Optimisation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage()
    {
        InitializeComponent();
        ViewModel = new SettingsViewModel(
            App.Services.GetRequiredService<ISettingsService>(),
            App.Services.GetRequiredService<AlertSettingsStore>(),
            App.Services.GetRequiredService<AutostartService>(),
            App.Services.GetRequiredService<AetherControl.Core.Updates.IUpdateCheckService>(),
            App.Services.GetRequiredService<AetherControl.Core.Alerts.AlertLog>());
    }

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);
}
