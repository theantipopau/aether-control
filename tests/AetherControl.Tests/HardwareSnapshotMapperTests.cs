using AetherControl.Services.Hardware;
using AetherControl.Tests.Fakes;
using LibreHardwareMonitor.Hardware;

namespace AetherControl.Tests;

/// <summary>
/// Pins <see cref="HardwareSnapshotMapper"/> against sensor trees shaped like real dumps captured
/// live (2026-09) on an ASUS PRIME B650EM-A WIFI / Ryzen 7 9800X3D — the exact case that motivated
/// the "Cores (Average Effective)" clock preference, the CoreVoltage VID-rejection, and the
/// Board Temperatures generic-name fallback in the mapper. Regressing any of these silently brings
/// back a number that looks live but is wrong (see the mapper's own comments for the full story).
/// </summary>
public class HardwareSnapshotMapperTests
{
    [Fact]
    public void MapCpu_PrefersEffectiveClock_OverBoostCeilingAggregate()
    {
        // "Cores (Average)" is this chip's near-constant boost-clock request (~5395MHz) — real, but
        // not what "current clock speed" should mean on a dashboard. "Cores (Average Effective)" is
        // the one that actually tracks load (1535MHz at the same instant, captured live).
        var cpu = new FakeHardware(HardwareType.Cpu, "AMD Ryzen 7 9800X3D", sensors:
        [
            new FakeSensor(SensorType.Clock, "Cores (Average)", 5395f),
            new FakeSensor(SensorType.Clock, "Cores (Average Effective)", 1535f),
        ]);

        var info = HardwareSnapshotMapper.MapCpu(cpu);

        Assert.Equal(1535f, info.ClockSpeedMhz);
    }

    [Fact]
    public void MapCpu_FallsBackToBoostCeiling_WhenNoEffectiveSensorExists()
    {
        // Intel parts / older LHM versions expose no "(Effective)" clock at all — the aggregate is
        // still better than reporting 0.
        var cpu = new FakeHardware(HardwareType.Cpu, "Generic CPU", sensors:
        [
            new FakeSensor(SensorType.Clock, "Cores (Average)", 4200f),
        ]);

        var info = HardwareSnapshotMapper.MapCpu(cpu);

        Assert.Equal(4200f, info.ClockSpeedMhz);
    }

    [Fact]
    public void MapCpu_PerCoreClock_DoesNotBlendEffectiveAndNonEffectiveSensors()
    {
        // Regression for the bug this fixed: a plain "Core #" substring match pairs "Core #1" (boost
        // ceiling) and "Core #1 (Effective)" (real) into one meaningless average. Per-core output must
        // come entirely from the effective set once any effective sensor exists.
        var cpu = new FakeHardware(HardwareType.Cpu, "AMD Ryzen 7 9800X3D", sensors:
        [
            new FakeSensor(SensorType.Load, "Core #1", 42f),
            new FakeSensor(SensorType.Clock, "Core #1", 5395f),
            new FakeSensor(SensorType.Clock, "Core #1 (Effective)", 1620f),
        ]);

        var info = HardwareSnapshotMapper.MapCpu(cpu);
        var core1 = Assert.Single(info.Cores);

        Assert.Equal(1620f, core1.ClockSpeedMhz);
    }

    [Fact]
    public void MapCpu_CoreVoltage_RejectsVidRequest_ReportsZeroInstead()
    {
        // "Core #1 VID" (~0.19-0.2V on this chip) is the VID *requested* of the VRM, not a measured
        // voltage, and used to be matched by substring and shown as if it were real CoreVoltage.
        // Confidently wrong is worse than honestly unavailable — this must come back 0, not ~0.2.
        var cpu = new FakeHardware(HardwareType.Cpu, "AMD Ryzen 7 9800X3D", sensors:
        [
            new FakeSensor(SensorType.Voltage, "Core #1 VID", 0.198f),
        ]);

        var info = HardwareSnapshotMapper.MapCpu(cpu);

        Assert.Equal(0f, info.CoreVoltage);
    }

    [Fact]
    public void MapCpu_CoreVoltage_AcceptsRealVoltageRailNames()
    {
        var cpu = new FakeHardware(HardwareType.Cpu, "AMD Ryzen 7 9800X3D", sensors:
        [
            new FakeSensor(SensorType.Voltage, "CPU Core", 1.392f),
        ]);

        var info = HardwareSnapshotMapper.MapCpu(cpu);

        Assert.Equal(1.392f, info.CoreVoltage);
    }

    [Fact]
    public void MapMotherboard_FallsBackToAllTemperatureSensors_WhenNoneAreVrmNamed()
    {
        // This board's Nuvoton NCT6701D reports 6 real temperature headers as plain
        // "Temperature #1".."Temperature #6" — nothing in the name says VRM/MOS/chipset. The old
        // filter matched none of them and the UI claimed "no VRM temperature sensors reported",
        // which was false: real board data existed, just under a generic name.
        var superIo = new FakeHardware(HardwareType.SuperIO, "Nuvoton NCT6701D", sensors:
        [
            new FakeSensor(SensorType.Temperature, "Temperature #1", 38f),
            new FakeSensor(SensorType.Temperature, "Temperature #2", 41f),
        ]);
        var motherboard = new FakeHardware(HardwareType.Motherboard, "ASUS PRIME B650EM-A WIFI", subHardware: [superIo]);

        var info = HardwareSnapshotMapper.MapMotherboard(motherboard);

        Assert.Equal(2, info.VrmTemperatures.Count);
        Assert.Contains(info.VrmTemperatures, v => v.Name == "Temperature #1" && v.Value == 38f);
    }

    [Fact]
    public void MapMotherboard_PrefersNamedVrmSensors_WhenTheyExist()
    {
        var superIo = new FakeHardware(HardwareType.SuperIO, "Some Other Chip", sensors:
        [
            new FakeSensor(SensorType.Temperature, "VRM MOS", 52f),
            new FakeSensor(SensorType.Temperature, "Chipset", 45f),
        ]);
        var motherboard = new FakeHardware(HardwareType.Motherboard, "Some Board", subHardware: [superIo]);

        var info = HardwareSnapshotMapper.MapMotherboard(motherboard);

        var vrm = Assert.Single(info.VrmTemperatures);
        Assert.Equal("VRM MOS", vrm.Name);
    }

    [Fact]
    public void MapMotherboard_Voltages_AreSortedByNameForStableDisplayOrder()
    {
        var superIo = new FakeHardware(HardwareType.SuperIO, "Nuvoton NCT6701D", sensors:
        [
            new FakeSensor(SensorType.Voltage, "Voltage #5", 3.3f),
            new FakeSensor(SensorType.Voltage, "AVCC", 3.344f),
            new FakeSensor(SensorType.Voltage, "Vcore", 1.376f),
        ]);
        var motherboard = new FakeHardware(HardwareType.Motherboard, "ASUS PRIME B650EM-A WIFI", subHardware: [superIo]);

        var info = HardwareSnapshotMapper.MapMotherboard(motherboard);

        Assert.Equal(["AVCC", "Vcore", "Voltage #5"], info.Voltages.Select(v => v.Name));
    }

    [Fact]
    public void MapCpu_NullHardware_ReturnsEmptyInfoInsteadOfThrowing()
    {
        var info = HardwareSnapshotMapper.MapCpu(null);

        Assert.Equal(0f, info.ClockSpeedMhz);
        Assert.Empty(info.Cores);
    }
}
