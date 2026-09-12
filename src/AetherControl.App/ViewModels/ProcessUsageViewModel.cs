using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Long-lived, per-process presentation state for one row in a Top Processes list — same rationale
/// as <see cref="StorageDriveViewModel"/>. A process that stays in the top ranking across several
/// polls (the case that actually matters — e.g. a game or build genuinely using sustained CPU) has a
/// CpuPercent/GpuPercent that legitimately fluctuates by fractions of a percent almost every poll;
/// created once per Pid, updated in place via <see cref="Apply"/>, never replaced while it remains
/// in the ranking.
/// </summary>
public sealed partial class ProcessUsageViewModel : ObservableObject
{
    public int Pid { get; }

    [ObservableProperty] private string name = string.Empty;
    [ObservableProperty] private double cpuPercent;
    [ObservableProperty] private double gpuPercent;

    public ProcessUsageViewModel(ProcessUsageInfo snapshot)
    {
        Pid = snapshot.Pid;
        Apply(snapshot);
    }

    public void Apply(ProcessUsageInfo snapshot)
    {
        Name = snapshot.Name;
        CpuPercent = snapshot.CpuPercent;
        GpuPercent = snapshot.GpuPercent;
    }
}
