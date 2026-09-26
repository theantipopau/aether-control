namespace AetherControl.Core.Health;

public enum HealthLevel
{
    Normal,
    Warning,
    Critical,
    /// <summary>No usable readings — say so rather than claim "Normal".</summary>
    Unknown
}

public sealed record HealthSummary(HealthLevel Level, string Title, string Detail);

/// <summary>
/// The Overview hero's one-line verdict. Pure and deliberately conservative: it only speaks to what
/// was actually measured (CPU/GPU temperature, RAM pressure, Super I/O freshness) and never claims
/// more than that — "within normal range", not "your PC is healthy". Thresholds match Portrait
/// Mode's severity colours (75/88 °C, 80/95 %) so every surface in the app agrees.
/// </summary>
public static class SystemHealth
{
    public const double TempWarn = 75, TempCritical = 88, RamWarn = 80, RamCritical = 95;

    public static HealthSummary Evaluate(double cpuTempC, double gpuTempC, double ramPercent, bool motherboardStale)
    {
        var hasCpu = cpuTempC > 0;
        var hasGpu = gpuTempC > 0;
        if (!hasCpu && !hasGpu)
        {
            return new HealthSummary(HealthLevel.Unknown, "Waiting for sensors",
                "No CPU or GPU temperature reported yet. Aether needs administrator rights to read most sensors.");
        }

        var issues = new List<(HealthLevel Level, string Text)>();
        Rate(issues, hasCpu, cpuTempC, TempWarn, TempCritical, v => $"CPU at {v:F0} °C");
        Rate(issues, hasGpu, gpuTempC, TempWarn, TempCritical, v => $"GPU at {v:F0} °C");
        Rate(issues, ramPercent > 0, ramPercent, RamWarn, RamCritical, v => $"memory {v:F0}% used");

        var level = issues.Count == 0 ? HealthLevel.Normal : issues.Max(i => i.Level);
        var title = level switch
        {
            HealthLevel.Critical => "Running hot",
            HealthLevel.Warning => "Under load",
            // A clean verdict over stale board data would read as fully healthy; say which half is known.
            _ => motherboardStale ? "Normal · board sensors stale" : "All readings normal"
        };

        var detail = issues.Count == 0
            ? "Temperatures and memory are within normal range."
            : string.Join(" · ", issues.OrderByDescending(i => i.Level).Select(i => Capitalise(i.Text)));

        if (motherboardStale)
        {
            detail += " Motherboard sensors are stale — see Diagnostics.";
        }

        return new HealthSummary(level, title, detail);
    }

    private static void Rate(List<(HealthLevel, string)> issues, bool present, double value, double warn, double critical, Func<double, string> describe)
    {
        if (!present || value < warn)
        {
            return;
        }

        issues.Add((value >= critical ? HealthLevel.Critical : HealthLevel.Warning, describe(value)));
    }

    private static string Capitalise(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}
