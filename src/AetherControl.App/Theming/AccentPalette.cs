using AetherControl.Core.Enums;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AetherControl.App.Theming;

/// <summary>
/// Applies the user's chosen accent colour. Settings' Accent picker previously did nothing at
/// all — <c>Colors.xaml</c> hardcoded Cyan with a TODO noting the picker wasn't wired up.
/// <para>
/// Two different mechanisms are needed because WinUI3 resources don't all behave the same way
/// at runtime: <c>AetherAccentBrush</c> is a single shared <see cref="SolidColorBrush"/> instance
/// that every one of Aether Control's own custom-styled elements (gauges, cards, hover borders,
/// <c>SegmentedToggle</c>) points at via <c>{StaticResource}</c> — mutating that instance's
/// <see cref="SolidColorBrush.Color"/> in place repaints all of them immediately, with no restart.
/// Native Fluent controls (stock <c>Button</c>/<c>ToggleSwitch</c>/<c>NavigationView</c> selection/
/// <c>Slider</c>) instead derive their brushes from <c>SystemAccentColor</c> through WinUI's own
/// internal <c>ThemeResource</c> chains, which don't reliably re-resolve from a plain dictionary
/// value swap — those pick up the new colour on next launch, once <see cref="Apply"/> runs again
/// during startup before any window is shown.
/// </para>
/// </summary>
internal static class AccentPalette
{
    public static void Apply(AccentColor accent)
    {
        var baseColor = GetBaseColor(accent);
        var resources = Application.Current.Resources;

        resources["AetherAccentColor"] = baseColor;
        if (resources["AetherAccentBrush"] is SolidColorBrush accentBrush)
        {
            accentBrush.Color = baseColor;
        }

        // Same ratios the original hardcoded Cyan cascade used (25/50/75% lightened for the three
        // Light steps, 80/60/40% of full brightness for the three Dark steps) — reverse-engineered
        // from the original fixed values so every accent choice gets a consistent-feeling cascade.
        resources["SystemAccentColor"] = baseColor;
        resources["SystemAccentColorLight1"] = Lighten(baseColor, 0.25);
        resources["SystemAccentColorLight2"] = Lighten(baseColor, 0.50);
        resources["SystemAccentColorLight3"] = Lighten(baseColor, 0.75);
        resources["SystemAccentColorDark1"] = Darken(baseColor, 0.8);
        resources["SystemAccentColorDark2"] = Darken(baseColor, 0.6);
        resources["SystemAccentColorDark3"] = Darken(baseColor, 0.4);
    }

    private static Color GetBaseColor(AccentColor accent) => accent switch
    {
        AccentColor.Cyan => Color.FromArgb(255, 0x00, 0xE5, 0xFF),
        AccentColor.Blue => Color.FromArgb(255, 0x3B, 0x82, 0xF6),
        AccentColor.Green => Color.FromArgb(255, 0x22, 0xC5, 0x5E),
        AccentColor.Amber => Color.FromArgb(255, 0xF5, 0x9E, 0x0B),
        AccentColor.Purple => Color.FromArgb(255, 0xA8, 0x55, 0xF7),
        _ => Color.FromArgb(255, 0x00, 0xE5, 0xFF)
    };

    private static Color Lighten(Color c, double amount)
    {
        byte Mix(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static Color Darken(Color c, double factor) =>
        Color.FromArgb(c.A, (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor));
}
