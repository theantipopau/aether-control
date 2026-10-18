using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>
/// The single mapping from <see cref="MetricTone"/> to actual brushes/colours — every control that
/// paints a severity tone (MetricCard, RadialGauge) reads through here instead of carrying its own
/// switch with its own literals. All lookups hit <see cref="Application.Current"/> resources live,
/// so a mid-session accent change propagates to Cool-toned elements without a restart, and Warm/Hot
/// stay on the fixed severity tokens whatever accent is chosen (a hot reading must read as hot).
/// </summary>
internal static class ToneBrushes
{
    /// <summary>Solid brush for stripes, meters, ticks and text at this tone.</summary>
    public static Brush BrushFor(MetricTone tone) => tone switch
    {
        MetricTone.Warm => GetBrush("StatusWarmBrush"),
        MetricTone.Hot => GetBrush("StatusHotBrush"),
        MetricTone.Cool => GetBrush("AetherAccentBrush"),
        _ => GetBrush("BorderStandardBrush")
    };

    /// <summary>Base + light gradient ends for gauge arcs at this tone. Cool/Neutral derive the
    /// light end from AccentPalette's own 50%-lightened step (same sheen as before, now derived);
    /// Warm/Hot use the fixed light-end tokens in Colors.xaml (hand-tuned pairs, not a uniform
    /// lighten ratio — kept byte-identical rather than re-derived).</summary>
    public static (Color Base, Color Light) GradientEndsFor(MetricTone tone) => tone switch
    {
        MetricTone.Warm => (GetColor("StatusWarmColor"), GetColor("StatusWarmLightColor")),
        MetricTone.Hot => (GetColor("StatusHotColor"), GetColor("StatusHotLightColor")),
        _ => (GetColor("AetherAccentColor"), GetColor("SystemAccentColorLight2"))
    };

    private static Brush GetBrush(string key) => (Brush)Application.Current.Resources[key];

    private static Color GetColor(string key) => (Color)Application.Current.Resources[key];
}
