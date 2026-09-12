using AetherControl.App.Controls;
using AetherControl.Core.Enums;
using AetherControl.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Long-lived, per-device presentation state for one physical disk — created once when a DeviceId is
/// first seen (see <see cref="AetherControl.Core.Collections.LiveCollectionSync{TViewModel,TSnapshot,TKey}"/>)
/// and updated in place on every poll thereafter via <see cref="Apply"/>, never replaced. Each
/// property is a normal <c>[ObservableProperty]</c> — CommunityToolkit's generated setter already
/// compares old/new with <c>EqualityComparer&lt;T&gt;.Default</c> and only raises
/// <c>PropertyChanged</c> when a field actually changed, which is exactly "raise PropertyChanged only
/// for properties that materially changed" without needing whole-object equality at all. Separating
/// this from <see cref="StorageDriveInfo"/> (which stays the immutable, byte-precise service-layer
/// snapshot — unchanged, still what <c>StorageHealthProbe</c> produces every poll) is what lets a
/// storage card's identity and its telemetry vary independently: the card and its
/// <c>NumberTween</c> live exactly as long as this view model does, regardless of how often
/// individual fields update.
/// </summary>
public sealed partial class StorageDriveViewModel : ObservableObject
{
    public string DeviceId { get; }

    [ObservableProperty] private string model = string.Empty;
    [ObservableProperty] private DriveHealthStatus health = DriveHealthStatus.Unknown;
    [ObservableProperty] private double capacityBytes;
    [ObservableProperty] private double freeBytes;
    [ObservableProperty] private double temperatureCelsius;
    [ObservableProperty] private bool isNvme;
    [ObservableProperty] private bool isFreeSpaceStale;

    public double CapacityGb => CapacityBytes / 1024 / 1024 / 1024;
    public double FreeGb => FreeBytes / 1024 / 1024 / 1024;

    // Same clamp as StorageDriveInfo.UsedPercent, preserved deliberately — FreeBytes and
    // CapacityBytes are independent readings and can transiently disagree.
    public double UsedPercent => CapacityBytes <= 0 ? 0 : Math.Clamp((CapacityBytes - FreeBytes) / CapacityBytes * 100.0, 0.0, 100.0);

    // A computed property combining three fields, not a converter over the whole object — a classic
    // {Binding Converter=...} with an empty path only re-evaluates when the DataContext reference
    // itself is swapped, not when ObservableObject raises PropertyChanged for one of several
    // properties a converter reads. Refreshed explicitly below whenever any of its three inputs change.
    public string DetailText => $"{Health} · {TemperatureCelsius:F0}°C{(IsFreeSpaceStale ? " · stale" : string.Empty)}";

    public StorageDriveViewModel(StorageDriveInfo snapshot)
    {
        DeviceId = snapshot.DeviceId;
        Apply(snapshot);
    }

    // Raw bytes stay raw here — CapacityGb/FreeGb/UsedPercent are the presentation-layer formatting,
    // computed on demand from whatever's currently in CapacityBytes/FreeBytes, never a second source
    // of truth. Refreshing their change notifications alongside the byte fields keeps a classic
    // {Binding FreeGb} (not INotifyPropertyChanged-aware on its own, since it's a computed property
    // with no backing field) updating correctly.
    partial void OnFreeBytesChanged(double value)
    {
        OnPropertyChanged(nameof(FreeGb));
        OnPropertyChanged(nameof(UsedPercent));
    }

    partial void OnCapacityBytesChanged(double value)
    {
        OnPropertyChanged(nameof(CapacityGb));
        OnPropertyChanged(nameof(UsedPercent));
    }

    partial void OnHealthChanged(DriveHealthStatus value) => OnPropertyChanged(nameof(DetailText));
    partial void OnTemperatureCelsiusChanged(double value) => OnPropertyChanged(nameof(DetailText));
    partial void OnIsFreeSpaceStaleChanged(bool value) => OnPropertyChanged(nameof(DetailText));

    public void Apply(StorageDriveInfo snapshot)
    {
        if (StorageDiagnostics.TraceEnabled)
        {
            TraceFieldChanges(snapshot);
        }

        Model = snapshot.Model;
        Health = snapshot.Health;
        CapacityBytes = snapshot.CapacityBytes;
        FreeBytes = snapshot.FreeBytes;
        TemperatureCelsius = snapshot.TemperatureCelsius;
        IsNvme = snapshot.IsNvme;
        IsFreeSpaceStale = snapshot.IsFreeSpaceStale;
    }

    /// <summary>Answers, per poll, exactly which field(s) changed and by how much — added to settle
    /// "which field causes each replacement" with evidence rather than another guess. There is no
    /// more "replacement" once a device has its own view model (this method never touches the
    /// collection), but knowing which fields are naturally volatile is still useful for judging
    /// whether a future property deserves its own change threshold.</summary>
    private void TraceFieldChanges(StorageDriveInfo snapshot)
    {
        var changes = new List<string>();
        if (Model != snapshot.Model) changes.Add($"Model {Model}->{snapshot.Model}");
        if (Health != snapshot.Health) changes.Add($"Health {Health}->{snapshot.Health}");
        if (CapacityBytes != snapshot.CapacityBytes) changes.Add($"CapacityBytes {CapacityBytes:R}->{snapshot.CapacityBytes:R}");
        if (FreeBytes != snapshot.FreeBytes) changes.Add($"FreeBytes {FreeBytes:R}->{snapshot.FreeBytes:R} (ΔGB={((snapshot.FreeBytes - FreeBytes) / 1024 / 1024 / 1024):F6})");
        if (TemperatureCelsius != snapshot.TemperatureCelsius) changes.Add($"TemperatureCelsius {TemperatureCelsius:R}->{snapshot.TemperatureCelsius:R}");
        if (IsNvme != snapshot.IsNvme) changes.Add($"IsNvme {IsNvme}->{snapshot.IsNvme}");
        if (IsFreeSpaceStale != snapshot.IsFreeSpaceStale) changes.Add($"IsFreeSpaceStale {IsFreeSpaceStale}->{snapshot.IsFreeSpaceStale}");

        UiValueTraceLog.Write(DeviceId, Guid.Empty,
            changes.Count == 0 ? "Apply: no field changed" : $"Apply: {string.Join("; ", changes)}");
    }
}
