using AetherControl.Core.Models;

namespace AetherControl.Core.Events;

public sealed class SensorsUpdatedEventArgs(HardwareSnapshot snapshot) : EventArgs
{
    public HardwareSnapshot Snapshot { get; } = snapshot;
}
