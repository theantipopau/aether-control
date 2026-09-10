using AetherControl.Plugins.Abstractions;

namespace AetherControl.Plugins.Sample;

/// <summary>
/// Reference implementation of the plugin contract: a single dashboard
/// widget showing system uptime in hours, sourced from
/// <see cref="Environment.TickCount64"/> — no extra services, no external
/// dependencies, just the minimum needed to demonstrate the pattern that
/// future modules (Fan Control, Benchmarking, NAS monitoring, ...) would follow.
/// </summary>
public sealed class UptimeWidgetPlugin : IAetherPlugin, IDashboardWidgetProvider
{
    public PluginMetadata Metadata { get; } = new()
    {
        Id = "aethercontrol.sample.uptime",
        Name = "System Uptime",
        Version = new Version(1, 0, 0),
        Author = "Aether Control",
        Description = "Shows system uptime as a dashboard widget.",
        Capabilities = PluginCapabilities.DashboardWidget
    };

    public Task InitializeAsync(IPluginContext context, CancellationToken ct = default)
    {
        context.Log("Uptime widget plugin initialised.");
        return Task.CompletedTask;
    }

    public Task ShutdownAsync(CancellationToken ct = default) => Task.CompletedTask;

    public IReadOnlyList<WidgetDescriptor> GetDashboardWidgets() =>
    [
        new()
        {
            Id = "uptime-hours",
            Title = "System Uptime",
            Kind = WidgetVisualKind.NumericGauge,
            Unit = "hrs",
            ReadValue = () => TimeSpan.FromMilliseconds(Environment.TickCount64).TotalHours
        }
    ];
}
