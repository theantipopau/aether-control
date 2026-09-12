using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using Windows.Foundation;

namespace AetherControl.App.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject, IDisposable
{
    // Live enough to feel like part of the dashboard rather than a one-shot report, without
    // hammering the history DB — a 30-day range barely moves visually within 30 seconds anyway.
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IHistoryService _historyService;
    private readonly DispatcherQueue _dispatcherQueue;
    private readonly Timer _autoRefreshTimer;

    [ObservableProperty] private MetricKind selectedMetric = MetricKind.CpuTemperature;
    [ObservableProperty] private HistoryResolution selectedResolution = HistoryResolution.Daily;
    [ObservableProperty] private IReadOnlyList<Point> chartPoints = [];
    [ObservableProperty] private string summary = "No data loaded yet.";

    public IReadOnlyList<MetricKind> AvailableMetrics { get; } =
    [
        MetricKind.CpuTemperature, MetricKind.CpuUtilisation, MetricKind.GpuTemperature,
        MetricKind.GpuUtilisation, MetricKind.RamUtilisationPercent, MetricKind.NetworkDownloadKbps
    ];

    public IReadOnlyList<HistoryResolution> AvailableResolutions { get; } =
    [
        HistoryResolution.Daily, HistoryResolution.Weekly, HistoryResolution.Monthly
    ];

    public HistoryViewModel(IHistoryService historyService)
    {
        _historyService = historyService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Previously required an explicit "Load" click for every single view of this page,
        // including the very first — picking a metric (or just opening the page with the defaults
        // already selected) now loads it immediately, and the auto-refresh timer keeps it current
        // without another click.
        _ = LoadCommand.ExecuteAsync(null);
        _autoRefreshTimer = new Timer(_ => _dispatcherQueue.TryEnqueue(() => _ = LoadCommand.ExecuteAsync(null)),
            null, AutoRefreshInterval, AutoRefreshInterval);
    }

    partial void OnSelectedMetricChanged(MetricKind value) => _ = LoadCommand.ExecuteAsync(null);

    partial void OnSelectedResolutionChanged(HistoryResolution value) => _ = LoadCommand.ExecuteAsync(null);

    public void Dispose() => _autoRefreshTimer.Dispose();

    [RelayCommand]
    private async Task LoadAsync()
    {
        var to = DateTimeOffset.UtcNow;
        var from = SelectedResolution switch
        {
            HistoryResolution.Daily => to.AddDays(-1),
            HistoryResolution.Weekly => to.AddDays(-7),
            _ => to.AddDays(-30)
        };

        var samples = await _historyService.QueryAsync(SelectedMetric, from, to, SelectedResolution);
        if (samples.Count == 0)
        {
            ChartPoints = [];
            Summary = "No samples in this time range yet.";
            return;
        }

        var min = samples.Min(s => s.Value);
        var max = samples.Max(s => s.Value);
        var range = Math.Max(max - min, 0.0001);

        ChartPoints = samples
            .Select((s, i) => new Point(
                samples.Count <= 1 ? 0 : i / (double)(samples.Count - 1),
                1 - (s.Value - min) / range))
            .ToList();

        Summary = $"Min {min:F1}  ·  Max {max:F1}  ·  Avg {samples.Average(s => s.Value):F1}  ·  {samples.Count} points";
    }
}
