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
    private bool _isClosing;
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

        // Reduced motion: drop the page slide entirely rather than shortening it.
        if (!Theming.Motion.AnimationsEnabled)
        {
            ContentFrame.ContentTransitions = null;
        }

        ContentFrame.Navigated += OnContentFrameNavigated;
        ContentFrame.Navigate(typeof(DashboardPage));

        Closed += OnWindowClosed;

        _ = InitializeTrayPreferencesAsync();
        _ = CheckForUpdatesAtStartupAsync();
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

    /// <summary>Only when the user opted in (Settings → About, off by default). A newer release puts a
    /// small badge on Settings — no dialog, no toast, no download; Settings shows the details.</summary>
    private async Task CheckForUpdatesAtStartupAsync()
    {
        try
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            if (!settings.Current.CheckForUpdatesOnStartup)
            {
                return;
            }

            var result = await App.Services.GetRequiredService<AetherControl.Core.Updates.IUpdateCheckService>().CheckAsync();
            if (result.Status == AetherControl.Core.Updates.UpdateStatus.UpdateAvailable
                && RootNavigationView.SettingsItem is NavigationViewItem settingsItem)
            {
                settingsItem.InfoBadge = new InfoBadge();
                ToolTipService.SetToolTip(settingsItem, $"Settings — {result.Message}");
            }
        }
        catch
        {
            // Best-effort; the manual check in Settings reports failures properly.
        }
    }

    private void OnSnapshotUpdatedForTray(object? sender, SensorsUpdatedEventArgs e)
    {
        // Fires on HardwareMonitorService's background polling thread — TrayIcon is a UI-thread object.
        var readout = TrayReadoutFormatter.Format(e.Snapshot, _trayPreferences);
        DispatcherQueue.TryEnqueue(() =>
        {
            // A callback can already be sitting in the UI-thread dispatch queue (enqueued from a
            // poll moments before the user closed the window) by the time OnWindowClosed disposes
            // TrayIcon — unsubscribing there stops NEW callbacks, this guard is what stops one
            // that's already queued from touching the now-closed TrayIcon.
            if (!_isClosing)
            {
                TrayIcon.ToolTipText = string.IsNullOrWhiteSpace(readout) ? "Aether Control" : readout;
                UpdateHealthIndicator(e.Snapshot);
            }
        });
    }

    private void ConfigureTitleBarButtons()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        // Title-bar caption buttons take Color values, not brushes — read from the same tokens.
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var primary = (Color)resources["TextPrimaryColor"];
        titleBar.ButtonForegroundColor = (Color)resources["TextSecondaryColor"];
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonHoverForegroundColor = primary;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);
        titleBar.ButtonPressedForegroundColor = primary;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ContentFrame.Navigate(typeof(SettingsPage));
            return;
        }

        if (_syncingSelection)
        {
            return; // selection is being mirrored from a navigation that already happened
        }

        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        if (tag is not null && PageTypesByTag.TryGetValue(tag, out var pageType) && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
    }

    // Route table: NavigationViewItem Tag → page. Tags are unchanged from before the 39.3 regrouping,
    // so tray commands and anything keyed on them keep working.
    private static readonly Dictionary<string, Type> PageTypesByTag = new()
    {
        ["Dashboard"] = typeof(DashboardPage),
        ["Optimisation"] = typeof(OptimisationPage),
        ["Rgb"] = typeof(RgbPage),
        ["Firmware"] = typeof(FirmwarePage),
        ["Devices"] = typeof(DeviceUtilitiesPage),
        ["History"] = typeof(HistoryPage),
        ["Diagnostics"] = typeof(DiagnosticsPage)
    };

    private bool _syncingSelection;

    /// <summary>Portrait Mode is a separate window, not a page — it's non-selectable
    /// (SelectsOnInvoked=False), so invoking it opens the window and leaves the highlight on the
    /// page actually being shown, instead of marking "Portrait Mode" as the current page.</summary>
    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if ((args.InvokedItemContainer as NavigationViewItem)?.Tag as string == "Portrait")
        {
            OpenPortraitWindow();
        }
    }

    /// <summary>Keeps the nav highlight honest when navigation didn't come from the nav itself —
    /// the tray menu's "Open Dashboard"/"RGB Control"/"Settings" previously navigated the frame but
    /// left the old item highlighted.</summary>
    private void OnContentFrameNavigated(object sender, Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        _syncingSelection = true;
        try
        {
            if (e.SourcePageType == typeof(SettingsPage))
            {
                RootNavigationView.SelectedItem = RootNavigationView.SettingsItem;
                return;
            }

            var tag = PageTypesByTag.FirstOrDefault(kv => kv.Value == e.SourcePageType).Key;
            var item = FindNavItem(RootNavigationView.MenuItems, tag) ?? FindNavItem(RootNavigationView.FooterMenuItems, tag);
            if (item is not null)
            {
                RootNavigationView.SelectedItem = item;
            }
        }
        finally
        {
            _syncingSelection = false;
        }
    }

    private static NavigationViewItem? FindNavItem(IList<object> items, string? tag)
    {
        if (tag is null)
        {
            return null;
        }

        foreach (var entry in items)
        {
            if (entry is not NavigationViewItem item)
            {
                continue;
            }

            if (item.Tag as string == tag)
            {
                return item;
            }

            if (FindNavItem(item.MenuItems, tag) is { } child)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>Title-bar health readout: worst of CPU/GPU temperature, as glyph + word + numbers.
    /// Uses the same thresholds Portrait Mode already shows, so the two never disagree.</summary>
    private void UpdateHealthIndicator(HardwareSnapshot snapshot)
    {
        var cpu = snapshot.Cpu.TemperatureCelsius;
        var gpu = snapshot.Gpu.TemperatureCelsius;
        if (cpu <= 0 && gpu <= 0)
        {
            HealthIndicator.Visibility = Visibility.Collapsed; // no readings — say nothing rather than "Normal"
            return;
        }

        var worst = (ViewModels.PortraitSeverity)Math.Max(
            (int)ViewModels.PortraitSeverityThresholds.ForTemp(cpu),
            (int)ViewModels.PortraitSeverityThresholds.ForTemp(gpu));

        var (status, glyph, brushKey) = worst switch
        {
            ViewModels.PortraitSeverity.Critical => ("Hot", "", "StatusCriticalBrush"),
            ViewModels.PortraitSeverity.Warning => ("Warm", "", "StatusWarningBrush"),
            _ => ("Normal", "", "StatusNormalBrush")
        };

        var brush = (Brush)Microsoft.UI.Xaml.Application.Current.Resources[brushKey];
        HealthStatusText.Text = status;
        HealthStatusText.Foreground = brush;
        HealthGlyph.Glyph = glyph;
        HealthGlyph.Foreground = brush;
        HealthDetailText.Text = $"CPU {cpu:F0}°C · GPU {gpu:F0}°C";
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(HealthIndicator, $"System health {status}: CPU {cpu:F0} degrees, GPU {gpu:F0} degrees");
        HealthIndicator.Visibility = Visibility.Visible;
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

        // Ordered shutdown. A recurring crash signature (RO_E_CLOSED / combase.dll, roughly weekly,
        // usually within ~30s of launch — i.e. shortly after open-then-close) matches this exactly:
        // OnSnapshotUpdatedForTray's DispatcherQueue.TryEnqueue callback, already queued from a
        // background poll moments before the user closed the window, running AFTER TrayIcon.Dispose()
        // below and touching the now-closed WinRT object. Stop everything that can enqueue new UI
        // work FIRST (unsubscribe + the _isClosing guard above), then dispose UI objects, then the
        // hardware session itself — not the reverse.
        _isClosing = true;
        var hardwareMonitor = App.Services.GetRequiredService<IHardwareMonitorService>();
        hardwareMonitor.SnapshotUpdated -= OnSnapshotUpdatedForTray;

        // TemperatureAlertService and IGameProfileService each subscribe to a long-lived singleton's
        // event or run their own background Timer, and previously were never disposed at all.
        ((App)Microsoft.UI.Xaml.Application.Current).StopBackgroundServices();

        _portraitWindow?.ForceClose();

        // H.NotifyIcon's TaskbarIcon owns a hidden native window for the tray icon that
        // Application.Exit() doesn't know about — without disposing it explicitly here, the
        // process stays alive (and the icon lingers in the tray) even after every XAML window closes.
        TrayIcon.Dispose();

        hardwareMonitor.Dispose();
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
