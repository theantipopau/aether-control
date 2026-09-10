using AetherControl.App.Views;
using AetherControl.Core.Enums;
using AetherControl.Core.Events;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Tray;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AetherControl.App;

public sealed partial class MainWindow : Window
{
    // Falls back to these when nothing's configured yet in Settings (tray_metric_preferences
    // starts empty on a fresh install) — CPU/GPU temp + RAM is the same trio HWiNFO's tray gadget
    // defaults to, and matches what a glance at the tray icon is actually for.
    private static readonly IReadOnlyList<TrayMetricPreference> DefaultTrayPreferences =
    [
        new() { Metric = MetricKind.CpuTemperature, Enabled = true, Order = 0 },
        new() { Metric = MetricKind.GpuTemperature, Enabled = true, Order = 1 },
        new() { Metric = MetricKind.RamUtilisationPercent, Enabled = true, Order = 2 }
    ];

    private bool _exitRequested;
    private PortraitWindow? _portraitWindow;
    private IReadOnlyList<TrayMetricPreference> _trayPreferences = DefaultTrayPreferences;

    public IRelayCommand ShowWindowCommand { get; }
    public IRelayCommand OpenDashboardCommand { get; }
    public IRelayCommand OpenPortraitModeCommand { get; }
    public IAsyncRelayCommand RunOptimisationCommand { get; }
    public IRelayCommand OpenRgbCommand { get; }
    public IRelayCommand OpenFirmwareCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }
    public IRelayCommand ExitCommand { get; }

    public MainWindow()
    {
        InitializeComponent();
        Title = "Aether Control";
        ShowWindowCommand = new RelayCommand(ShowAndActivate);
        OpenDashboardCommand = new RelayCommand(() => { ShowAndActivate(); ContentFrame.Navigate(typeof(DashboardPage)); });
        OpenPortraitModeCommand = new RelayCommand(OpenPortraitWindow);
        RunOptimisationCommand = new AsyncRelayCommand(RunOptimisationAsync);
        OpenRgbCommand = new RelayCommand(() => { ShowAndActivate(); ContentFrame.Navigate(typeof(RgbPage)); });
        OpenFirmwareCommand = new RelayCommand(() => { ShowAndActivate(); ContentFrame.Navigate(typeof(FirmwarePage)); });
        OpenSettingsCommand = new RelayCommand(() => { ShowAndActivate(); ContentFrame.Navigate(typeof(SettingsPage)); });
        ExitCommand = new RelayCommand(() => { _exitRequested = true; Close(); });

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ConfigureTitleBarButtons();

        // BaseAlt is Mica's darker-tinted variant — reads correctly against the charcoal palette
        // instead of the lighter default Mica tuned for light-theme apps.
        SystemBackdrop = new MicaBackdrop { Kind = MicaKind.BaseAlt };

        ContentFrame.Navigate(typeof(DashboardPage));
        RootNavigationView.SelectedItem = RootNavigationView.MenuItems[0];

        Closed += OnWindowClosed;

        _ = InitializeTrayPreferencesAsync();
        App.Services.GetRequiredService<IHardwareMonitorService>().SnapshotUpdated += OnSnapshotUpdatedForTray;
    }

    private async Task InitializeTrayPreferencesAsync()
    {
        var stored = await App.Services.GetRequiredService<ISettingsService>().GetTrayMetricPreferencesAsync();
        if (stored.Count > 0)
        {
            _trayPreferences = stored;
        }
    }

    private void OnSnapshotUpdatedForTray(object? sender, SensorsUpdatedEventArgs e)
    {
        // Fires on HardwareMonitorService's background polling thread — TrayIcon is a UI-thread object.
        var readout = TrayReadoutFormatter.Format(e.Snapshot, _trayPreferences);
        DispatcherQueue.TryEnqueue(() => TrayIcon.ToolTipText = string.IsNullOrWhiteSpace(readout) ? "Aether Control" : readout);
    }

    private void ConfigureTitleBarButtons()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 0x9B, 0xA1, 0xA6);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);
        titleBar.ButtonPressedForegroundColor = Colors.White;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        var pageType = tag switch
        {
            "Dashboard" => typeof(DashboardPage),
            "Portrait" => null, // opens a separate window instead of navigating in-place
            "Optimisation" => typeof(OptimisationPage),
            "Rgb" => typeof(RgbPage),
            "Firmware" => typeof(FirmwarePage),
            "Devices" => typeof(DeviceUtilitiesPage),
            "History" => typeof(HistoryPage),
            _ => typeof(DashboardPage)
        };

        if (tag == "Portrait")
        {
            OpenPortraitWindow();
            return;
        }

        if (pageType is not null)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    private void OpenPortraitWindow()
    {
        _portraitWindow ??= new PortraitWindow();
        _portraitWindow.Activate();
    }

    private void ShowAndActivate() => Activate();

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        if (!_exitRequested && settings.Current.MinimiseToTrayOnClose)
        {
            args.Handled = true;
            AppWindow.Hide();
            return;
        }

        // H.NotifyIcon's TaskbarIcon owns a hidden native window for the tray icon that
        // Application.Exit() doesn't know about — without disposing it explicitly here, the
        // process stays alive (and the icon lingers in the tray) even after every XAML window closes.
        _portraitWindow?.ForceClose();
        TrayIcon.Dispose();

        App.Services.GetRequiredService<IHardwareMonitorService>().Dispose();
        Microsoft.Windows.AppNotifications.AppNotificationManager.Default.Unregister();

        Microsoft.UI.Xaml.Application.Current.Exit();
    }

    private async Task RunOptimisationAsync()
    {
        var optimisation = App.Services.GetRequiredService<IOptimisationService>();
        var tasks = optimisation.GetAvailableTasks();
        if (tasks.Count > 0)
        {
            await optimisation.RunAsync(tasks[0].Id, createRestorePoint: false);
        }
    }
}
