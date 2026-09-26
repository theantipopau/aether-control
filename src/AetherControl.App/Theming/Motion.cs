using Windows.UI.ViewManagement;

namespace AetherControl.App.Theming;

/// <summary>
/// One place for every animation duration in the app, plus the Windows "Animation effects"
/// preference (Settings → Accessibility → Visual effects). Before this, each control hard-coded its
/// own timing (150 ms hover, 220 ms number tween) and nothing honoured reduced motion at all.
/// Callers check <see cref="AnimationsEnabled"/> and snap straight to the end state when it's false —
/// the value still changes, only the transition is skipped, so no information is lost.
/// </summary>
internal static class Motion
{
    public static readonly TimeSpan Hover = TimeSpan.FromMilliseconds(140);
    public static readonly TimeSpan ValueChange = TimeSpan.FromMilliseconds(220);
    public static readonly TimeSpan PageTransition = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan ModeChange = TimeSpan.FromMilliseconds(340);

    private static readonly UISettings Settings = CreateSettings();
    private static bool _systemAnimationsEnabled = ReadSystemPreference();

    /// <summary>False when Windows' animation effects are off. Cached and refreshed on change —
    /// read on every tween start, so it must not construct a UISettings each time.</summary>
    public static bool AnimationsEnabled => _systemAnimationsEnabled;

    private static UISettings CreateSettings()
    {
        var settings = new UISettings();
        try
        {
            settings.AnimationsEnabledChanged += (_, _) => _systemAnimationsEnabled = ReadSystemPreference();
        }
        catch
        {
            // Older Windows builds lack the change event — the startup value still applies.
        }

        return settings;
    }

    private static bool ReadSystemPreference()
    {
        try
        {
            return Settings?.AnimationsEnabled ?? true;
        }
        catch
        {
            return true;
        }
    }
}
