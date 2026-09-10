namespace AetherControl.Plugins.Abstractions;

[Flags]
public enum PluginCapabilities
{
    None = 0,
    DashboardWidget = 1 << 0,
    PortraitWidget = 1 << 1,
    TrayMetric = 1 << 2,
    OptimisationTask = 1 << 3,
    BackgroundService = 1 << 4
}

public sealed class PluginMetadata
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required Version Version { get; init; }
    public string Author { get; init; } = "Unknown";
    public string Description { get; init; } = string.Empty;
    public PluginCapabilities Capabilities { get; init; } = PluginCapabilities.None;
}
