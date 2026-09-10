using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using Microsoft.Data.Sqlite;

namespace AetherControl.Data.Repositories;

public sealed class SettingsRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task<AppSettings> GetAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT theme, accent, dashboard_refresh_ms, start_with_windows, start_minimised_to_tray, minimise_to_tray_on_close, history_retention_days, logging_enabled, log_level FROM app_settings WHERE id = 1;";

        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return new AppSettings();
        }

        return new AppSettings
        {
            Theme = Enum.Parse<ThemeMode>(reader.GetString(0)),
            Accent = Enum.Parse<AccentColor>(reader.GetString(1)),
            DashboardRefreshMs = reader.GetInt32(2),
            StartWithWindows = reader.GetInt32(3) != 0,
            StartMinimisedToTray = reader.GetInt32(4) != 0,
            MinimiseToTrayOnClose = reader.GetInt32(5) != 0,
            HistoryRetentionDays = reader.GetInt32(6),
            LoggingEnabled = reader.GetInt32(7) != 0,
            LogLevel = reader.GetString(8)
        };
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE app_settings SET
                theme = $theme, accent = $accent, dashboard_refresh_ms = $refresh,
                start_with_windows = $startWithWindows, start_minimised_to_tray = $startMinimised,
                minimise_to_tray_on_close = $minimiseOnClose, history_retention_days = $retention,
                logging_enabled = $loggingEnabled, log_level = $logLevel
            WHERE id = 1;
            """;
        command.Parameters.AddWithValue("$theme", settings.Theme.ToString());
        command.Parameters.AddWithValue("$accent", settings.Accent.ToString());
        command.Parameters.AddWithValue("$refresh", settings.DashboardRefreshMs);
        command.Parameters.AddWithValue("$startWithWindows", settings.StartWithWindows ? 1 : 0);
        command.Parameters.AddWithValue("$startMinimised", settings.StartMinimisedToTray ? 1 : 0);
        command.Parameters.AddWithValue("$minimiseOnClose", settings.MinimiseToTrayOnClose ? 1 : 0);
        command.Parameters.AddWithValue("$retention", settings.HistoryRetentionDays);
        command.Parameters.AddWithValue("$loggingEnabled", settings.LoggingEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$logLevel", settings.LogLevel);

        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TrayMetricPreference>> GetTrayMetricPreferencesAsync(CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT metric, enabled, sort_order FROM tray_metric_preferences ORDER BY sort_order;";

        var results = new List<TrayMetricPreference>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            if (!Enum.TryParse<MetricKind>(reader.GetString(0), out var metric))
            {
                continue;
            }

            results.Add(new TrayMetricPreference
            {
                Metric = metric,
                Enabled = reader.GetInt32(1) != 0,
                Order = reader.GetInt32(2)
            });
        }

        return results;
    }

    public async Task SaveTrayMetricPreferencesAsync(IReadOnlyList<TrayMetricPreference> preferences, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        foreach (var preference in preferences)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO tray_metric_preferences (metric, enabled, sort_order)
                VALUES ($metric, $enabled, $order)
                ON CONFLICT(metric) DO UPDATE SET enabled = excluded.enabled, sort_order = excluded.sort_order;
                """;
            command.Parameters.AddWithValue("$metric", preference.Metric.ToString());
            command.Parameters.AddWithValue("$enabled", preference.Enabled ? 1 : 0);
            command.Parameters.AddWithValue("$order", preference.Order);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }
}
