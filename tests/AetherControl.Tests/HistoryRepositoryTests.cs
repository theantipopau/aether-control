using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using AetherControl.Data;
using AetherControl.Data.Repositories;
using Microsoft.Data.Sqlite;

namespace AetherControl.Tests;

public class HistoryRepositoryTests : IDisposable
{
    private readonly string _dbPath;
    private readonly HistoryRepository _repository;

    public HistoryRepositoryTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"aethercontrol-test-{Guid.NewGuid():N}.db");
        _repository = new HistoryRepository(new SqliteConnectionFactory(_dbPath));
    }

    [Fact]
    public async Task InsertAndQuery_RawResolution_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new List<SensorSample>
        {
            new() { Metric = MetricKind.CpuTemperature, Value = 50, TimestampUtc = now.AddMinutes(-2) },
            new() { Metric = MetricKind.CpuTemperature, Value = 60, TimestampUtc = now.AddMinutes(-1) },
            new() { Metric = MetricKind.GpuTemperature, Value = 999, TimestampUtc = now } // different metric — must not leak into the query
        };

        await _repository.InsertAsync(samples);

        var results = await _repository.QueryAsync(
            MetricKind.CpuTemperature, now.AddHours(-1), now.AddHours(1), HistoryResolution.Raw);

        Assert.Equal(2, results.Count);
        Assert.Equal(50, results[0].Value);
        Assert.Equal(60, results[1].Value);
    }

    [Fact]
    public async Task PurgeOlderThan_RemovesOnlyExpiredSamples()
    {
        var now = DateTimeOffset.UtcNow;
        await _repository.InsertAsync(
        [
            new SensorSample { Metric = MetricKind.CpuTemperature, Value = 1, TimestampUtc = now.AddDays(-10) },
            new SensorSample { Metric = MetricKind.CpuTemperature, Value = 2, TimestampUtc = now }
        ]);

        await _repository.PurgeOlderThanAsync(now.AddDays(-1));

        var remaining = await _repository.QueryAsync(
            MetricKind.CpuTemperature, now.AddDays(-30), now.AddDays(1), HistoryResolution.Raw);

        Assert.Single(remaining);
        Assert.Equal(2, remaining[0].Value);
    }

    public void Dispose()
    {
        // Microsoft.Data.Sqlite pools connections by connection string, so the native handle
        // can outlive the SqliteConnection's own Dispose — clear the pool first or the temp
        // file is still locked when we try to delete it here.
        SqliteConnection.ClearAllPools();

        foreach (var path in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
