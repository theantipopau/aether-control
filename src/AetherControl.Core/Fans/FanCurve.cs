namespace AetherControl.Core.Fans;

/// <summary>One temperature → fan-percent knot on a curve. Percents are 0–100; temperatures °C.</summary>
public sealed record FanCurvePoint(double TemperatureC, int Percent);

/// <summary>
/// Pure fan-curve evaluator: piecewise-linear interpolation between ordered temperature knots
/// with endpoint clamping. No I/O, no timers, no hardware — the control *loop* (sampling a
/// temperature, deciding when to write, driving <c>IFanControlService</c>) lives App-side; this
/// only answers \"what percent does this curve want at this temperature\", which is what's
/// unit-testable without a motherboard. Mirrors FanControl's documented curve semantics
/// (interpolate between ordered points, clamp outside the endpoints) without copying its code.
/// </summary>
public sealed class FanCurve
{
    public string Name { get; }

    private readonly List<FanCurvePoint> _points;

    public IReadOnlyList<FanCurvePoint> Points => _points;

    public FanCurve(string name, IEnumerable<FanCurvePoint> points)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(points);

        _points = points.OrderBy(p => p.TemperatureC).ToList();
        if (_points.Count < 2)
        {
            throw new ArgumentException("A fan curve needs at least two points.", nameof(points));
        }

        for (var i = 0; i < _points.Count; i++)
        {
            if (_points[i].Percent is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(points),
                    $"Point {i} percent ({_points[i].Percent}) is outside 0–100.");
            }

            if (i > 0 && _points[i].TemperatureC <= _points[i - 1].TemperatureC)
            {
                throw new ArgumentException(
                    $"Point {i} temperature ({_points[i].TemperatureC}°C) must be above point {i - 1}'s ({_points[i - 1].TemperatureC}°C).",
                    nameof(points));
            }
        }

        Name = name;
    }

    /// <summary>Interpolated percent at <paramref name=\"temperatureC\"/>, rounded to whole percent.\n    /// Below the first knot returns the first knot's percent, above the last returns the last's —\n    /// a curve never extrapolates beyond what its endpoints state.</summary>
    public int Evaluate(double temperatureC)
    {
        if (temperatureC <= _points[0].TemperatureC)
        {
            return _points[0].Percent;
        }

        var last = _points[^1];
        if (temperatureC >= last.TemperatureC)
        {
            return last.Percent;
        }

        for (var i = 1; i < _points.Count; i++)
        {
            if (temperatureC <= _points[i].TemperatureC)
            {
                var lower = _points[i - 1];
                var upper = _points[i];
                var fraction = (temperatureC - lower.TemperatureC) / (upper.TemperatureC - lower.TemperatureC);
                return (int)Math.Round(lower.Percent + fraction * (upper.Percent - lower.Percent));
            }
        }

        return last.Percent; // unreachable given the clamps above — keeps the compiler honest
    }
}

/// <summary>
/// Built-in curves for the Fan Control presets. All floors sit at 20% — the same safety floor as
/// software fan writes elsewhere (never a full stop with no thermal watchdog). Tuned against CPU
/// package temperature; GPU-driven curves are a later addition, not a silent substitution.
/// </summary>
public static class FanCurvePresets
{
    public static FanCurve Silent { get; } = new("Silent",
    [
        new FanCurvePoint(30, 20),
        new FanCurvePoint(55, 20),
        new FanCurvePoint(70, 35),
        new FanCurvePoint(80, 60),
        new FanCurvePoint(90, 100)
    ]);

    public static FanCurve Balanced { get; } = new("Balanced",
    [
        new FanCurvePoint(30, 20),
        new FanCurvePoint(50, 30),
        new FanCurvePoint(65, 50),
        new FanCurvePoint(75, 75),
        new FanCurvePoint(85, 100)
    ]);

    public static FanCurve Performance { get; } = new("Performance",
    [
        new FanCurvePoint(30, 35),
        new FanCurvePoint(50, 55),
        new FanCurvePoint(65, 80),
        new FanCurvePoint(75, 100)
    ]);
}

/// <summary>
/// The anti-hunting gate for a future control loop: a temperature oscillating a fraction of a
/// degree around a knot boundary would otherwise flip the target by a point or two every poll and
/// chatter the Super I/O control. Only writes that move the channel by a meaningful step go
/// through; anything smaller waits for the temperature to actually go somewhere.
/// </summary>
public static class FanCurveApplication
{
    public const int MinPercentStep = 5;

    public static bool ShouldApply(int targetPercent, int lastAppliedPercent) =>
        Math.Abs(targetPercent - lastAppliedPercent) >= MinPercentStep;
}
