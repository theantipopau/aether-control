using AetherControl.App.Controls;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>
/// Maps a raw sensor value to a <see cref="MetricTone"/> using the same
/// thresholds as Radium PCs Companion's <c>severity.ts</c>: temperatures run
/// warmer before they're actionable than usage percentages do, so each gets
/// its own threshold set. ConverterParameter selects which set: "temperature" or "usage".
/// </summary>
public sealed class SeverityToneConverter : IValueConverter
{
    private const double TemperatureWarm = 70;
    private const double TemperatureHot = 85;
    private const double UsageWarm = 65;
    private const double UsageHot = 85;

    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not double numericValue)
        {
            return MetricTone.Neutral;
        }

        var (warm, hot) = string.Equals(parameter as string, "usage", StringComparison.OrdinalIgnoreCase)
            ? (UsageWarm, UsageHot)
            : (TemperatureWarm, TemperatureHot);

        if (numericValue >= hot)
        {
            return MetricTone.Hot;
        }

        return numericValue >= warm ? MetricTone.Warm : MetricTone.Cool;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
