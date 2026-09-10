using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

public sealed class BoolToFanModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is true ? "Manual" : "Automatic";

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
