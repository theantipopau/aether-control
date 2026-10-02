using AetherControl.Core.Timing;

namespace AetherControl.Tests;

/// <summary>
/// Windows write focus flags from the UI thread; the poll timer reads them. Both directions must
/// work, and one window going inactive must not hide another still-active one.
/// </summary>
public class UiActivityTrackerTests
{
    [Fact]
    public void DefaultsToInactive()
    {
        Assert.False(new UiActivityTracker().AnyWindowActive);
    }

    [Fact]
    public void ActivatingAWindow_MakesActivityTrue_DeactivatingMakesItFalse()
    {
        var tracker = new UiActivityTracker();

        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, true);
        Assert.True(tracker.AnyWindowActive);

        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, false);
        Assert.False(tracker.AnyWindowActive);
    }

    [Fact]
    public void OneWindowLosingFocus_DoesNotHideAnotherStillActive()
    {
        var tracker = new UiActivityTracker();
        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, true);
        tracker.SetWindowActive(UiActivityTracker.PortraitWindowKey, true);

        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, false);
        Assert.True(tracker.AnyWindowActive);

        tracker.SetWindowActive(UiActivityTracker.PortraitWindowKey, false);
        Assert.False(tracker.AnyWindowActive);
    }

    [Fact]
    public void RepeatedSetOfSameState_IsIdempotent()
    {
        var tracker = new UiActivityTracker();
        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, true);
        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, true);
        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, false);
        tracker.SetWindowActive(UiActivityTracker.MainWindowKey, false);

        Assert.False(tracker.AnyWindowActive);
    }
}
