using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace AetherControl.App.Views;

public sealed partial class FirmwarePage : Page
{
    public FirmwareViewModel ViewModel { get; }

    public FirmwarePage()
    {
        InitializeComponent();
        ViewModel = new FirmwareViewModel(App.Services.GetRequiredService<IFirmwareDriverService>());
    }

    /// <summary>Hides the "not checked yet" hint once a search has produced an on-screen state.</summary>
    public static Visibility HideWhenChecked(bool hasCheckedForUpdates) =>
        hasCheckedForUpdates ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>Disables the check button while a search runs. A function binding, not
    /// <c>{x:Bind !...}</c>: this toolchain's x:Bind parser silently *drops* a leading `!`
    /// ("token recognition error at '!'") and binds the raw bool instead — verified against the
    /// generated FirmwarePage.g.cs.</summary>
    public static bool NotChecking(bool isCheckingUpdates) => !isCheckingUpdates;

    public static InfoBarSeverity SeverityFor(bool succeeded) =>
        succeeded ? InfoBarSeverity.Success : InfoBarSeverity.Error;

    /// <summary>The install step always happens in Windows itself — Aether only ever lists.</summary>
    private async void OnOpenWindowsUpdateClick(object sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri("ms-settings:windowsupdate"));
}
