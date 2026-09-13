// Standalone fan-RPM probe, launched as a fresh child process by AetherControl.Services on a
// short cadence (a few seconds) rather than read continuously from the main app's long-lived
// LibreHardwareMonitor Computer instance.
//
// The out-of-process design predates a since-corrected diagnosis: an earlier version of this
// comment claimed AsusFanControlService reclaiming the Super I/O LPC ports was why fan RPM stuck
// at 0/absent. That was never actually true — a live A/B test (elevated fresh Computer instance,
// AsusFanControlService fully stopped vs. running) showed identical behaviour either way: on this
// board (PRIME B650EM-A WIFI / Nuvoton NCT6701D), LibreHardwareMonitorLib 0.9.4 didn't create a
// SuperIO sub-hardware node at all, elevated or not, service running or not. The real cause was a
// library gap — NCT6701D support landed after 0.9.4 — fixed by bumping to a newer
// LibreHardwareMonitorLib release (see AetherControl.FanHelper.csproj / AetherControl.Services.csproj).
// Kept as a separate process anyway: LibreHardwareMonitorLib's Ring0/MSR driver access is global
// per-process, so an out-of-process probe still guarantees a bad fan read can never corrupt the
// main app's own CPU/GPU sensor state, independent of the original (wrong) contention theory.
//
// Output: one "Name\tRpm" line per fan header currently reporting non-zero RPM. No output and
// exit code 0 both mean "no live fan data this poll" as far as the caller is concerned.

using LibreHardwareMonitor.Hardware;

try
{
    var computer = new Computer { IsMotherboardEnabled = true };
    computer.Open();
    computer.Accept(new UpdateVisitor());

    foreach (var hardware in computer.Hardware)
    {
        PrintFans(hardware);
    }

    computer.Close();
}
catch
{
    // Best-effort probe — any failure here just means this poll contributes no fan data.
}

return 0;

static void PrintFans(IHardware hardware)
{
    foreach (var sensor in hardware.Sensors)
    {
        if (sensor.SensorType == SensorType.Fan && sensor.Value is > 0)
        {
            Console.WriteLine($"{sensor.Name}\t{sensor.Value.Value:0.##}");
        }
    }

    foreach (var sub in hardware.SubHardware)
    {
        PrintFans(sub);
    }
}

internal sealed class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware)
        {
            sub.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor)
    {
    }

    public void VisitParameter(IParameter parameter)
    {
    }
}
