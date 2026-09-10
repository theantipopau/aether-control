using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace AetherControl.App.Views;

public sealed partial class OptimisationPage : Page
{
    public OptimisationViewModel ViewModel { get; }

    public OptimisationPage()
    {
        InitializeComponent();
        ViewModel = new OptimisationViewModel(
            App.Services.GetRequiredService<IOptimisationService>(),
            App.Services.GetRequiredService<IFanControlService>(),
            App.Services.GetRequiredService<FanLabelStore>(),
            App.Services.GetRequiredService<IGameProfileService>());

        // The Frame creates a fresh OptimisationPage (and OptimisationViewModel) on every
        // navigation to this page — without unsubscribing here, each one leaks a permanent
        // subscriber on the singleton IGameProfileService, since nothing else calls Dispose().
        Unloaded += (_, _) => ViewModel.Dispose();
    }

    private async void OnRenameFanChannelClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FanControlChannel channel })
        {
            return;
        }

        var input = new TextBox { Text = channel.Name, PlaceholderText = "e.g. Front Intake" };
        var dialog = new ContentDialog
        {
            Title = "Rename fan channel",
            Content = input,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.RenameFanChannel(channel.Id, input.Text.Trim());
        }
    }

    private void OnGamingProfileToggled(object sender, bool isOn)
    {
        if (ViewModel.ToggleGamingProfileCommand.CanExecute(null))
        {
            ViewModel.ToggleGamingProfileCommand.Execute(null);
        }
    }

    private void OnFanSliderChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: FanControlChannel channel })
        {
            ViewModel.SetFanPercent(channel.Id, (int)e.NewValue);
        }
    }

    private void OnStartupEntryToggled(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: StartupEntry entry } && ViewModel.ToggleStartupEntryCommand.CanExecute(entry))
        {
            ViewModel.ToggleStartupEntryCommand.Execute(entry);
        }
    }

    public string FormatScannedDetail(double scanned) => $"of {scanned:F0} scanned";
}
