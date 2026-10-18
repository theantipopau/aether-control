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

    // The Frame builds a fresh page on every navigation, so an instance flag can't mean "first
    // navigation ever" — the static gate can. Stagger runs once per app session; reflow attaches
    // on every load (idempotent) since each instance owns new visuals.
    private static bool s_hasStaggered;

    public DashboardPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            Theming.Motion.EnableImplicitReflow(HeroPanel);
            Theming.Motion.EnableImplicitReflow(LeftColumnPanel);
            Theming.Motion.EnableImplicitReflow(RightColumnPanel);
            if (!s_hasStaggered)
            {
                s_hasStaggered = true;
                Theming.Motion.StaggerIn([HeroPanel, LeftColumnPanel, RightColumnPanel]);
            }
        };
        ViewModel = new DashboardViewModel(
            App.Services.GetRequiredService<IHardwareMonitorService>(),
            App.Services.GetRequiredService<ProcessRankerService>(),
            App.Services.GetRequiredService<AetherControl.Core.Alerts.AlertLog>());

        // The Frame creates a fresh DashboardPage (and DashboardViewModel) on every navigation to
        // this page — without unsubscribing here, each one leaks a permanent subscriber on the
        // singleton IHardwareMonitorService, since nothing else ever calls Dispose().
        Unloaded += (_, _) => ViewModel.Dispose();

        // Fluid metric-card rows plan themselves (Controls.FluidPanel) — no attach/wiring needed.
    }

    /// <summary>HealthLevel.Unknown is the "no usable readings yet" state (see SystemHealth) — the
    /// hero gauges stay hidden and the skeleton row holds their space until the first real snapshot
    /// lands, so the app never shows "0 °C" at launch.</summary>
    public static bool IsWaiting(AetherControl.Core.Health.HealthLevel level) =>
        level == AetherControl.Core.Health.HealthLevel.Unknown;

    public static bool IsLive(AetherControl.Core.Health.HealthLevel level) => !IsWaiting(level);

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
