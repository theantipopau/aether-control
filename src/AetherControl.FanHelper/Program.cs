// Standalone fan-RPM probe, launched as a fresh child process by AetherControl.Services on a
// short cadence (a few seconds) rather than read continuously from the main app's long-lived
// LibreHardwareMonitor Computer instance.
//
// Why a separate process: on real ASUS boards (e.g. PRIME B650EM-A WIFI / Nuvoton NCT6701D),
// AsusFanControlService reclaims the Super I/O LPC ports immediately after LibreHardwareMonitor's
// first read, so a long-lived Computer instance's fan RPM sticks at 0 after the first poll. A
// fresh Computer per poll avoids this — and since LibreHardwareMonitorLib's Ring0/MSR driver
// access is global per-process, running that fresh instance in a genuinely separate OS process
// means closing it can never corrupt the main app's own CPU/GPU sensor state.
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
