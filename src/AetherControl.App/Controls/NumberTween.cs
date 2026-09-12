using System.Diagnostics;
using AetherControl.Core.Animation;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>
/// WinUI3 adapter around <see cref="TweenState"/> (the actual decision logic — pure, unit-tested,
/// no WinUI dependency). Drives it from <see cref="CompositionTarget.Rendering"/>, the same "run a
/// callback every frame" primitive Radium PCs Companion's original <c>useAnimatedNumber</c> hook
/// gets from <c>requestAnimationFrame</c>. Deliberately not a XAML <c>Storyboard</c>/<c>DoubleAnimation</c>:
/// that approach silently failed to update a custom dependency property when driven from x:Bind's
/// very first assignment, which is exactly what left every dashboard tile blank on first launch. A
/// plain per-frame callback has no such failure mode.
/// </summary>
internal sealed class NumberTween(Action<double> onUpdate)
{
    private const double DurationMs = 220;

    private readonly TweenState _state = new();
    private long _startTimestamp;
    private bool _isRunning;

    public double Current => _state.Current;

    public void AnimateTo(double target)
    {
        switch (_state.SetTarget(target))
        {
            case TweenTransition.Ignored:
                // Not a real reading (NaN/Infinity) this update — leave whatever's on screen
                // exactly as it was. See TweenState's own doc comment for the bug this guards.
                return;

            case TweenTransition.Snapped:
                Stop();
                onUpdate(_state.Current);
                return;

            case TweenTransition.Animating:
                _startTimestamp = Stopwatch.GetTimestamp();
                if (!_isRunning)
                {
                    _isRunning = true;
                    CompositionTarget.Rendering += OnRendering;
                }

                return;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        var current = _state.Advance(elapsedMs / DurationMs);
        onUpdate(current);

        if (!_state.IsAnimating)
        {
            Stop();
        }
    }

    private void Stop()
    {
        if (_isRunning)
        {
            CompositionTarget.Rendering -= OnRendering;
            _isRunning = false;
        }
    }
}
