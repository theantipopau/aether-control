using AetherControl.Core.Events;
using AetherControl.Core.Fans;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using AetherControl.Services.Optimisation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AetherControl.Tests;

public sealed class FanCurveControllerTests : IDisposable
{
    private sealed class FakeFanControl : IFanControlService
    {
        public bool IsSoftwareControlSafe { get; set; } = true;
        public List<FanControlChannel> Channels { get; } = [];
        public List<(string Id, int Percent)> Writes { get; } = [];
        public int ResetAllCount { get; private set; }

        public bool IsConflictingVendorSoftwareRunning() => false;
        public IReadOnlyList<string> RunningConflictingSoftware() => [];
        public IReadOnlyList<FanControlChannel> GetChannels() => Channels.ToList();

        public void SetPercent(string channelId, int percent) => Writes.Add((channelId, percent));
        public void ResetToAutomatic(string channelId) { }
        public void ResetAllToAutomatic() => ResetAllCount++;
    }

    private sealed class FakeMonitor : IHardwareMonitorService
    {
        public HardwareSnapshot? LatestSnapshot { get; private set; }
        public event EventHandler<SensorsUpdatedEventArgs>? SnapshotUpdated;

        public void Publish(double cpuTempC)
        {
            var snapshot = new HardwareSnapshot { Cpu = new CpuInfo { TemperatureCelsius = cpuTempC } };
            LatestSnapshot = snapshot;
            SnapshotUpdated?.Invoke(this, new SensorsUpdatedEventArgs(snapshot));
        }

        public void Start(TimeSpan pollingInterval) { }
        public void Stop() { }
        public void SetPollingInterval(TimeSpan interval) { }
        public void Dispose() { }
    }

    private static FanCurve TestCurve() => new("Test",
    [
        new FanCurvePoint(40, 20),
        new FanCurvePoint(80, 100)
    ]);

    private readonly FakeFanControl _fans = new();
    private readonly FakeMonitor _monitor = new();
    private readonly List<FanCurveController> _controllers = [];

    private FanCurveController Create()
    {
        var controller = new FanCurveController(_fans, _monitor, NullLogger<FanCurveController>.Instance);
        _controllers.Add(controller);
        return controller;
    }

    public void Dispose()
    {
        foreach (var controller in _controllers)
        {
            controller.Dispose();
        }
    }

    private void AddChannel(string id = "ch1", double currentPercent = 20) =>
        _fans.Channels.Add(new FanControlChannel { Id = id, Name = "Fan", CurrentPercent = currentPercent });

    [Fact]
    public void HotSnapshot_WritesInterpolatedTarget()
    {
        AddChannel();
        var controller = Create();
        controller.Start(TestCurve());

        _monitor.Publish(60); // halfway (40,20)→(80,100) → 60

        Assert.Contains(("ch1", 60), _fans.Writes);
    }

    [Fact]
    public void SubStepWobble_DoesNotRewrite()
    {
        AddChannel(currentPercent: 58);
        var controller = Create();
        controller.Start(TestCurve());

        _monitor.Publish(60); // target 60 vs live 58 — inside the 5pp gate

        Assert.Empty(_fans.Writes);
    }

    [Fact]
    public void MissingTemperature_NeverDrivesFans()
    {
        AddChannel();
        var controller = Create();
        controller.Start(TestCurve());

        _monitor.Publish(0);

        Assert.Empty(_fans.Writes);
    }

    [Fact]
    public void UnsafeControl_NeverDrivesFans()
    {
        AddChannel();
        _fans.IsSoftwareControlSafe = false;
        var controller = Create();
        controller.Start(TestCurve());

        _monitor.Publish(90);

        Assert.Empty(_fans.Writes);
    }

    [Fact]
    public void InactiveController_IgnoresSnapshots()
    {
        AddChannel();
        Create(); // never started

        _monitor.Publish(90);

        Assert.Empty(_fans.Writes);
    }

    [Fact]
    public void Stop_ResetsToAutomatic_AndStopsDriving()
    {
        AddChannel();
        var controller = Create();
        controller.Start(TestCurve());
        controller.Stop();

        Assert.Equal(1, _fans.ResetAllCount);
        _fans.Writes.Clear();
        _monitor.Publish(90);

        Assert.Empty(_fans.Writes);
    }

    [Fact]
    public void Dispose_Unsubscribes_AndResetsToAutomatic()
    {
        AddChannel();
        var controller = Create();
        controller.Start(TestCurve());
        controller.Dispose();

        Assert.Equal(1, _fans.ResetAllCount);
        _monitor.Publish(90);

        Assert.Empty(_fans.Writes);
    }
}
