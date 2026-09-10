namespace AetherControl.Plugins.Abstractions;

/// <summary>
/// Root contract every Aether Control module (built-in or third-party) implements.
/// A plugin assembly is discovered by <see cref="IPluginManager"/>, given a
/// scoped <see cref="IPluginContext"/>, and may optionally implement any of the
/// capability interfaces in this namespace (<see cref="IDashboardWidgetProvider"/>,
/// <see cref="ITrayMetricProvider"/>, <see cref="IOptimisationTaskProvider"/>) to
/// surface itself in the corresponding part of the shell.
/// </summary>
public interface IAetherPlugin
{
    PluginMetadata Metadata { get; }

    Task InitializeAsync(IPluginContext context, CancellationToken ct = default);

    Task ShutdownAsync(CancellationToken ct = default);
}

public interface IPluginContext
{
    string PluginDataDirectory { get; }

    IServiceProvider HostServices { get; }

    void Log(string message);
}
