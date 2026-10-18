using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
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
    // Gate 3 (Phase 56): entrance choreography. Reflow matches the compositor's own glide pace;
    // Stagger matches ModeChange's unhurried feel. Both join the central tokens above rather than
    // a second motion file, so reduced-motion and duration tuning stay in exactly one place.
    public static readonly TimeSpan Reflow = TimeSpan.FromMilliseconds(260);
    public static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(420);

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

    /// <summary>Shared ease-out curve for every Composition animation in the app.</summary>
    public static CompositionEasingFunction EaseOut(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

    /// <summary>Cards glide to new positions when the layout reflows (window resize, panel
    /// collapse). Pure compositor side: no per-frame managed code, no timers. No-op when Windows
    /// animation effects are off. Note: this fires for Offset changes the layout engine routes
    /// through the visual; whether a given panel reflow takes that path needs Matt's eyeball —
    /// if cards snap instead of gliding on resize, the stagger-in below is still the visible win.
    /// </summary>
    public static void EnableImplicitReflow(UIElement element)
    {
        if (!AnimationsEnabled)
        {
            return;
        }

        var visual = ElementCompositionPreview.GetElementVisual(element);
        var c = visual.Compositor;

        var offset = c.CreateVector3KeyFrameAnimation();
        offset.Target = "Offset";
        offset.InsertExpressionKeyFrame(1f, "this.FinalValue", EaseOut(c));
        offset.Duration = Reflow;

        var collection = c.CreateImplicitAnimationCollection();
        collection["Offset"] = offset;
        visual.ImplicitAnimations = collection;
    }

    /// <summary>Staggered fade + rise for a page's cards. Run once per app session (first
    /// navigation), never on polls or back-navigation — the caller gates that. When animations
    /// are off, every element is pinned to its end state (opacity 1, no translation) so nothing
    /// can be left invisible.
    /// </summary>
    public static void StaggerIn(IReadOnlyList<UIElement> elements, int staggerMs = 35)
    {
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var visual = ElementCompositionPreview.GetElementVisual(element);

            if (!AnimationsEnabled)
            {
                visual.Opacity = 1f;
                continue;
            }

            var c = visual.Compositor;
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var delay = TimeSpan.FromMilliseconds(i * staggerMs);

            var fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, EaseOut(c));
            fade.Duration = Stagger;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            var rise = c.CreateVector3KeyFrameAnimation();
            rise.InsertKeyFrame(0f, new Vector3(0f, 14f, 0f));
            rise.InsertKeyFrame(1f, Vector3.Zero, EaseOut(c));
            rise.Duration = Stagger;
            rise.DelayTime = delay;
            rise.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;

            visual.StartAnimation("Opacity", fade);
            visual.StartAnimation("Translation", rise);
        }
    }
}
