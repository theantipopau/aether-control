using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace AetherControl.App.Controls;

/// <summary>
/// Shimmer placeholder shown while real content hasn't arrived (first sensor poll, LHM init).
/// The sheen sweeps the actual width on a loop; under reduced motion it stays parked off-screen
/// (a static elevated block — still clearly a placeholder, with zero motion). The storyboard is
/// stopped on unload so a dead control can't keep ticking.
/// </summary>
public sealed partial class SkeletonBlock : UserControl
{
    private Storyboard? _sweep;

    public SkeletonBlock()
    {
        InitializeComponent();
        Loaded += (_, _) => StartSweep();
        Unloaded += (_, _) => StopSweep();
        SizeChanged += (_, _) => StartSweep();
    }

    private void StartSweep()
    {
        StopSweep();
        if (!Theming.Motion.AnimationsEnabled || ActualWidth <= 0)
        {
            return;
        }

        var anim = new DoubleAnimation
        {
            From = -Sheen.Width,
            To = ActualWidth,
            Duration = new Duration(TimeSpan.FromSeconds(1.4)),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(anim, SheenShift);
        Storyboard.SetTargetProperty(anim, "X");
        _sweep = new Storyboard();
        _sweep.Children.Add(anim);
        _sweep.Begin();
    }

    private void StopSweep()
    {
        _sweep?.Stop();
        _sweep = null;
    }
}
