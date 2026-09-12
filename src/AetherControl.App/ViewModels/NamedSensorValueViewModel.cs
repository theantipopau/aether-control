using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Long-lived, per-sensor presentation state for a single named motherboard sensor (a voltage rail,
/// a fan header, a VRM temperature) — same rationale as <see cref="StorageDriveViewModel"/>: these
/// are real, continuously-fluctuating readings (voltage ripple, fan RPM jitter) carrying a stable
/// identity (the sensor's name), and were exposed to the identical "recreated every poll" risk once
/// <c>NamedSensorValue</c> became a record with byte/float-exact equality. Created once per sensor
/// name, updated in place via <see cref="Apply"/>, never replaced.
/// </summary>
public sealed partial class NamedSensorValueViewModel : ObservableObject
{
    public string Name { get; }

    [ObservableProperty] private double value;
    [ObservableProperty] private string unit = string.Empty;

    public NamedSensorValueViewModel(NamedSensorValue snapshot)
    {
        Name = snapshot.Name;
        Apply(snapshot);
    }

    public void Apply(NamedSensorValue snapshot)
    {
        Value = snapshot.Value;
        Unit = snapshot.Unit;
    }
}
