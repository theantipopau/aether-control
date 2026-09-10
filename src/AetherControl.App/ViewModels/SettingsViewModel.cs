using AetherControl.App.Theming;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AetherControl.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    // The only MetricKind values TrayReadoutFormatter actually knows how to render — see
    // TrayMetricOption's own doc comment for why the picker doesn't just enumerate the full enum.
    private static readonly (MetricKind Metric, string DisplayName, bool DefaultEnabled)[] SupportedTrayMetrics =
    [
        (MetricKind.CpuTemperature, "CPU temperature", true),
        (MetricKind.CpuUtilisation, "CPU utilisation", false),
        (MetricKind.GpuTemperature, "GPU temperature", true),
        (MetricKind.GpuUtilisation, "GPU utilisation", false),
        (MetricKind.RamUtilisationPercent, "RAM utilisation", true),
        (MetricKind.NetworkDownloadKbps, "Network download", false),
        (MetricKind.NetworkUploadKbps, "Network upload", false)
    ];

    private readonly ISettingsService _settingsService;
    private readonly AlertSettingsStore _alertSettingsStore;

    [ObservableProperty] private ThemeMode theme;
    [ObservableProperty] private AccentColor accent;
    [ObservableProperty] private double dashboardRefreshMs;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool startMinimisedToTray;
    [ObservableProperty] private bool minimiseToTrayOnClose;
    [ObservableProperty] private double historyRetentionDays;
    [ObservableProperty] private bool loggingEnabled;
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<TrayMetricOption> trayMetricOptions = [];
    [ObservableProperty] private bool temperatureAlertsEnabled;
    [ObservableProperty] private double cpuTemperatureAlertThreshold;
    [ObservableProperty] private double gpuTemperatureAlertThreshold;

    public IReadOnlyList<ThemeMode> ThemeOptions { get; } = Enum.GetValues<ThemeMode>();
    public IReadOnlyList<AccentColor> AccentOptions { get; } = Enum.GetValues<AccentColor>();

    public SettingsViewModel(ISettingsService settingsService, AlertSettingsStore alertSettingsStore)
    {
        _settingsService = settingsService;
        _alertSettingsStore = alertSettingsStore;
        var current = _settingsService.Current;
        Theme = current.Theme;
        Accent = current.Accent;
        DashboardRefreshMs = current.DashboardRefreshMs;
        StartWithWindows = current.StartWithWindows;
        StartMinimisedToTray = current.StartMinimisedToTray;
        MinimiseToTrayOnClose = current.MinimiseToTrayOnClose;
        HistoryRetentionDays = current.HistoryRetentionDays;
        LoggingEnabled = current.LoggingEnabled;

        var alerts = _alertSettingsStore.Current;
        TemperatureAlertsEnabled = alerts.Enabled;
        CpuTemperatureAlertThreshold = alerts.CpuTemperatureThreshold;
        GpuTemperatureAlertThreshold = alerts.GpuTemperatureThreshold;

        _ = LoadTrayMetricOptionsAsync();
    }

    private async Task LoadTrayMetricOptionsAsync()
    {
        var stored = await _settingsService.GetTrayMetricPreferencesAsync();
        var enabledByMetric = stored.ToDictionary(p => p.Metric, p => p.Enabled);

        TrayMetricOptions = SupportedTrayMetrics
            .Select(m => new TrayMetricOption(m.Metric, m.DisplayName, enabledByMetric.GetValueOrDefault(m.Metric, m.DefaultEnabled)))
            .ToList();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var updated = _settingsService.Current;
        updated.Theme = Theme;
        updated.Accent = Accent;
        updated.DashboardRefreshMs = (int)DashboardRefreshMs;
        updated.StartWithWindows = StartWithWindows;
        updated.StartMinimisedToTray = StartMinimisedToTray;
        updated.MinimiseToTrayOnClose = MinimiseToTrayOnClose;
        updated.HistoryRetentionDays = (int)HistoryRetentionDays;
        updated.LoggingEnabled = LoggingEnabled;

        await _settingsService.SaveAsync(updated);

        var trayPreferences = TrayMetricOptions
            .Select((option, index) => new TrayMetricPreference { Metric = option.Metric, Enabled = option.IsEnabled, Order = index })
            .ToList();
        await _settingsService.SaveTrayMetricPreferencesAsync(trayPreferences);

        _alertSettingsStore.Save(new AlertSettings
        {
            Enabled = TemperatureAlertsEnabled,
            CpuTemperatureThreshold = CpuTemperatureAlertThreshold,
            GpuTemperatureThreshold = GpuTemperatureAlertThreshold
        });

        // Aether Control's own custom-styled elements (gauges, cards, hover borders) pick up the
        // new accent immediately since they share one mutated brush instance; stock WinUI controls
        // (buttons, switches, the nav selection pill) derive from SystemAccentColor through
        // ThemeResource chains that don't reliably re-resolve without a restart.
        AccentPalette.Apply(updated.Accent);
        StatusMessage = "Settings saved. Restart Aether Control for the new accent colour to fully apply everywhere.";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        var json = await _settingsService.ExportConfigurationAsync();
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aether Control", "export.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, json);
        StatusMessage = $"Exported to {path}";
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Aether Control", "export.json");
        if (!File.Exists(path))
        {
            StatusMessage = $"No export found at {path}";
            return;
        }

        await _settingsService.ImportConfigurationAsync(await File.ReadAllTextAsync(path));
        StatusMessage = "Configuration imported.";
    }
}
