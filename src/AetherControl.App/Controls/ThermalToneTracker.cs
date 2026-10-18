using AetherControl.App.ViewModels;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>
/// Decides WHEN the Thermal Aura retints — the control itself only paints. Upgrades apply the
/// moment the raw tone worsens (heat is news); downgrades wait until the temperature drops a full
/// hysteresis band below the boundary, so hovering at 75 °C doesn't strobe the backdrop every
/// poll. Same 5 °C band as <c>TemperatureAlertService</c>'s re-arm gap, applied to tone boundaries
/// instead of alert thresholds. Shared by both windows so the banding can't drift apart.
/// </summary>
public sealed class ThermalToneTracker
{
    // Mirrors PortraitSeverityThresholds.ForTemp's defaults (warn 75, critical 88) minus the 5 °C
    // band. If those defaults ever change, these floors must follow — there is no accessor for
    // the boundaries, only the rating itself.
    private const double WarnFloor = 70;
    private const double CriticalFloor = 83;

    private PortraitSeverity _current = PortraitSeverity.Normal;

    public PortraitSeverity Current => _current;

    /// <summary>Glow colour for a tone: the accent while cool, amber when warm, rose when hot.
    /// Reads resources live so a mid-session accent change retints a cool aura with everything else.
    /// </summary>
    public static Color GlowColorFor(PortraitSeverity tone)
    {
        var resources = Application.Current.Resources;
        return tone switch
        {
            PortraitSeverity.Critical => (Color)resources["StatusHotColor"],
            PortraitSeverity.Warning => (Color)resources["StatusWarmColor"],
            _ => (Color)resources["AetherAccentColor"]
        };
    }

    /// <summary>Returns the new tone when the aura should retint, or null when it should hold.</summary>
    public PortraitSeverity? Update(double worstTempCelsius)
    {
        var raw = PortraitSeverityThresholds.ForTemp(worstTempCelsius);
        if (raw > _current)
        {
            _current = raw;
            return _current;
        }

        if (raw < _current)
        {
            var floor = _current == PortraitSeverity.Critical ? CriticalFloor : WarnFloor;
            if (worstTempCelsius <= floor)
            {
                _current = raw;
                return _current;
            }
        }

        return null;
    }
}
