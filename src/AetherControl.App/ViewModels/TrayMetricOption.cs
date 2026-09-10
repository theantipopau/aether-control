using AetherControl.Core.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

/// <summary>One selectable row in Settings' tray-icon metric list. <see cref="AetherControl.Services.Tray.TrayReadoutFormatter"/>
/// only knows how to render these seven <see cref="MetricKind"/> values, so that's the list offered here
/// rather than the full enum — an enabled checkbox for a metric the formatter silently drops would look broken.</summary>
public sealed partial class TrayMetricOption : ObservableObject
{
    [ObservableProperty] private bool isEnabled;

    public TrayMetricOption(MetricKind metric, string displayName, bool isEnabled)
    {
        Metric = metric;
        DisplayName = displayName;
        IsEnabled = isEnabled;
    }

    public MetricKind Metric { get; }
    public string DisplayName { get; }
}
