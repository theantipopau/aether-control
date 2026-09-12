namespace AetherControl.Core.Animation;

/// <summary>What a new target value did to the tween on arrival.</summary>
public enum TweenTransition
{
    /// <summary>The target wasn't a real reading (NaN/Infinity) — ignored outright, nothing changed.</summary>
    Ignored,
    /// <summary>Close enough to the current value that it snapped instantly, no animation needed.</summary>
    Snapped,
    /// <summary>A genuinely different value — now animating toward it.</summary>
    Animating
}

/// <summary>
/// Pure, UI-framework-independent core of the number-tween used for animated dashboard values —
/// deliberately holds no <c>CompositionTarget</c>/<c>Stopwatch</c> dependency so it can be unit
/// tested directly (see AetherControl.App.Controls.NumberTween, the thin WinUI3 adapter around this).
/// Ports Radium PCs Companion's <c>useAnimatedNumber</c> hook; a real divergence from that proven
/// source was found and fixed here during a storage-flicker audit — the original hook coerces a
/// non-finite value to 0 and returns immediately, but an earlier version of the WinUI3 port coerced
/// to 0 without returning, so it fell through into the animation path and visibly glided the last
/// good value down toward a fabricated 0 (passing through the midpoint on the way) whenever a single
/// non-finite update slipped through. This version goes further than restoring the original:
/// a non-finite target is <see cref="TweenTransition.Ignored"/> outright rather than snapped to 0,
/// since "no real reading this update" and "confirmed zero" are different facts.
/// </summary>
public sealed class TweenState
{
    private const double SnapThreshold = 0.05;

    private double _from;
    private double _to;

    public double Current { get; private set; }
    public bool IsAnimating { get; private set; }

    public TweenTransition SetTarget(double target)
    {
        if (!double.IsFinite(target))
        {
            return TweenTransition.Ignored;
        }

        if (Math.Abs(target - Current) < SnapThreshold)
        {
            // Capturing "already there" as a real transition (not a no-op) matters when a caller
            // wants to know whether anything needs to happen — e.g. stop any in-flight animation.
            Current = target;
            _to = target;
            IsAnimating = false;
            return TweenTransition.Snapped;
        }

        // From wherever the animation currently IS (its last interpolated Current), not from
        // wherever it started — this is what makes a new target arriving mid-animation redirect
        // smoothly instead of restarting the glide from the original starting point.
        _from = Current;
        _to = target;
        IsAnimating = true;
        return TweenTransition.Animating;
    }

    /// <summary>Advances the animation to <paramref name="progress"/> (0-1 of the configured
    /// duration) and returns the interpolated value. Clamped, so an out-of-range progress can't
    /// overshoot the target.</summary>
    public double Advance(double progress)
    {
        var clamped = Math.Clamp(progress, 0.0, 1.0);
        var eased = 1 - Math.Pow(1 - clamped, 3);
        Current = _from + (_to - _from) * eased;

        if (clamped >= 1.0)
        {
            IsAnimating = false;
        }

        return Current;
    }
}
