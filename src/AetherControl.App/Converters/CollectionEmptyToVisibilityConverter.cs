using System.Collections;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>
/// Shows an element only when the bound collection is empty — pairs with a
/// "no data yet" placeholder next to an <c>ItemsControl</c> that would
/// otherwise just render nothing when a sensor group isn't available (e.g.
/// motherboard fan RPM on a board where a vendor service is holding the
/// Super I/O ports — see FanRpmProbeService). Pass ConverterParameter
/// "Invert" to get the opposite (visible only when non-empty).
/// </summary>
public sealed class CollectionEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var isEmpty = value is not ICollection { Count: > 0 };
        var invert = string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase);
        var show = invert ? !isEmpty : isEmpty;
        return show ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
