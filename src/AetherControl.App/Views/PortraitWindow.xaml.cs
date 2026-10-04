using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Layout;
using AetherControl.Core.Timing;
using AetherControl.Services.Hardware;
using AetherControl.Services.Processes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;

namespace AetherControl.App.Views;

/// <summary>
/// Portrait Mode — a real port of Matt's own Portrait Stats layout (see <see cref="PortraitViewModel"/>
/// and the Portrait* controls). Opens at 768x1366 (Portrait Stats' own fixed size) but immediately
/// auto-fills whichever connected display is actually rotated to portrait, if one exists — see
/// <see cref="FindPortraitDisplay"/> — so the window matches that monitor's real resolution instead
/// of assuming every portrait panel is exactly 768x1366.
/// <para>
/// Dragging uses <see cref="Window.SetTitleBar"/> on <c>DragRegion</c> — the same mechanism
/// <c>MainWindow</c> already uses successfully — rather than a hand-rolled
/// GetCursorPos/AppWindow.Move implementation tried first, which didn't actually move the window
/// in practice. The window is user-resizable from its edges (it used to be fixed-size, which
/// stranded a 768x1366 layout on any monitor that isn't that exact panel); minimise/maximise stay
/// disabled since this is a fixed-purpose utility panel. While FILL is on, the window follows its
/// display — see <see cref="OnAppWindowChanged"/>. Close is intercepted to hide rather than
/// destroy the window, since <c>MainWindow</c> caches this instance and re-<c>Activate()</c>s it
/// on next open.
/// </para>
/// </summary>
public sealed partial class PortraitWindow : Window
{
    public PortraitViewModel ViewModel { get; }

    private bool _forceClose;
    private bool _isFilled;
    private PointInt32 _preFillPosition;
    private SizeInt32 _preFillSize;

    public PortraitWindow()
    {
        InitializeComponent();
        Title = "Aether Control — Portrait Mode";
        ViewModel = new PortraitViewModel(
            App.Services.GetRequiredService<IHardwareMonitorService>(),
            App.Services.GetRequiredService<ProcessRankerService>(),
            App.Services.GetRequiredService<IFpsSource>(),
            App.Services.GetRequiredService<FanLabelStore>());

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(DragRegion);
        ConfigureTitleBarButtons();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            // Resizable from the edges: a fixed size only ever matched one specific portrait panel.
            // FILL (below) still snaps to exact display bounds programmatically — that flag only
            // affects user edge-dragging.
            presenter.IsResizable = true;
            presenter.IsMinimizable = false;
            presenter.IsMaximizable = false;
        }

        // 768x1366 matches Portrait Stats' own fixed size — the resolution Matt's actual portrait
        // monitor reports once rotated in Windows Display Settings. Kept as the floating-window
        // fallback/restore size below, not as the only size this window can ever be: a fixed size
        // only matches one specific monitor, and on anything else (a taller/shorter portrait panel,
        // a different rotation) the bottom of the layout — Processes, last in the ScrollViewer —
        // ends up positioned off the real screen instead of just needing a scroll.
        AppWindow.Resize(new SizeInt32(768, 1366));
        _preFillPosition = AppWindow.Position;
        _preFillSize = AppWindow.Size;

        // Auto-detect a connected portrait-oriented display and fill it immediately on open, instead
        // of requiring the old manual "drag the window onto the monitor, then click FILL" two-step —
        // that only worked if the window happened to be dragged onto a monitor whose real resolution
        // matched the 768x1366 default; any mismatch is exactly what put Processes off-screen.
        var portraitDisplay = FindPortraitDisplay();
        if (portraitDisplay is not null)
        {
            AppWindow.MoveAndResize(portraitDisplay.OuterBounds);
            _isFilled = true;
            // Reflects reality in the UI; OnFillScreenChecked's _isFilled guard stops this from
            // re-capturing _preFillPosition/_preFillSize from the now-already-filled bounds.
            FillScreenToggle.IsChecked = true;
        }

        // While FILL is on, the window stays locked to whichever display it's on — the "dragged it
        // to the other monitor and now it's a floating portrait-sized rectangle on a landscape
        // screen" failure mode.
        // Same adaptive-cadence focus flag as the main window, keyed per window — the portrait
        // panel being actively used keeps polling at full rate even while the main window sits in
        // the tray. Hiding (close is intercepted to hide, not destroy) deactivates the window, so
        // the flag clears itself without needing a separate path here.
        var uiActivity = App.Services.GetRequiredService<UiActivityTracker>();
        Activated += (_, e) => uiActivity.SetWindowActive(
            UiActivityTracker.PortraitWindowKey, e.WindowActivationState != WindowActivationState.Deactivated);

