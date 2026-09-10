using AetherControl.App.Services;
using AetherControl.App.Theming;
using AetherControl.Core.Interfaces;
using AetherControl.Services;
using AetherControl.Services.Hardware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppNotifications;

namespace AetherControl.App;

public partial class App : Application
{
    private IHost? _host;
    private TemperatureAlertService? _temperatureAlertService;

    public static IServiceProvider Services { get; private set; } = null!;

    public MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services.AddAetherControlServices())
            .Build();

        Services = _host.Services;

        // Unpackaged WinUI3 apps must register once at startup before AppNotificationManager.Show
        // will do anything — this is what makes the temperature-alert toasts below possible without
        // an MSIX package.
        AppNotificationManager.Default.Register();

        var settings = Services.GetRequiredService<ISettingsService>();
        await settings.InitializeAsync();
        AccentPalette.Apply(settings.Current.Accent);

        var hardwareMonitor = Services.GetRequiredService<IHardwareMonitorService>();
        hardwareMonitor.Start(TimeSpan.FromMilliseconds(settings.Current.DashboardRefreshMs));

        _temperatureAlertService = new TemperatureAlertService(hardwareMonitor, Services.GetRequiredService<AlertSettingsStore>());
        Services.GetRequiredService<IGameProfileService>().Start();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }
}
