using System.Collections.Generic;

namespace AetherControl.Core.Timing;

/// <summary>
/// Shared "is any Aether window currently focused" flag: written by each window's
/// <c>Window.Activated</c> handler on the UI thread, read by the hardware poll timer on a
/// background thread. Focus rather than on-screen visibility is the signal — minimising or
/// closing to tray deactivates the window, and a user who alt-tabs back to the dashboard gets
/// full polling cadence on the very next tick. Keyed per window so the main window losing focus
/// never hides the portrait window still being actively used (and vice versa).
/// </summary>
public sealed class UiActivityTracker
{
    public const string MainWindowKey = "main";
    public const string PortraitWindowKey = "portrait";

    private readonly HashSet<string> _activeWindows = new();
    private readonly object _gate = new();

    public void SetWindowActive(string windowKey, bool active)
    {
        lock (_gate)
        {
            if (active)
            {
                _activeWindows.Add(windowKey);
            }
            else
            {
                _activeWindows.Remove(windowKey);
            }
        }
    }

    public bool AnyWindowActive
    {
        get
        {
            lock (_gate)
            {
                return _activeWindows.Count > 0;
            }
        }
    }
}
