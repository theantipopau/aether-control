using System.Text.Json;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Data.Repositories;

namespace AetherControl.Services.Settings;

public sealed class SettingsService(SettingsRepository settingsRepository) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        Current = await settingsRepository.GetAsync(ct).ConfigureAwait(false);
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await settingsRepository.SaveAsync(settings, ct).ConfigureAwait(false);
        Current = settings;
        SettingsChanged?.Invoke(this, settings);
    }

    public Task<IReadOnlyList<TrayMetricPreference>> GetTrayMetricPreferencesAsync(CancellationToken ct = default)
        => settingsRepository.GetTrayMetricPreferencesAsync(ct);

    public Task SaveTrayMetricPreferencesAsync(IReadOnlyList<TrayMetricPreference> preferences, CancellationToken ct = default)
        => settingsRepository.SaveTrayMetricPreferencesAsync(preferences, ct);

    public async Task<string> ExportConfigurationAsync(CancellationToken ct = default)
    {
        var trayPreferences = await settingsRepository.GetTrayMetricPreferencesAsync(ct).ConfigureAwait(false);
        var export = new ConfigurationExport(Current, trayPreferences);
        return JsonSerializer.Serialize(export, JsonOptions);
    }

    public async Task ImportConfigurationAsync(string json, CancellationToken ct = default)
    {
        var import = JsonSerializer.Deserialize<ConfigurationExport>(json)
            ?? throw new InvalidOperationException("Configuration file is empty or malformed.");

        await SaveAsync(import.Settings, ct).ConfigureAwait(false);
        await SaveTrayMetricPreferencesAsync(import.TrayMetrics, ct).ConfigureAwait(false);
    }

    private sealed record ConfigurationExport(AppSettings Settings, IReadOnlyList<TrayMetricPreference> TrayMetrics);
}
