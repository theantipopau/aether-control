using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace AetherControl.App.Theming;

/// <summary>
/// Portrait Mode skins: alternate background treatments for the portrait window that respect the
/// single-accent identity (per-metric hues were deliberately removed — skins only touch page/card/
/// border surfaces, never accent or severity colours). Applied by mutating the three shared
/// portrait brushes in place (the <see cref="AccentPalette"/> precedent), so an open Portrait
/// window re-skins instantly with no restart. Unknown ids fall back to Default.
/// </summary>
public static class PortraitSkinApplier
{
    public static IReadOnlyList<(string Id, string Label)> Skins { get; } =
    [
        ("Default", "Charcoal (default)"),
        ("OledBlack", "OLED black"),
        ("Midnight", "Midnight blue")
    ];

    public static string Normalise(string? id) =>
        Skins.Any(s => s.Id == id) ? id! : "Default";

    public static void Apply(string? id)
    {
        var resources = Microsoft.UI.Xaml.Application.Current.Resources;
        var (bg, card, border) = Colours(Normalise(id), resources);
        ((SolidColorBrush)resources["PortraitPageBgBrush"]).Color = bg;
        ((SolidColorBrush)resources["PortraitCardBgBrush"]).Color = card;
        ((SolidColorBrush)resources["PortraitCardBorderBrush"]).Color = border;
    }

    private static (Color Bg, Color Card, Color Border) Colours(string id, Microsoft.UI.Xaml.ResourceDictionary resources) =>
        id switch
        {
            // True black surfaces for OLED panels; text and accent untouched so contrast is preserved.
            "OledBlack" => (Color.FromArgb(255, 0, 0, 0), Color.FromArgb(255, 10, 10, 10), Color.FromArgb(255, 38, 38, 38)),
            "Midnight" => (Color.FromArgb(255, 10, 17, 32), Color.FromArgb(255, 16, 26, 48), Color.FromArgb(255, 42, 58, 92)),
            // Default re-reads the base tokens (never hardcodes them) so a future palette change
            // or a previous skin can't leave stale colours behind.
            _ => ((Color)resources["AetherBackgroundColor"], (Color)resources["AetherSurfaceColor"], (Color)resources["AetherBorderColor"])
        };
}