        AppWindow.Changed += OnAppWindowChanged;
        Closed += OnClosed;
    }

    /// <summary>Re-snaps a FILLed window to the display it currently sits on. Fires on every
    /// position/size change, so dragging to a different monitor (or a resolution/orientation change
    /// on that monitor) pulls the window to the new bounds instead of stranding it. Snapping is
    /// idempotent: our own MoveAndResize re-enters here, finds the bounds already matching, and
    /// does nothing — no loop. When FILL is off, this never runs, so ordinary free-form dragging
    /// and edge-resizing are untouched.</summary>
    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (!_isFilled || (!args.DidPositionChange && !args.DidSizeChange))
        {
            return;
        }

        var display = DisplayArea.GetFromWindowId(sender.Id, DisplayAreaFallback.Nearest);
        var bounds = display.OuterBounds;
        var current = sender.Position;
        var size = sender.Size;
        if (current.X != bounds.X || current.Y != bounds.Y ||
            size.Width != bounds.Width || size.Height != bounds.Height)
        {
            sender.MoveAndResize(bounds);
        }
    }

    /// <summary>Picks a connected display taller than it is wide — i.e. actually rotated to
    /// portrait in Windows Display Settings, not just guessed from this window's own fixed size.
    /// Prefers a non-primary one: a portrait panel is almost always a secondary accessory monitor,
    /// and auto-filling the user's actual primary/landscape display would be actively wrong.
    /// <para>
    /// Indexes into <see cref="DisplayArea.FindAll"/>'s result by <c>Count</c>/<c>[i]</c> rather than
    /// LINQ — live-captured crash evidence (<c>unhandled-exceptions.log</c>, 2026-09-24) showed
    /// <c>.Where().ToList()</c> throwing <c>InvalidCastException: No such interface supported</c>
    /// from inside <c>IReadOnlyListImpl.GetEnumerator()</c>: this WinAppSDK version's WinRT
    /// projection for that return type doesn't implement <c>IEnumerable&lt;DisplayArea&gt;</c>
    /// enumeration correctly, even though the same object's indexer/Count (backed by a real
    /// <c>IVectorView</c>) works fine. Portrait Mode silently failed to open every time as a result.
    /// </para></summary>
    private static DisplayArea? FindPortraitDisplay()
    {
        var displays = DisplayArea.FindAll();
        DisplayArea? firstPortrait = null;

        for (var i = 0; i < displays.Count; i++)
        {
            var display = displays[i];
            if (display.OuterBounds.Height <= display.OuterBounds.Width)
            {
                continue;
            }

            if (!display.IsPrimary)
            {
                return display;
            }

            firstPortrait ??= display;
        }

        return firstPortrait;
    }

    private void ConfigureTitleBarButtons()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Color.FromArgb(255, 0x7C, 0x85, 0x98);
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(50, 255, 255, 255);
        titleBar.ButtonPressedForegroundColor = Colors.White;
    }

    /// <summary>Called by MainWindow during a real app exit — unlike a normal close (which this
    /// window intercepts to just hide), shutdown needs the window to actually go away.</summary>
    public void ForceClose()
    {
        _forceClose = true;
        Close();
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        if (_forceClose)
        {
            ViewModel.Dispose();
            return;
        }

        args.Handled = true;
        AppWindow.Hide();
    }

    private void OnAlwaysOnTopChanged(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsAlwaysOnTop = AlwaysOnTopToggle.IsChecked == true;
        }
    }

    private void OnOledToggleChanged(object sender, RoutedEventArgs e)
    {
        var oled = OledToggle.IsChecked == true;
        RootGrid.Background = oled
            ? new SolidColorBrush(Colors.Black)
            : (Brush)Application.Current.Resources["PortraitPageBgBrush"];
    }

    /// <summary>Resizes and repositions to fill whatever monitor the window is currently on — for
    /// the rare case the auto-detect in the constructor didn't find a portrait display (e.g. it's
    /// connected but still rotated landscape in Windows Display Settings), drag the window onto it
    /// and toggle this. Programmatic resize via AppWindow works regardless of the presenter's
    /// IsResizable=false (that flag only affects user edge-dragging). Guarded by <see cref="_isFilled"/>
    /// so this doesn't overwrite the real pre-fill position/size when the window opened already
    /// auto-filled — toggling FILL off must restore the original floating window, not the filled one.</summary>
    private void OnFillScreenChecked(object sender, RoutedEventArgs e)
    {
        if (!_isFilled)
        {
            _preFillPosition = AppWindow.Position;
            _preFillSize = AppWindow.Size;
        }

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        AppWindow.MoveAndResize(displayArea.OuterBounds);
        _isFilled = true;
    }

    private void OnFillScreenUnchecked(object sender, RoutedEventArgs e)
    {
        AppWindow.MoveAndResize(new RectInt32(_preFillPosition.X, _preFillPosition.Y, _preFillSize.Width, _preFillSize.Height));
        _isFilled = false;
    }

    /// <summary>SIZE — cycles the window down (then back up) through PortraitSizing's presets and
    /// centres it on the display it's on, so shrinking to a corner panel is one click instead of
    /// an edge-drag. The nearest preset is re-derived from the live width each click, so the cycle
    /// stays honest after a manual edge-resize. FILL and a preset size are mutually exclusive:
    /// unchecking FILL restores the old floating bounds first, which the preset resize below then
    /// immediately replaces — two moves in the same tick, no visible intermediate. The floating
    /// bounds are re-pointed at the preset so a later FILL-off restores the size the user picked,
    /// not the one the window opened with.</summary>
    private void OnSizeCycleClick(object sender, RoutedEventArgs e)
    {
        var display = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var bounds = display.OuterBounds;

        var step = PortraitSizing.NextStep(PortraitSizing.NearestStep(AppWindow.Size.Width));
        var (width, height) = PortraitSizing.SizeForStep(step, bounds.Width, bounds.Height);

        if (_isFilled)
        {
            FillScreenToggle.IsChecked = false; // fires OnFillScreenUnchecked → _isFilled = false
        }

        var x = bounds.X + (bounds.Width - width) / 2;
        var y = bounds.Y + Math.Max(0, (bounds.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

        _preFillPosition = new PointInt32(x, y);
        _preFillSize = new SizeInt32(width, height);
        SizeCycleButton.Content = $"{PortraitSizing.PercentForStep(step)}%";
    }

    /// <summary>MON — hops the panel to the next connected display at its current size, centred,
    /// instead of dragging it across the desktop. Indexes DisplayArea.FindAll with Count/[i] and
    /// no LINQ — the WinRT projection for that return type throws InvalidCastException on
    /// enumeration (see <see cref="FindPortraitDisplay"/> for the crash evidence). A FILLed window
    /// needs no special case here: the move changes position, OnAppWindowChanged resolves the new
    /// display and re-snaps it to that display's full bounds.</summary>
    private void OnMoveMonitorClick(object sender, RoutedEventArgs e)
    {
        var displays = DisplayArea.FindAll();
        if (displays.Count < 2)
        {
            return; // single display — nowhere to hop to
        }

        var current = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        var currentIndex = 0;
        for (var i = 0; i < displays.Count; i++)
        {
            var bounds = displays[i].OuterBounds;
            var currentBounds = current.OuterBounds;
            if (bounds.X == currentBounds.X && bounds.Y == currentBounds.Y &&
                bounds.Width == currentBounds.Width && bounds.Height == currentBounds.Height)
            {
                currentIndex = i;
                break;
            }
        }

        var target = displays[PortraitSizing.NextDisplay(currentIndex, displays.Count)];
        var targetBounds = target.OuterBounds;

        // Keep the current size; only cap it if the target display is genuinely smaller.
        var width = Math.Min(AppWindow.Size.Width, targetBounds.Width);
        var height = Math.Min(AppWindow.Size.Height, targetBounds.Height);
        var x = targetBounds.X + Math.Max(0, (targetBounds.Width - width) / 2);
        var y = targetBounds.Y + Math.Max(0, (targetBounds.Height - height) / 2);
        AppWindow.MoveAndResize(new RectInt32(x, y, width, height));

        if (!_isFilled)
        {
            // Same reason as SIZE: FILL-off must restore the window as it looked on the last
            // display, not as it opened.
            _preFillPosition = new PointInt32(x, y);
            _preFillSize = new SizeInt32(width, height);
        }
    }

    /// <summary>Header breathability as the window shrinks: below ~560px the title label goes
    /// (clock stays — it's the point of a stats panel), below ~420px the date line goes too, so
    /// five header buttons + time always fit on one line even at the 50% preset. Re-fires when the
    /// collapsing changes the root's own height, but the values are idempotent, so it settles.</summary>
    private void OnRootGridSizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        HeaderText.Visibility = width >= 560 ? Visibility.Visible : Visibility.Collapsed;
        DateText.Visibility = width >= 420 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnFanNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PortraitFanRow fan } textBox)
        {
            ViewModel.RenameFan(fan.FanId, textBox.Text.Trim());
        }
    }
}
