using System.Numerics;
using AetherControl.App.Theming;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>
/// Signature backdrop: a low-cost, GPU-composited radial glow that drifts slowly and tints to the
/// system's thermal state (accent-cool → amber → rose). Pure Composition — no per-frame managed
/// code, no timers. Sits behind content, above the Mica backdrop. Created only when the matching
/// glow setting is on (see ThermalAuraSettingsStore), so off means zero cost, not a hidden visual.
/// </summary>
public sealed class ThermalAura : Grid
{
    // Single-use constants (not shared Motion tokens): nothing else in the app drifts on a 28 s
    // loop or cross-fades colour over 1.2 s.
    private static readonly TimeSpan DriftPeriod = TimeSpan.FromSeconds(28);
    private static readonly TimeSpan ToneShift = TimeSpan.FromSeconds(1.2);

    private Compositor? _compositor;
    private SpriteVisual? _sprite;
    private CompositionRadialGradientBrush? _brush;
    private CompositionColorGradientStop? _core;
    private bool _driftPaused;

    public ThermalAura()
    {
        IsHitTestVisible = false;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += (_, e) =>
        {
            if (_sprite is not null)
            {
                _sprite.Size = new Vector2((float)e.NewSize.Width, (float)e.NewSize.Height);
            }
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _compositor = ElementCompositionPreview.GetElementVisual(this).Compositor;

        _brush = _compositor.CreateRadialGradientBrush();
        _brush.MappingMode = CompositionMappingMode.Relative;
        _brush.EllipseCenter = new Vector2(0.22f, 0.18f);
        _brush.EllipseRadius = new Vector2(0.75f, 0.75f);

        _core = _compositor.CreateColorGradientStop(0f, Color.FromArgb(40, 56, 189, 248));
        _brush.ColorStops.Add(_core);
        _brush.ColorStops.Add(_compositor.CreateColorGradientStop(1f, Colors.Transparent));

        _sprite = _compositor.CreateSpriteVisual();
        _sprite.Brush = _brush;
        _sprite.Size = new Vector2((float)ActualWidth, (float)ActualHeight);
        ElementCompositionPreview.SetElementChildVisual(this, _sprite);

        if (!_driftPaused)
        {
            StartDrift();
        }
    }

    /// <summary>Call when the hysteresis-tracked tone changes (NOT every poll) — the caller owns
    /// the banding (see ThermalToneTracker); this only paints.</summary>
    public void SetTone(Color target)
    {
        if (_compositor is null || _core is null)
        {
            return;
        }

        var glow = Color.FromArgb(40, target.R, target.G, target.B);

        if (!Motion.AnimationsEnabled)
        {
            _core.Color = glow;
            return;
        }

        var anim = _compositor.CreateColorKeyFrameAnimation();
        anim.InsertKeyFrame(1f, glow, Motion.EaseOut(_compositor));
        anim.Duration = ToneShift;
        _core.StartAnimation(nameof(CompositionColorGradientStop.Color), anim);
    }

    /// <summary>Pauses the drift loop when the window is minimised, hidden or deactivated —
    /// a glow nobody can see shouldn't spend GPU cycles. Tone updates still apply while paused.
/// </summary>
    public void SetDriftPaused(bool paused)
    {
        _driftPaused = paused;
        if (_brush is null)
        {
            return; // not loaded yet — OnLoaded consults _driftPaused before starting
        }

        if (paused)
        {
            _brush.StopAnimation(nameof(CompositionRadialGradientBrush.EllipseCenter));
        }
        else if (Motion.AnimationsEnabled)
        {
            StartDrift();
        }
    }

    private void StartDrift()
    {
        if (_compositor is null || _brush is null)
        {
            return;
        }

        var drift = _compositor.CreateVector2KeyFrameAnimation();
        drift.InsertKeyFrame(0.5f, new Vector2(0.78f, 0.62f));
        drift.InsertKeyFrame(1f, new Vector2(0.22f, 0.18f));
        drift.Duration = DriftPeriod;
        drift.IterationBehavior = AnimationIterationBehavior.Forever;
        _brush.StartAnimation(nameof(CompositionRadialGradientBrush.EllipseCenter), drift);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _brush?.StopAnimation(nameof(CompositionRadialGradientBrush.EllipseCenter));
        ElementCompositionPreview.SetElementChildVisual(this, null);
        _sprite?.Dispose();
        _brush?.Dispose();
        _sprite = null;
        _brush = null;
        _core = null;
    }
}
