using AetherControl.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Formats a drive's health + temperature into MetricCard's single Detail line (e.g. "Good · 37°C"
/// or "Good · 37°C · stale" when this poll couldn't get a fresh free-space reading — see
/// StorageDriveInfo.IsFreeSpaceStale). The free-space number itself is never hidden or replaced when
/// stale, only labelled, so a real reading doesn't get papered over by a "loading" state.</summary>
public sealed class StorageDetailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is StorageDriveInfo drive
            ? $"{drive.Health} · {drive.TemperatureCelsius:F0}°C{(drive.IsFreeSpaceStale ? " · stale" : string.Empty)}"
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
