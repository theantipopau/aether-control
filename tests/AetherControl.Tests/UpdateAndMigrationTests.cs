using AetherControl.Core.Models;
using AetherControl.Core.Updates;
using AetherControl.Data;
using AetherControl.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace AetherControl.Tests;

/// <summary>
/// Pins two things added with the Settings → About/updates work: the release-tag comparison (an
/// unparseable or equal tag must never be reported as an update), and the migration runner, which
/// previously re-ran every script on every launch — fatal the moment a non-idempotent
/// ALTER TABLE (002_UpdateCheck) exists.
/// </summary>
public class UpdateAndMigrationTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"aethercontrol-test-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("v1.0.0", "0.9.0", true)]
    [InlineData("0.9.1", "0.9.0+4f2c1ab", true)]
    [InlineData("v0.9.0", "0.9.0+4f2c1ab", false)]   // same version, build metadata ignored
    [InlineData("0.9", "0.9.0", false)]               // missing components normalised
    [InlineData("v0.8.5", "0.9.0", false)]
    [InlineData("1.0.0-preview.1", "0.9.0", true)]
    [InlineData("latest", "0.9.0", false)]            // unparseable tag never nags
    [InlineData(null, "0.9.0", false)]
    public void IsNewer_ComparesNumericCoreOnly(string? candidate, string current, bool expected) =>
        Assert.Equal(expected, ReleaseVersion.IsNewer(candidate, current));

    [Fact]
    public async Task Migrations_RunOnce_SurviveSecondLaunch_AndPersistNewSetting()
    {
        var first = new SettingsRepository(new SqliteConnectionFactory(_dbPath));
        var settings = await first.GetAsync();
        Assert.False(settings.CheckForUpdatesOnStartup); // default off — opt-in only

        settings.CheckForUpdatesOnStartup = true;
        await first.SaveAsync(settings);

        // A fresh factory is a second app launch: the ALTER TABLE must not run again.
        var second = new SettingsRepository(new SqliteConnectionFactory(_dbPath));
        Assert.True((await second.GetAsync()).CheckForUpdatesOnStartup);
    }

    [Fact]
    public async Task Migrations_UpgradeDatabaseCreatedBeforeVersionTracking()
    {
        // Simulate a pre-tracking database: 001's tables exist, user_version still 0.
        await using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE app_settings (id INTEGER PRIMARY KEY CHECK (id = 1), theme TEXT NOT NULL DEFAULT 'Dark',
                    accent TEXT NOT NULL DEFAULT 'Cyan', dashboard_refresh_ms INTEGER NOT NULL DEFAULT 1000,
                    start_with_windows INTEGER NOT NULL DEFAULT 0, start_minimised_to_tray INTEGER NOT NULL DEFAULT 1,
                    minimise_to_tray_on_close INTEGER NOT NULL DEFAULT 1, history_retention_days INTEGER NOT NULL DEFAULT 90,
                    logging_enabled INTEGER NOT NULL DEFAULT 1, log_level TEXT NOT NULL DEFAULT 'Information');
                INSERT INTO app_settings (id, accent) VALUES (1, 'Purple');
                """;
            await command.ExecuteNonQueryAsync();
        }

        SqliteConnection.ClearAllPools();
        var settings = await new SettingsRepository(new SqliteConnectionFactory(_dbPath)).GetAsync();

        Assert.Equal(AetherControl.Core.Enums.AccentColor.Purple, settings.Accent); // existing data preserved
        Assert.False(settings.CheckForUpdatesOnStartup);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(path); } catch { /* temp file — best effort */ }
        }
    }
}
