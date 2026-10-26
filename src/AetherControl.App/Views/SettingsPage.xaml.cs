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
            App.Services.GetRequiredService<ThermalAuraSettingsStore>(),
            App.Services.GetRequiredService<AutostartService>(),
            App.Services.GetRequiredService<AetherControl.Core.Updates.IUpdateCheckService>(),
            App.Services.GetRequiredService<AetherControl.Core.Alerts.AlertLog>());
    }

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);

    // Re-opens the first-run dialog (same helper MainWindow uses) — best-effort like the
    // test-notification button: a guide that fails to open must never break Settings.
    private async void OnShowWelcomeGuideClicked(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            await Controls.OnboardingDialog.ShowAsync(XamlRoot, App.Services.GetRequiredService<ISettingsService>());
        }
        catch (Exception ex)
        {
            // Staying on the Settings page is the correct fallback; say why the guide didn't open.
            ViewModel.StatusMessage = $"Couldn't open the welcome guide: {ex.Message}";
        }
    }
}
