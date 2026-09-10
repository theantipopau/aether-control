using Microsoft.Data.Sqlite;

namespace AetherControl.Data.Repositories;

public sealed class OptimisationLogRepository(SqliteConnectionFactory connectionFactory)
{
    public async Task LogAsync(string taskId, bool success, string message, long? bytesReclaimed, string? restorePointDescription, CancellationToken ct = default)
    {
        await using var connection = await connectionFactory.CreateConnectionAsync(ct).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO optimisation_run_log (task_id, ran_at_utc, success, message, bytes_reclaimed, restore_point_description)
            VALUES ($taskId, $ranAt, $success, $message, $bytes, $restorePoint);
            """;
        command.Parameters.AddWithValue("$taskId", taskId);
        command.Parameters.AddWithValue("$ranAt", DateTime.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$success", success ? 1 : 0);
        command.Parameters.AddWithValue("$message", message);
        command.Parameters.AddWithValue("$bytes", (object?)bytesReclaimed ?? DBNull.Value);
        command.Parameters.AddWithValue("$restorePoint", (object?)restorePointDescription ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
