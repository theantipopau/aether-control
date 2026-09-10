using System.Globalization;
using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using Microsoft.Data.Sqlite;

namespace AetherControl.Data.Repositories;

public sealed class HistoryRepository(SqliteConnectionFactory connectionFactory)
{
    private const string IsoFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";

    public async Task InsertAsync(IReadOnlyList<SensorSample> samples, CancellationToken ct = default)
    {
        if (samples.Count == 0)
        {
            return;
        }

        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction();

        foreach (var sample in samples)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "INSERT INTO sensor_history (timestamp_utc, metric, value) VALUES ($ts, $metric, $value);";
            command.Parameters.AddWithValue("$ts", sample.TimestampUtc.UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$metric", sample.Metric.ToString());
            command.Parameters.AddWithValue("$value", sample.Value);
            await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await transaction.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SensorSample>> QueryAsync(
        MetricKind metric,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        HistoryResolution resolution,
        CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();

        var bucketExpression = resolution switch
        {
            HistoryResolution.Daily => "strftime('%Y-%m-%d', timestamp_utc)",
            HistoryResolution.Weekly => "strftime('%Y-%W', timestamp_utc)",
            HistoryResolution.Monthly => "strftime('%Y-%m', timestamp_utc)",
            _ => null
        };

        if (bucketExpression is null)
        {
            command.CommandText = """
                SELECT timestamp_utc, value FROM sensor_history
                WHERE metric = $metric AND timestamp_utc BETWEEN $from AND $to
                ORDER BY timestamp_utc;
                """;
        }
        else
        {
            command.CommandText = $"""
                SELECT MIN(timestamp_utc) AS bucket_start, AVG(value) AS avg_value
                FROM sensor_history
                WHERE metric = $metric AND timestamp_utc BETWEEN $from AND $to
                GROUP BY {bucketExpression}
                ORDER BY bucket_start;
                """;
        }

        command.Parameters.AddWithValue("$metric", metric.ToString());
        command.Parameters.AddWithValue("$from", fromUtc.UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", toUtc.UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture));

        var results = new List<SensorSample>();
        await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            results.Add(new SensorSample
            {
                TimestampUtc = DateTime.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                Metric = metric,
                Value = reader.GetDouble(1)
            });
        }

        return results;
    }

    public async Task PurgeOlderThanAsync(DateTimeOffset cutoffUtc, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sensor_history WHERE timestamp_utc < $cutoff;";
        command.Parameters.AddWithValue("$cutoff", cutoffUtc.UtcDateTime.ToString(IsoFormat, CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
