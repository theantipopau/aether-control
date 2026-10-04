using AetherControl.App.Services;
using AetherControl.App.Theming;
using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Core.Updates;
using AetherControl.Services.Updates;
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
    private readonly AetherControl.Core.Alerts.AlertLog _alertLog;

    // Dark-only: Themes/Colors.xaml is a single hardcoded dark palette and App.xaml pins
    // RequestedTheme="Dark" so stock WinUI chrome matches it regardless of the system theme.
    // AppSettings still has a Theme field (left alone — no reason to force a data migration over
    // this), but the picker that let a user select an option with zero effect was removed from
    // Settings' UI.
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

    // ── About & updates ──
    private readonly IUpdateCheckService _updateCheckService;
    [ObservableProperty] private bool checkForUpdatesOnStartup;
    [ObservableProperty] private string updateStatusText = "Not checked yet.";
    [ObservableProperty] private string? releaseUrl;
    [ObservableProperty] private Uri? releaseUri;
    [ObservableProperty] private bool isCheckingForUpdates;

    /// <summary>"0.9.0" plus the short commit the build came from, when the SDK stamped one.</summary>
    public string AppVersionText { get; } = FormatVersion(GitHubUpdateCheckService.CurrentVersion);
    public string RuntimeText { get; } = $".NET {Environment.Version} · {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";

    public SettingsViewModel(ISettingsService settingsService, AlertSettingsStore alertSettingsStore, AutostartService autostartService,
        IUpdateCheckService updateCheckService, AetherControl.Core.Alerts.AlertLog alertLog)
    {
        _updateCheckService = updateCheckService;
        _alertLog = alertLog;
        _settingsService = settingsService;
        CheckForUpdatesOnStartup = settingsService.Current.CheckForUpdatesOnStartup;
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
        updated.CheckForUpdatesOnStartup = CheckForUpdatesOnStartup;

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

    /// <summary>Fires the exact toast + AlertLog path a real temperature trip uses, so the whole
    /// pipeline (registration → toast → Overview hero) is verifiable without heating the CPU.
    /// Records the test as an alert on purpose — seeing it appear in the hero is the point.</summary>
    [RelayCommand]
    private void SendTestAlert()
    {
        var now = DateTimeOffset.UtcNow;
        var title = "Test alert";
        var message = $"This is a test — thresholds are {CpuTemperatureAlertThreshold:F0} °C (CPU) and {GpuTemperatureAlertThreshold:F0} °C (GPU).";
        _alertLog.Add(now, title, message);
        TemperatureAlertService.ShowAlert(title, message);
        StatusMessage = "Test notification sent.";
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        UpdateStatusText = "Checking GitHub releases…";
        try
        {
            var result = await _updateCheckService.CheckAsync();
            UpdateStatusText = result.Message ?? result.Status.ToString();
            ReleaseUrl = result.Status == UpdateStatus.UpdateAvailable ? result.ReleaseUrl : null;
            ReleaseUri = Uri.TryCreate(ReleaseUrl, UriKind.Absolute, out var uri) ? uri : null;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private static string FormatVersion(string informational)
    {
        // "0.9.0+4f2c1ab9e..." → "0.9.0 (4f2c1ab)"
        var plus = informational.IndexOf('+');
        if (plus < 0)
        {
            return informational;
        }

        var hash = informational[(plus + 1)..];
        return $"{informational[..plus]} ({hash[..Math.Min(7, hash.Length)]})";
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
