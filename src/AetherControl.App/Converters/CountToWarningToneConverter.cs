using AetherControl.App.Controls;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Warm tone when a count is above zero, Cool otherwise — for summary tiles like
/// "high-impact startup entries" where any non-zero count deserves attention.</summary>
public sealed class CountToWarningToneConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is int count && count > 0 ? MetricTone.Warm : MetricTone.Cool;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
