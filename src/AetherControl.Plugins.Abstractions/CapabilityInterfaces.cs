namespace AetherControl.Plugins.Abstractions;

public enum WidgetVisualKind
{
    NumericGauge,
    LineGraph,
    Text,
    Toggle
}

public sealed class WidgetDescriptor
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public WidgetVisualKind Kind { get; init; } = WidgetVisualKind.NumericGauge;
    public string Unit { get; init; } = string.Empty;
    public required Func<double> ReadValue { get; init; }
}

/// <summary>Implemented by a plugin that contributes one or more live widgets to the dashboard or portrait mode.</summary>
public interface IDashboardWidgetProvider
{
    IReadOnlyList<WidgetDescriptor> GetDashboardWidgets();
}

/// <summary>Implemented by a plugin that contributes a metric eligible for the system-tray live readout.</summary>
public interface ITrayMetricProvider
{
    IReadOnlyList<WidgetDescriptor> GetTrayMetrics();
}

/// <summary>Implemented by a plugin that contributes a task to the Windows Optimisation Centre.</summary>
public interface IOptimisationTaskProvider
{
    string TaskId { get; }

    string TaskTitle { get; }

    Task<bool> ExecuteAsync(CancellationToken ct = default);
}

public interface IPluginManager
{
    IReadOnlyList<IAetherPlugin> LoadedPlugins { get; }

    Task LoadPluginsAsync(string pluginsDirectory, CancellationToken ct = default);

    Task UnloadAllAsync();
}
