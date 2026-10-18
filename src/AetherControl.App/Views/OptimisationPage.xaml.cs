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

    // Same shape as DashboardPage: fresh instance per navigation, so the first-run gate is static.
    private static bool s_hasStaggered;

    public OptimisationPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Theming.Motion.EnableImplicitReflow(SectionsStack);
            if (!s_hasStaggered)
            {
                s_hasStaggered = true;
                var cards = new List<UIElement>(SectionsStack.Children.Count);
                foreach (var child in SectionsStack.Children)
                {
                    cards.Add(child);
                }

                Theming.Motion.StaggerIn(cards);
            }
        };
        ViewModel = new OptimisationViewModel(
            App.Services.GetRequiredService<IOptimisationService>(),
            App.Services.GetRequiredService<IFanControlService>(),
            App.Services.GetRequiredService<AetherControl.Services.Optimisation.FanCurveController>(),
            App.Services.GetRequiredService<FanLabelStore>(),
            App.Services.GetRequiredService<IGameProfileService>(),
            App.Services.GetRequiredService<IHardwareMonitorService>());

        // The Frame creates a fresh OptimisationPage (and OptimisationViewModel) on every
        // navigation to this page — without unsubscribing here, each one leaks a permanent
        // subscriber on the singleton IGameProfileService, since nothing else calls Dispose().
        Unloaded += (_, _) => ViewModel.Dispose();

        // Fluid metric-card rows plan themselves (Controls.FluidPanel) — no attach/wiring needed.
    }

    // Window-size-dependent type scale — text grows/shrinks with the page width (WindowScale).
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs e) =>
        Controls.ResponsiveScale.Apply(RootContent, e.NewSize.Width, e.NewSize.Height);

    // Hero stat helpers. The view model reassigns these lists (never mutates them in place), so a
    // OneWay function binding re-evaluates on every PropertyChanged for the list.
    public static string FormatCount(IReadOnlyList<FanControlChannel> items) => items.Count.ToString();

    public static string FormatManualFans(IReadOnlyList<FanControlChannel> items) =>
        items.Count(c => c.IsSoftwareControlled).ToString();

    /// <summary>Fan-section StatusPill content: "Curve: X" while the closed loop drives, else
    /// "BIOS control" when every channel rides the board, "Manual (n)" otherwise. The list is
    /// reassigned (never mutated) so the OneWay binding re-evaluates on every change — same
    /// shape as the hero stat helpers above.</summary>
    public static string FormatFanPillText(IReadOnlyList<FanControlChannel>? items, string? activeCurve)
    {
        if (!string.IsNullOrEmpty(activeCurve))
        {
            return $"Curve: {activeCurve}";
        }

        var manual = items?.Count(c => c.IsSoftwareControlled) ?? 0;
        return manual == 0 ? "BIOS control" : $"Manual ({manual})";
    }

    public static Controls.StatusPillState FormatFanPillState(IReadOnlyList<FanControlChannel>? items, string? activeCurve) =>
        !string.IsNullOrEmpty(activeCurve) || (items?.Any(c => c.IsSoftwareControlled) == true)
            ? Controls.StatusPillState.Info
            : Controls.StatusPillState.Ok;

    public static string FormatProfileCount(IReadOnlyList<GameProfile> items) => items.Count.ToString();

    public static string FormatGamingMode(bool enabled) => enabled ? "On" : "Off";

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

    public static string FormatPercent(double percent) => double.IsFinite(percent) ? $"{percent:F0}%" : "—";
}
