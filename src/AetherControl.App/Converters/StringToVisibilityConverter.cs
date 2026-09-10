using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>Collapses an element when the bound string is null/empty — e.g. hiding a vendor link when no URL is known.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
