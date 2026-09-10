using AetherControl.Core.Enums;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Windows.Foundation;

namespace AetherControl.App.ViewModels;

public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly IHistoryService _historyService;

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
    }

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
