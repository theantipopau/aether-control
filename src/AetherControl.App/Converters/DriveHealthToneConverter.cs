using AetherControl.App.Controls;
using AetherControl.Core.Enums;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Maps drive health to the same tone vocabulary as temperature/usage severity, so a failing drive reads as visually urgent the same way an overheating CPU does.</summary>
public sealed class DriveHealthToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value switch
    {
        DriveHealthStatus.Bad => MetricTone.Hot,
        DriveHealthStatus.Caution => MetricTone.Warm,
        DriveHealthStatus.Good => MetricTone.Neutral,
        _ => MetricTone.Neutral
    };

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
