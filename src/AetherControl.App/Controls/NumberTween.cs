using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>
/// Ports the cubic ease-out number-tween from Radium PCs Companion's
/// <c>useAnimatedNumber</c> hook (a per-frame <c>requestAnimationFrame</c>
/// loop) to WinUI3's <see cref="CompositionTarget.Rendering"/> event, which
/// is the same "run a callback every frame" primitive. Deliberately not a
/// XAML <c>Storyboard</c>/<c>DoubleAnimation</c>: that approach silently
/// failed to update a custom dependency property when driven from x:Bind's
/// very first assignment, which is exactly what left every dashboard tile
/// blank on first launch. A plain per-frame callback has no such failure mode.
/// </summary>
internal sealed class NumberTween(Action<double> onUpdate)
{
    private const double DurationMs = 220;
    private const double SnapThreshold = 0.05;

    private double _current;
    private double _from;
    private double _to;
    private long _startTimestamp;
    private bool _isRunning;

    public double Current => _current;

    public void AnimateTo(double target)
    {
        if (!double.IsFinite(target))
        {
            target = 0;
        }

        if (Math.Abs(target - _current) < SnapThreshold)
        {
            _current = target;
            _to = target;
            Stop();
            onUpdate(_current);
            return;
        }

        _from = _current;
        _to = target;
        _startTimestamp = Stopwatch.GetTimestamp();

        if (!_isRunning)
        {
            _isRunning = true;
            CompositionTarget.Rendering += OnRendering;
        }
    }

    private void OnRendering(object? sender, object e)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
        var progress = Math.Clamp(elapsedMs / DurationMs, 0, 1);
        var eased = 1 - Math.Pow(1 - progress, 3);
        _current = _from + (_to - _from) * eased;
        onUpdate(_current);

        if (progress >= 1)
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
