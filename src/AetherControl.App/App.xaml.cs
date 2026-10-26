using AetherControl.App.Services;
using AetherControl.App.Theming;
using AetherControl.Core.Interfaces;
using AetherControl.Services;
using AetherControl.Services.Infrastructure;
using AetherControl.Services.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace AetherControl.App;

public partial class App : Application
{
    private IHost? _host;
    private SingleInstanceGuard? _singleInstance;
    private TemperatureAlertService? _temperatureAlertService;
    private HistoryRecorderService? _historyRecorderService;

    public static IServiceProvider Services { get; private set; } = null!;

    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();

        // A single missed catch anywhere in the app previously took the entire process down with it
        // (confirmed: an uncaught PathTooLongException from a background directory scan surfaced here
        // as a fatal, unrecoverable crash with nothing but a Windows Error Reporting dump to explain
        // it). Logging + Handled=true keeps the app alive for anything that reaches this point — the
        // underlying bugs still get fixed as they're found, this is the backstop for the ones that
        // aren't yet.
        UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        UnhandledExceptionLog.Write(e.Exception, $"UnhandledException: {e.Message}");
        e.Handled = true;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // A second process opening its own LibreHardwareMonitor session would contend with the
        // first for the same Super I/O chip (both sides reading garbage persistently) — refuse
        // the launch and foreground the running instance instead. Held for process lifetime via
        // the field; the OS releases it on crash, so a dead holder can never wedge startup.
        var exitRequested = Environment.GetCommandLineArgs().Contains("--exit", StringComparer.OrdinalIgnoreCase);
        _singleInstance = new SingleInstanceGuard("Local\\AetherControl.SingleInstance.v1");
        if (!_singleInstance.IsFirstInstance || exitRequested)
        {
            if (exitRequested)
            {
                _singleInstance.Dispose();
                ExitSignal.SignalAndWait(TimeSpan.FromSeconds(15));
            }
            else
            {
                SingleInstanceFocus.FocusExisting();
            }

            Exit();
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddAetherControlServices())
            // No logging provider was ever registered anywhere in this app — every _logger.LogWarning
            // / LogError call throughout the codebase went nowhere a user could see without a debugger
            // attached. AppFileLoggerProvider's own gating (LoggingSettings.Enabled/MinLevel, applied
            // below once Settings loads) is what makes LoggingEnabled/LogLevel real settings instead
            // of values saved to the database and never read.
            .ConfigureLogging(logging => logging.AddProvider(new AppFileLoggerProvider()))
            .Build();

        Services = _host.Services;

        // Unpackaged WinUI3 apps must register once at startup before AppNotificationManager.Show
        // will do anything — this is what makes the temperature-alert toasts below possible without
        // an MSIX package.
        AppNotificationManager.Default.Register();
        AppNotificationManager.Default.NotificationInvoked += OnNotificationInvoked;

        var settings = Services.GetRequiredService<ISettingsService>();
        await settings.InitializeAsync();
        AccentPalette.Apply(settings.Current.Accent);
        LoggingSettings.Apply(settings.Current.LoggingEnabled, settings.Current.LogLevel);

        var hardwareMonitor = Services.GetRequiredService<IHardwareMonitorService>();
        hardwareMonitor.Start(TimeSpan.FromMilliseconds(settings.Current.DashboardRefreshMs));

        _temperatureAlertService = new TemperatureAlertService(
            hardwareMonitor,
            Services.GetRequiredService<AlertSettingsStore>(),
            Services.GetRequiredService<AetherControl.Core.Alerts.AlertLog>());
        _historyRecorderService = new HistoryRecorderService(hardwareMonitor, Services.GetRequiredService<IHistoryService>(), settings);
        Services.GetRequiredService<IGameProfileService>().Start();

        MainWindow = new MainWindow();
        MainWindow.Activate();

        var window = MainWindow;
        ExitSignal.Listen(() => window.DispatcherQueue.TryEnqueue(() => window.ExitCommand.Execute(null)));

        // The autostart task stores an absolute exe path; moving from the portable folder to the
        // installed one (or vice versa) would leave it launching the old copy. Re-register in place.
        // The task's existence is the setting (SettingsViewModel reads IsEnabled()), so check that.
        var autostart = Services.GetRequiredService<AetherControl.Services.Optimisation.AutostartService>();
        _ = Task.Run(() =>
        {
            if (autostart.IsEnabled())
            {
                autostart.SetEnabled(true);
            }
        });

        // StartMinimisedToTray was previously saved to Settings and never read by anything — the
        // window always showed on launch regardless of this toggle.
        if (settings.Current.StartMinimisedToTray)
        {
            MainWindow.AppWindow.Hide();
        }
    }

    /// <summary>
    /// Temperature toasts (and Settings' test notification) previously dismissed silently — a
    /// click now brings the main window forward and navigates to the page the toast deep-links
    /// (temperature alerts → dashboard, test notification → settings). Fires on a background
    /// thread, hence the dispatch; best-effort, since foregrounding can be refused.
    /// </summary>
    // The event is called NotificationInvoked but its args type is AppNotificationActivatedEventArgs
    // (verified in both the 1.5 and 2.x SDK winmd — AppNotificationInvokedEventArgs never existed).
    private void OnNotificationInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var pageType = ParseToastPage(args.Argument);
        MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            if (MainWindow is not null)
            {
                MainWindow.ShowAndNavigateTo(pageType);
            }
        });
    }

    private static Type ParseToastPage(string arguments)
    {
        // AppNotificationBuilder serialises arguments as "key=value" pairs joined by ';'.
        foreach (var pair in arguments.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (pair.Trim().Equals("page=settings", StringComparison.OrdinalIgnoreCase))
            {
                return typeof(Views.SettingsPage);
            }
        }

        return typeof(Views.DashboardPage);
    }

    /// <summary>
    /// Disposes the background services <see cref="OnLaunched"/> starts that nothing else in the
    /// process ever tears down — <see cref="TemperatureAlertService"/>, <see cref="HistoryRecorderService"/>,
    /// and <see cref="IGameProfileService"/> each subscribe to a long-lived singleton's event
    /// (<c>IHardwareMonitorService.SnapshotUpdated</c>) or run their own background <see cref="Timer"/>,
    /// and were previously never disposed at all. Must be called from <see cref="MainWindow"/>'s
    /// shutdown path BEFORE anything UI-facing (tray icon, windows) is disposed — see that class's
    /// own ordered-shutdown comment for why.
    /// </summary>
    public void StopBackgroundServices()
    {
        _temperatureAlertService?.Dispose();
        _historyRecorderService?.Dispose();
        Services.GetRequiredService<IGameProfileService>().Dispose();
    }
}
