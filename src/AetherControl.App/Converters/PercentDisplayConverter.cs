using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

public sealed class PercentDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:F0}" : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
