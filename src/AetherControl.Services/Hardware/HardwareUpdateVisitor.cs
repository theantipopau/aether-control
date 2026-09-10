using LibreHardwareMonitor.Hardware;

namespace AetherControl.Services.Hardware;

/// <summary>
/// LibreHardwareMonitor requires an <see cref="IVisitor"/> to recursively
/// update hardware and sub-hardware (e.g. Super I/O chips beneath the
/// motherboard) on every polling tick.
/// </summary>
internal sealed class HardwareUpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (var subHardware in hardware.SubHardware)
        {
            subHardware.Accept(this);
        }
    }

    public void VisitSensor(ISensor sensor)
    {
    }

    public void VisitParameter(IParameter parameter)
    {
    }
}
