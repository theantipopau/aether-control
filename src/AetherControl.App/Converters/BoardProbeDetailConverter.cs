using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>
/// Detail line for a Super I/O board temperature. Unconnected thermistor inputs on the NCT6701D
/// read values like 3 °C (seen live on the test rig: "Temperature #3/#4: 3 °C") — below any real
/// in-case temperature. Label them instead of presenting them as measurements; the value itself
/// is still shown, nothing is filtered.
/// </summary>
public sealed class BoardProbeDetailConverter : IValueConverter
{
    public const double MinPlausibleC = 10, MaxPlausibleC = 125;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is double c && (c < MinPlausibleC || c > MaxPlausibleC)
            ? "Implausible — probe likely unconnected"
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
