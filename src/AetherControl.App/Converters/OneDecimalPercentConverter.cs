using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

public sealed class OneDecimalPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:F1}%" : value?.ToString() ?? string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
