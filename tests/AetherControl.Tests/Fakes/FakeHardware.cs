using System.Collections.Generic;
using LibreHardwareMonitor.Hardware;

namespace AetherControl.Tests.Fakes;

/// <summary>
/// Minimal <see cref="ISensor"/>/<see cref="IHardware"/> stand-ins for pinning
/// <see cref="AetherControl.Services.Hardware.HardwareSnapshotMapper"/> against real captured sensor
/// dumps without a live board. Only the members the mapper actually reads (<see cref="ISensor.SensorType"/>,
/// <see cref="ISensor.Name"/>, <see cref="ISensor.Value"/>, <see cref="IHardware.Sensors"/>,
/// <see cref="IHardware.SubHardware"/>, <see cref="IHardware.HardwareType"/>, <see cref="IHardware.Name"/>)
/// carry real behaviour — everything else on the interfaces is present only to satisfy the compiler.
/// </summary>
internal sealed class FakeSensor : ISensor
{
    public FakeSensor(SensorType sensorType, string name, float? value)
    {
        SensorType = sensorType;
        Name = name;
        Value = value;
    }

    public SensorType SensorType { get; }
    public string Name { get; set; }
    public float? Value { get; }

    public IControl? Control => null;
    public IHardware Hardware => null!;
    public Identifier Identifier => new("sensor", Name);
    public int Index => 0;
    public bool IsDefaultHidden => false;
    public float? Max => null;
    public float? Min => null;
    public IReadOnlyList<IParameter> Parameters => System.Array.Empty<IParameter>();
    public IEnumerable<SensorValue> Values => System.Array.Empty<SensorValue>();
    public TimeSpan ValuesTimeWindow { get; set; }

    public void Accept(IVisitor visitor) { }
    public void Traverse(IVisitor visitor) { }
    public void ResetMin() { }
    public void ResetMax() { }
    public void ClearValues() { }
}

internal sealed class FakeHardware : IHardware
{
    public FakeHardware(HardwareType hardwareType, string name, IEnumerable<ISensor>? sensors = null, IEnumerable<IHardware>? subHardware = null)
    {
        HardwareType = hardwareType;
        Name = name;
        Sensors = sensors is null ? [] : [.. sensors];
        SubHardware = subHardware is null ? [] : [.. subHardware];
    }

    public HardwareType HardwareType { get; }
    public Identifier Identifier => new("hardware", Name);
    public string Name { get; set; }
    public IHardware? Parent => null;
    public ISensor[] Sensors { get; }
    public IHardware[] SubHardware { get; }
    public IDictionary<string, string> Properties => new Dictionary<string, string>();

    public string GetReport() => string.Empty;
    public void Update() { }
    public void Accept(IVisitor visitor) { }
    public void Traverse(IVisitor visitor) { }

    public event SensorEventHandler? SensorAdded { add { } remove { } }
    public event SensorEventHandler? SensorRemoved { add { } remove { } }
}
