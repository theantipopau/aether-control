using AetherControl.App.Services;
using AetherControl.App.Theming;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Hardware;
using AetherControl.Services.Optimisation;
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
    private readonly AutostartService _autostartService;

    // No light or system theme is actually implemented anywhere — Themes/Colors.xaml is a single
    // hardcoded dark palette with no RequestedTheme/prefers-color-scheme handling at all. AppSettings
    // still has a Theme field (left alone — no reason to force a data migration over this), but the
    // picker that let a user select an option with zero effect was removed from Settings' UI.
    [ObservableProperty] private AccentColor accent;
    [ObservableProperty] private double dashboardRefreshMs;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private bool startMinimisedToTray;
    [ObservableProperty] private bool minimiseToTrayOnClose;
    [ObservableProperty] private double historyRetentionDays;
    [ObservableProperty] private bool loggingEnabled;
    [ObservableProperty] private string logLevel = "Information";
    [ObservableProperty] private string statusMessage = string.Empty;
    [ObservableProperty] private IReadOnlyList<TrayMetricOption> trayMetricOptions = [];
    [ObservableProperty] private bool temperatureAlertsEnabled;
    [ObservableProperty] private double cpuTemperatureAlertThreshold;
    [ObservableProperty] private double gpuTemperatureAlertThreshold;

    public IReadOnlyList<AccentColor> AccentOptions { get; } = Enum.GetValues<AccentColor>();
    public IReadOnlyList<string> LogLevelOptions { get; } = ["Debug", "Information", "Warning", "Error"];

    public SettingsViewModel(ISettingsService settingsService, AlertSettingsStore alertSettingsStore, AutostartService autostartService)
    {
        _settingsService = settingsService;
        _alertSettingsStore = alertSettingsStore;
        _autostartService = autostartService;
        var current = _settingsService.Current;
        Accent = current.Accent;
        DashboardRefreshMs = current.DashboardRefreshMs;
        // Reflects the real Task Scheduler state, not just whatever was last saved to the database —
        // the two can legitimately drift (e.g. the user removed the task manually, or SetEnabled's
        // best-effort schtasks call silently failed last time).
        StartWithWindows = _autostartService.IsEnabled();
        StartMinimisedToTray = current.StartMinimisedToTray;
        MinimiseToTrayOnClose = current.MinimiseToTrayOnClose;
        HistoryRetentionDays = current.HistoryRetentionDays;
        LoggingEnabled = current.LoggingEnabled;
        LogLevel = current.LogLevel;

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
        updated.Accent = Accent;
        updated.DashboardRefreshMs = (int)DashboardRefreshMs;
        updated.StartWithWindows = StartWithWindows;
        updated.StartMinimisedToTray = StartMinimisedToTray;
        updated.MinimiseToTrayOnClose = MinimiseToTrayOnClose;
        updated.HistoryRetentionDays = (int)HistoryRetentionDays;
        updated.LoggingEnabled = LoggingEnabled;
        updated.LogLevel = LogLevel;

        // Was previously just a bool saved to the database — nothing ever created or removed the
        // actual Task Scheduler entry, so the toggle had no real effect either way.
        _autostartService.SetEnabled(StartWithWindows);

        await _settingsService.SaveAsync(updated);
        LoggingSettings.Apply(updated.LoggingEnabled, updated.LogLevel);

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
