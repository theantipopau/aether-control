namespace AetherControl.App.ViewModels;

public enum PortraitVendor
{
    None,
    Amd,
    Nvidia,
    Intel
}

public static class PortraitVendorDetector
{
    public static PortraitVendor FromName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return PortraitVendor.None;
        }

        if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase))
        {
            return PortraitVendor.Amd;
        }

        if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
        {
            return PortraitVendor.Nvidia;
        }

        if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            return PortraitVendor.Intel;
        }

        return PortraitVendor.None;
    }
}

public enum PortraitSeverity
{
    Normal,
    Warning,
    Critical
}

public static class PortraitSeverityThresholds
{
    public static PortraitSeverity ForTemp(double value, double warn = 75, double critical = 88) => Rate(value, warn, critical);

    public static PortraitSeverity ForPercent(double value, double warn = 80, double critical = 95) => Rate(value, warn, critical);

    private static PortraitSeverity Rate(double value, double warn, double critical)
    {
        if (value >= critical)
        {
            return PortraitSeverity.Critical;
        }

        if (value >= warn)
        {
            return PortraitSeverity.Warning;
        }

        return PortraitSeverity.Normal;
    }
}

/// <summary>Fixed-capacity FIFO sample buffer backing the sparkline controls — ported from
/// Portrait Stats' <c>History</c>.</summary>
public sealed class PortraitHistory(int capacity)
{
    private readonly LinkedList<double> _values = new();

    public void Add(double value)
    {
        _values.AddLast(value);
        if (_values.Count > capacity)
        {
            _values.RemoveFirst();
        }
    }

    public IReadOnlyList<double> Snapshot() => _values.ToArray();
}
