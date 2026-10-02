using AetherControl.Core.Timing;

namespace AetherControl.Tests;

/// <summary>
/// The idle gate behind the adaptive poll cadence: slow down only after usage has been genuinely
/// quiet for the full stretch, and snap back to full rate on the first busy sample.
/// </summary>
public class IdleDetectorTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FirstLowSample_IsNotYetIdle()
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));

        Assert.False(detector.IsIdle(T0, cpuUtilisationPercent: 2, gpuUtilisationPercent: 0));
    }

    [Fact]
    public void SustainedLowUsage_BecomesIdleExactlyAfterTheThreshold()
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));

        detector.IsIdle(T0, 2, 0);
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(14.9), 2, 0));
        Assert.True(detector.IsIdle(T0 + TimeSpan.FromSeconds(15), 2, 0));
    }

    [Fact]
    public void BusySample_ExitsIdleAndRestartsTheStretch()
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));

        detector.IsIdle(T0, 2, 0);
        // A burst10s in: idle ends immediately, even though the next samples are low again.
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(10), cpuUtilisationPercent: 45, gpuUtilisationPercent: 0));

        detector.IsIdle(T0 + TimeSpan.FromSeconds(11), 2, 0);
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(20), 2, 0)); // only 9s into the new stretch
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(25.9), 2, 0)); // 14.9s — still short
        Assert.True(detector.IsIdle(T0 + TimeSpan.FromSeconds(26), 2, 0)); // full 15s since the burst
    }

    [Fact]
    public void GpuLoadAlone_CountsAsBusy()
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));

        detector.IsIdle(T0, 2, 0);
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(15), cpuUtilisationPercent: 3, gpuUtilisationPercent: 80));
    }

    [Theory]
    [InlineData(9.99, false)] // under the default 10 % threshold → still idle-eligible
    [InlineData(10, true)] // at the threshold → busy
    public void ThresholdIsInclusive(double cpuPercent, bool expectBusy)
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));
        detector.IsIdle(T0, 2, 0);

        var idle = detector.IsIdle(T0 + TimeSpan.FromSeconds(15), cpuPercent, 0);

        Assert.Equal(!expectBusy, idle);
    }

    [Fact]
    public void CustomThresholdAndDuration_AreHonoured()
    {
        var detector = new IdleDetector(busyThresholdPercent: 25, minimumIdleDuration: TimeSpan.FromSeconds(5));

        detector.IsIdle(T0, 20, 0);
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(4), 20, 0));
        Assert.True(detector.IsIdle(T0 + TimeSpan.FromSeconds(5), 20, 0));
        Assert.False(detector.IsIdle(T0 + TimeSpan.FromSeconds(6), 30, 0));
    }

    [Fact]
    public void ClockGoingBackwards_ReadsAsNotYetIdle()
    {
        var detector = new IdleDetector(minimumIdleDuration: TimeSpan.FromSeconds(15));

        detector.IsIdle(T0, 2, 0);
        Assert.False(detector.IsIdle(T0 - TimeSpan.FromSeconds(30), 2, 0));
    }
}
