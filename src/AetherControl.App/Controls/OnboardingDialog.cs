using AetherControl.Core.Interfaces;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Controls;

/// <summary>
/// First-run welcome dialog: what the app needs (elevation), where things live (areas), and the
/// fan-safety promise — the three things a new user otherwise discovers by confusion. Shown once
/// from MainWindow; re-openable from Settings → About via the same entry point. Any dismissal
/// counts as seen: re-nagging every launch until the exact primary button is pressed would be hostile.
/// </summary>
public static class OnboardingDialog
{
    public static async Task ShowAsync(XamlRoot xamlRoot, ISettingsService settings)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(Section("Elevated on purpose",
            "Most motherboard, fan and per-process readings need administrator rights — that's why every launch asks for elevation, and why some sensors stay empty without it."));
        body.Children.Add(Section("Areas, not utilities",
            "Overview is live health, Performance holds optimisation and fan control, Lighting drives RGB, Devices covers firmware and drivers, Diagnostics explains what the sensors are doing."));
        body.Children.Add(Section("Fans are safe by default",
            "Software control never drops below 20%, and every channel hands back to BIOS control when the app closes. Automatic curves are opt-in under Performance → Fan Control."));
        body.Children.Add(Section("Second screen? Open Portrait Mode",
            "A compact always-on-top stats window for a portrait monitor. Closing the main window keeps everything running in the tray."));

        var dialog = new ContentDialog
        {
            Title = "Welcome to Aether Control",
            Content = new ScrollViewer { Content = body, MaxHeight = 420 },
            PrimaryButtonText = "Get started",
            CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = xamlRoot
        };

        await dialog.ShowAsync();

        settings.Current.HasSeenOnboarding = true;
        await settings.SaveAsync(settings.Current);
    }

    private static StackPanel Section(string heading, string text)
    {
        var panel = new StackPanel { Spacing = 2 };
        panel.Children.Add(new TextBlock { Text = heading, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
        return panel;
    }
}
