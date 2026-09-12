using AetherControl.Core.Animation;

namespace AetherControl.Tests;

/// <summary>
/// Regression coverage for the animation hypothesis raised during the storage-flicker audit: does
/// NumberTween itself produce a value like "half of the real reading"? Answer, evidenced here: yes,
/// under one specific condition — a non-finite (NaN/Infinity) target used to be coerced to 0 and then
/// animated toward, which for a value around 680 would visibly pass through ~340 on the way down.
/// Fixed in TweenState.SetTarget (Ignored transition, never reached by the animation path at all).
/// These tests exercise the pure decision logic directly — CompositionTarget.Rendering (the WinUI3
/// per-frame driver in NumberTween) can't run in a headless xUnit host, so the actual frame-pumping
/// in NumberTween itself is not covered here; TweenState.Advance is called directly instead, which
/// covers everything NumberTween.OnRendering does except the real-time Stopwatch wiring.
/// </summary>
public class TweenStateTests
{
    [Fact]
    public void NewState_StartsAtZero_MatchingCSharpDoubleDefault()
    {
        // Not asserted as "correct" so much as pinned as documented, known behaviour: the very
        // first SetTarget call on a fresh card always animates FROM 0, which is why a freshly
        // materialized MetricCard fades in from 0 rather than starting at its real value the way
        // Radium's original React hook does (useState(value) seeds the *actual* initial value).
        var state = new TweenState();
        Assert.Equal(0.0, state.Current);
        Assert.False(state.IsAnimating);
    }

    [Fact]
    public void NonFiniteTarget_IsIgnored_CurrentValueUnchanged()
    {
        var state = new TweenState();
        state.SetTarget(682.34);
        state.Advance(1.0); // let it settle at 682.34
        Assert.Equal(682.34, state.Current, precision: 6);

        var transition = state.SetTarget(double.NaN);

        Assert.Equal(TweenTransition.Ignored, transition);
        Assert.Equal(682.34, state.Current, precision: 6); // unchanged — not 0, not half
        Assert.False(state.IsAnimating); // did not start animating toward a fabricated 0 either
    }

    [Fact]
    public void PositiveInfinityTarget_IsAlsoIgnored()
    {
        var state = new TweenState();
        state.SetTarget(530.0);
        state.Advance(1.0);

        Assert.Equal(TweenTransition.Ignored, state.SetTarget(double.PositiveInfinity));
        Assert.Equal(530.0, state.Current, precision: 6);
    }

    [Fact]
    public void RealZeroTarget_IsALegitimateAnimationTarget_NotIgnored()
    {
        // 0 is a real, meaningful value (an empty drive, 0% load) and must still animate normally —
        // the fix only special-cases non-finite input, not the number zero itself.
        var state = new TweenState();
        state.SetTarget(682.0);
        state.Advance(1.0);

        var transition = state.SetTarget(0.0);

        Assert.Equal(TweenTransition.Animating, transition);
        var midpoint = state.Advance(0.5);
        Assert.InRange(midpoint, 0.0, 682.0);
    }

    [Fact]
    public void RepeatedIdenticalTarget_SnapsInstead_DoesNotRestartAnimation()
    {
        var state = new TweenState();
        state.SetTarget(682.34);
        state.Advance(1.0);
        Assert.False(state.IsAnimating);

        var transition = state.SetTarget(682.34);

        Assert.Equal(TweenTransition.Snapped, transition);
        Assert.False(state.IsAnimating);
        Assert.Equal(682.34, state.Current, precision: 6);
    }

    [Fact]
    public void NearlyIdenticalTarget_WithinSnapThreshold_AlsoSnapsInstantly()
    {
        // Real poll-to-poll drift (e.g. 682.34 -> 682.30 from normal disk writes) should never
        // trigger a visible 220ms glide for a change too small to perceive.
        var state = new TweenState();
        state.SetTarget(682.34);
        state.Advance(1.0);

        var transition = state.SetTarget(682.30);

        Assert.Equal(TweenTransition.Snapped, transition);
        Assert.Equal(682.30, state.Current, precision: 6);
    }

    [Fact]
    public void NewTargetArrivingMidAnimation_RedirectsFromCurrentInterpolatedValue_NotFromOriginalStart()
    {
        var state = new TweenState();
        state.SetTarget(1000.0);
        var midway = state.Advance(0.5); // somewhere between 0 and 1000, not exactly halfway due to easing
        Assert.True(midway is > 0 and < 1000);

        // A new target arrives before the first animation finished.
        state.SetTarget(0.0);
        var next = state.Advance(0.0); // progress 0 of the *new* animation

        // The new animation's starting point must be where the display actually was (midway),
        // not the original 0 the first animation started from — otherwise the value would jump
        // backwards to the old start point for one frame before redirecting.
        Assert.Equal(midway, next, precision: 6);
    }

    [Fact]
    public void CompletedAnimation_ClearsIsAnimating()
    {
        var state = new TweenState();
        state.SetTarget(500.0);
        Assert.True(state.IsAnimating);

        state.Advance(1.0);

        Assert.False(state.IsAnimating);
        Assert.Equal(500.0, state.Current, precision: 6);
    }

    [Fact]
    public void AlternatingSnapshotsWithIdenticalModelValue_NeverAnimates()
    {
        // Simulates a card's value being pushed on every poll even though the underlying reading
        // hasn't changed (the ObservableCollectionMergeExtensions.MergeFrom path replaces the model
        // instance every poll even when its FreeGb is identical) — must settle to "does nothing"
        // rather than perpetually restarting a sub-threshold animation.
        var state = new TweenState();
        state.SetTarget(978.24);
        state.Advance(1.0);

        for (var i = 0; i < 20; i++)
        {
            var transition = state.SetTarget(978.24);
            Assert.Equal(TweenTransition.Snapped, transition);
            Assert.False(state.IsAnimating);
        }
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(340.5)]
    [InlineData(682.34)]
    [InlineData(978.24)]
    [InlineData(1_048_575.9)] // ~1 PiB in GiB — realistic upper bound for a very large array
    public void LargeRealisticGbValues_RemainPrecise(double target)
    {
        var state = new TweenState();
        state.SetTarget(target);
        state.Advance(1.0);

        Assert.Equal(target, state.Current, precision: 6);
    }
}
