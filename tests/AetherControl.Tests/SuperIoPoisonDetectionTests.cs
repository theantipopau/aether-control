using AetherControl.Services.Hardware;

namespace AetherControl.Tests;

/// <summary>
/// Regression coverage for <see cref="HardwareMonitorService.LooksPoisoned"/> — added after a live
/// capture (2026-09-23) proved this board's Nuvoton Super I/O chip sometimes returns a "poisoned"
/// read (every voltage rail collapsed into one of two identical values, e.g. Vcore = AVCC = CMOS
/// Battery = 2.04V or 4.08V) that never self-corrects without a full Computer.Close()/Open() —
/// confirmed to happen both at startup and mid-session, with real vendor software (Armoury Crate,
/// iCUE) on this machine re-triggering it on an ~11 second periodic cadence even with nothing of
/// Aether's own touching the chip. The values below are the exact ones captured live, not invented.
/// </summary>
public class SuperIoPoisonDetectionTests
{
    // Captured 2026-09-23 while Aether Control was itself poisoned (ASUS PRIME B650EM-A WIFI,
    // Nuvoton NCT6701D): Vcore/Voltage#2/#5/#6/#7/CPU Termination/#11-15 all read exactly 2.04V;
    // AVCC/+3.3V/+3V Standby/CMOS Battery all read exactly 4.08V. Two distinct values across 15 sensors.
    private static readonly double[] PoisonedReading =
    [
        2.0400002, 2.0400002, 4.0800004, 4.0800004, 2.0400002, 2.0400002, 2.0400002,
        4.0800004, 4.0800004, 2.0400002, 2.0400002, 2.0400002, 2.0400002, 2.0400002
    ];

    // Captured 2026-09-23 on the same board reading correctly: Vcore 1.376V, AVCC 3.344V, and 8
    // genuinely distinct rails among the same 15 sensors.
    private static readonly double[] HealthyReading =
    [
        1.376, 1.03, 3.344, 3.344, 1.0, 1.03, 0.99,
        3.392, 3.392, 1.664, 0.7, 0.56, 1.02, 0.99, 1.02
    ];

    [Fact]
    public void PoisonedReading_IsDetected()
    {
        var (poisoned, distinctCount, sensorCount) = HardwareMonitorService.LooksPoisoned(PoisonedReading);

        Assert.True(poisoned);
        Assert.Equal(2, distinctCount);
        Assert.Equal(14, sensorCount);
    }

    [Fact]
    public void HealthyReading_IsNotFlagged()
    {
        var (poisoned, distinctCount, _) = HardwareMonitorService.LooksPoisoned(HealthyReading);

        Assert.False(poisoned);
        Assert.True(distinctCount >= 4);
    }

    [Fact]
    public void FewSensors_NeverFlagged_NothingToDistinguishFrom()
    {
        // A board with only 2-3 voltage sensors to begin with legitimately might report only 1-2
        // distinct values (e.g. Vcore and a single 3.3V rail) — that's not evidence of poisoning,
        // there just isn't enough data to tell the difference. The >= 4 sensor gate exists for this.
        var (poisoned, _, _) = HardwareMonitorService.LooksPoisoned([1.2, 3.3]);

        Assert.False(poisoned);
    }

    [Fact]
    public void EmptyReading_NeverFlagged()
    {
        var (poisoned, distinctCount, sensorCount) = HardwareMonitorService.LooksPoisoned([]);

        Assert.False(poisoned);
        Assert.Equal(0, distinctCount);
        Assert.Equal(0, sensorCount);
    }

    [Fact]
    public void ExactlyFourDistinctValues_IsTheHealthyBoundary()
    {
        // Four sensors landing on four genuinely different values should read as healthy even though
        // it's the minimum — real per-rail variety, not the collapsed-to-1-or-2 poisoned signature.
        var (poisoned, distinctCount, _) = HardwareMonitorService.LooksPoisoned([1.0, 1.1, 1.2, 1.3]);

        Assert.False(poisoned);
        Assert.Equal(4, distinctCount);
    }

    [Fact]
    public void FourSensorsThreeDistinctValues_IsPoisoned()
    {
        // Just below the healthy boundary: 4+ sensors but too few distinct values among them.
        var (poisoned, distinctCount, sensorCount) = HardwareMonitorService.LooksPoisoned([1.0, 1.0, 1.1, 1.2]);

        Assert.True(poisoned);
        Assert.Equal(3, distinctCount);
        Assert.Equal(4, sensorCount);
    }
}
