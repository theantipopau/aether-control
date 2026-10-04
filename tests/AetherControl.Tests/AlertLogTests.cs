using AetherControl.Core.Alerts;

namespace AetherControl.Tests;

public class AlertLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Empty_Log_Has_No_Snapshot_And_No_Recent_Alerts()
    {
        var log = new AlertLog();

        Assert.Empty(log.Snapshot());
        Assert.Empty(log.Recent(TimeSpan.FromMinutes(15), Now));
    }

    [Fact]
    public void Snapshot_Returns_Newest_First()
    {
        var log = new AlertLog();
        log.Add(Now.AddMinutes(-2), "first", "m1");
        log.Add(Now.AddMinutes(-1), "second", "m2");

        var entries = log.Snapshot();

        Assert.Equal(2, entries.Count);
        Assert.Equal("second", entries[0].Title);
        Assert.Equal("first", entries[1].Title);
    }

    [Fact]
    public void Capacity_Trim_Drops_The_Oldest_Entry()
    {
        var log = new AlertLog(capacity: 3);
        for (var i = 0; i < 5; i++)
        {
            log.Add(Now.AddMinutes(-i), $"alert-{i}", "m");
        }

        var entries = log.Snapshot();

        Assert.Equal(3, entries.Count);
        // alert-4 was added last (newest) — alert-0 and alert-1 are gone.
        Assert.Equal("alert-4", entries[0].Title);
        Assert.Equal("alert-3", entries[1].Title);
        Assert.Equal("alert-2", entries[2].Title);
    }

    [Fact]
    public void Recent_Keeps_Fresh_Alerts_Newest_First()
    {
        var log = new AlertLog();
        log.Add(Now.AddMinutes(-10), "cpu", "m1");
        log.Add(Now.AddMinutes(-1), "gpu", "m2");

        var recent = log.Recent(TimeSpan.FromMinutes(15), Now);

        Assert.Equal(2, recent.Count);
        Assert.Equal("gpu", recent[0].Title);
        Assert.Equal("cpu", recent[1].Title);
    }

    [Fact]
    public void Recent_Hides_Alerts_Older_Than_The_Window()
    {
        var log = new AlertLog();
        log.Add(Now.AddMinutes(-20), "cpu running hot", "reached 90 °C");
        log.Add(Now.AddMinutes(-5), "gpu running hot", "reached 90 °C");

        var recent = log.Recent(TimeSpan.FromMinutes(15), Now);

        Assert.Single(recent);
        Assert.Equal("gpu running hot", recent[0].Title);
    }

    [Fact]
    public void Recent_Is_Empty_Once_Everything_Has_Aged_Out()
    {
        var log = new AlertLog();
        log.Add(Now.AddMinutes(-60), "cpu running hot", "reached 90 °C");

        Assert.Empty(log.Recent(TimeSpan.FromMinutes(15), Now));
    }
}
