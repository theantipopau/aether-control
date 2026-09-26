using System.Reflection;
using Microsoft.Data.Sqlite;

namespace AetherControl.Data;

/// <summary>
/// Owns the single SQLite database file used for settings, history and layouts.
/// Applies embedded .sql migrations in order on first use, each exactly once, tracked by
/// SQLite's <c>PRAGMA user_version</c>. Consumers request short-lived connections via
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

            // Previously every script re-ran on every launch — harmless only while all of them were
            // idempotent CREATE ... IF NOT EXISTS. A non-idempotent step (ALTER TABLE ADD COLUMN)
            // would throw on the second launch. Now tracked via PRAGMA user_version: each script's
            // numeric prefix (002_...) runs once, in a transaction, then the version is bumped.
            // Existing databases start at user_version 0, so 001 re-runs once — safe, it's idempotent.
            using (var versionCommand = connection.CreateCommand())
            {
                versionCommand.CommandText = "PRAGMA user_version;";
                var appliedVersion = Convert.ToInt32(await versionCommand.ExecuteScalarAsync(ct).ConfigureAwait(false));

                var assembly = Assembly.GetExecutingAssembly();
                var migrations = assembly.GetManifestResourceNames()
                    .Where(name => name.Contains(".Migrations.") && name.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
                    .Select(name => (Name: name, Number: MigrationNumber(name)))
                    .Where(m => m.Number > appliedVersion)
                    .OrderBy(m => m.Number);

                foreach (var (resourceName, number) in migrations)
                {
                    await using var stream = assembly.GetManifestResourceStream(resourceName)
                        ?? throw new InvalidOperationException($"Missing embedded migration resource: {resourceName}");
                    using var reader = new StreamReader(stream);
                    var sql = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                    await using var transaction = connection.BeginTransaction();
                    using var command = connection.CreateCommand();
                    command.Transaction = transaction;
                    // PRAGMA can't take a parameter; number is parsed from our own embedded file name.
                    command.CommandText = sql + $"\nPRAGMA user_version = {number};";
                    await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    await transaction.CommitAsync(ct).ConfigureAwait(false);
                }
            }

            _migrated = true;
        }
        finally
        {
            _migrationLock.Release();
        }
    }

    /// <summary>"AetherControl.Data.Migrations.002_UpdateCheck.sql" → 2.</summary>
    internal static int MigrationNumber(string resourceName)
    {
        var fileName = resourceName[(resourceName.IndexOf(".Migrations.", StringComparison.Ordinal) + ".Migrations.".Length)..];
        // Embedded resource names get a leading underscore when the file name starts with a digit.
        var digits = new string(fileName.TrimStart('_').TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var number)
            ? number
            : throw new InvalidOperationException($"Migration '{resourceName}' has no numeric prefix.");
    }
}
