using System.Reflection;
using Microsoft.Data.Sqlite;

namespace AetherControl.Data;

/// <summary>
/// Owns the single SQLite database file used for settings, history and layouts.
/// Applies embedded .sql migrations in order on first use, tracked by
/// <c>schema_version</c>. Consumers request short-lived connections via
/// <see cref="CreateConnectionAsync"/> rather than holding one open, since
/// SQLite handles many short connections against WAL mode well and this keeps
/// the file unlocked for backup/export.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _databasePath;
    private readonly SemaphoreSlim _migrationLock = new(1, 1);
    private bool _migrated;

    public SqliteConnectionFactory(string databasePath)
    {
        _databasePath = databasePath;
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
    }

    public static string GetDefaultDatabasePath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(root, "Aether Control", "aethercontrol.db");
    }

    public async Task<SqliteConnection> CreateConnectionAsync(CancellationToken ct = default)
    {
        await EnsureMigratedAsync(ct).ConfigureAwait(false);

        var connection = new SqliteConnection($"Data Source={_databasePath};Cache=Shared");
        await connection.OpenAsync(ct).ConfigureAwait(false);

        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        return connection;
    }

    private async Task EnsureMigratedAsync(CancellationToken ct)
    {
        if (_migrated)
        {
            return;
        }

        await _migrationLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_migrated)
            {
                return;
            }

            await using var connection = new SqliteConnection($"Data Source={_databasePath}");
            await connection.OpenAsync(ct).ConfigureAwait(false);

            var assembly = Assembly.GetExecutingAssembly();
            var migrationResources = assembly.GetManifestResourceNames()
                .Where(name => name.Contains(".Migrations.") && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                .OrderBy(name => name, StringComparer.Ordinal);

            foreach (var resourceName in migrationResources)
            {
                await using var stream = assembly.GetManifestResourceStream(resourceName)
                    ?? throw new InvalidOperationException($"Missing embedded migration resource: {resourceName}");
                using var reader = new StreamReader(stream);
                var sql = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                using var command = connection.CreateCommand();
                command.CommandText = sql;
                await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }

            _migrated = true;
        }
        finally
        {
            _migrationLock.Release();
        }
    }
}
