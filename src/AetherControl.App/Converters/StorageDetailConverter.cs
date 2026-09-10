using AetherControl.Core.Models;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Formats a drive's health + temperature into MetricCard's single Detail line (e.g. "Good · 37°C").</summary>
public sealed class StorageDetailConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is StorageDriveInfo drive ? $"{drive.Health} · {drive.TemperatureCelsius:F0}°C" : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
