using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

/// <summary>Adds per-row UI selection state to <see cref="CleanupLocationPreview"/> — the Core
/// model stays a plain data record since checkbox state is presentation-only.</summary>
public sealed partial class CleanupLocationEntry : ObservableObject
{
    [ObservableProperty] private bool isSelected = true;

    public CleanupLocationEntry(CleanupLocationPreview preview)
    {
        Name = preview.Name;
        Path = preview.Path;
        SizeBytes = preview.SizeBytes;
        SafeToDeleteWhileRunning = preview.SafeToDeleteWhileRunning;
    }

    public string Name { get; }
    public string Path { get; }
    public long SizeBytes { get; }
    public bool SafeToDeleteWhileRunning { get; }

    public double SizeGb => SizeBytes / 1024.0 / 1024.0 / 1024.0;

    public string SizeDisplay => SizeGb >= 0.1
        ? $"{SizeGb:F2} GB"
        : $"{SizeBytes / 1024.0 / 1024.0:F1} MB";
}
