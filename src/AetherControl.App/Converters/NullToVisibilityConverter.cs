using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Visible when the bound value is non-null (e.g. showing a "last result" panel only after a task has actually run).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
