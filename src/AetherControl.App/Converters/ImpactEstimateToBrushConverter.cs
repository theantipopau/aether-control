using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Converters;

/// <summary>Colours a startup entry's impact badge the same way MetricCard colours its tone —
/// Low/Medium/High map to the app's existing Cool/Warm/Hot brushes, not a separate palette.</summary>
public sealed class ImpactEstimateToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var resources = Application.Current.Resources;
        return value as string switch
        {
            "High" => (Brush)resources["AetherHotBrush"],
            "Medium" => (Brush)resources["AetherWarmBrush"],
            "Low" => (Brush)resources["AetherAccentBrush"],
            _ => (Brush)resources["AetherTextSecondaryBrush"]
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
