using AetherControl.Core.Health;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace AetherControl.App.Converters;

/// <summary>HealthLevel → one of the shared Status* brushes from Colors.xaml (looked up, never
/// allocated, so a per-poll binding update costs nothing).</summary>
public sealed class HealthLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var key = value is HealthLevel level
            ? level switch
            {
                HealthLevel.Critical => "StatusCriticalBrush",
                HealthLevel.Warning => "StatusWarningBrush",
                HealthLevel.Normal => "StatusNormalBrush",
                _ => "StatusUnsupportedBrush"
            }
            : "StatusUnsupportedBrush";
        return Application.Current.Resources[key];
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>HealthLevel → Segoe Fluent glyph, so the state is never carried by colour alone.</summary>
public sealed class HealthLevelToGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is HealthLevel level
            ? level switch
            {
                HealthLevel.Critical or HealthLevel.Warning => "", // warning triangle
                HealthLevel.Normal => "",                          // check mark
                _ => ""                                           // unknown
            }
            : "";

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
