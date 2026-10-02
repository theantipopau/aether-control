using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Services.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Views;

public sealed partial class DashboardPage : Page
{
    public DashboardViewModel ViewModel { get; }

    public DashboardPage()
    {
        InitializeComponent();
        ViewModel = new DashboardViewModel(
            App.Services.GetRequiredService<IHardwareMonitorService>(),
            App.Services.GetRequiredService<ProcessRankerService>());

        // The Frame creates a fresh DashboardPage (and DashboardViewModel) on every navigation to
        // this page — without unsubscribing here, each one leaks a permanent subscriber on the
        // singleton IHardwareMonitorService, since nothing else ever calls Dispose().
        Unloaded += (_, _) => ViewModel.Dispose();

        // Fluid metric-card rows plan themselves (Controls.FluidPanel) — no attach/wiring needed.
    }

    /// <summary>RDNA cards stop their fans below ~50–60 °C (zero-RPM mode), so 0 % on a cool GPU is
    /// normal, not a fault. Only explain it when that's plausibly what's happening.</summary>
    public static string FormatGpuFanDetail(double fanPercent, double gpuTempC) =>
        fanPercent <= 0 && gpuTempC is > 0 and < 60 ? "Stopped — fans idle when cool" : string.Empty;

    private void OnExternalIpTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) =>
        ViewModel.IsExternalIpRevealed = !ViewModel.IsExternalIpRevealed;

    public static string FormatOneDecimal(double value) => double.IsFinite(value) ? value.ToString("F1") : "—";

    // Quick actions navigate the shell's own Frame — MainWindow's Frame.Navigated handler then moves
    // the nav highlight, so these stay in sync with the menu without knowing about it.
    private void OnOpenPerformanceClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Frame.Navigate(typeof(OptimisationPage));

    private void OnOpenLightingClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => Frame.Navigate(typeof(RgbPage));

    private void OnOpenPortraitClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) =>
        ((App)Microsoft.UI.Xaml.Application.Current).MainWindow?.OpenPortraitWindow();
}
