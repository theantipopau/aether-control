using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Services.Hardware;
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
            App.Services.GetRequiredService<AlertSettingsStore>());
    }
}
