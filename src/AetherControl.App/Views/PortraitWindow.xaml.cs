using AetherControl.App.ViewModels;
using AetherControl.Core.Interfaces;
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
/// in practice. Minimize/maximize are disabled via the presenter (this is a fixed-purpose utility
/// panel, not a normal app window); Close is intercepted to hide rather than destroy the window,
/// since <c>MainWindow</c> caches this instance and re-<c>Activate()</c>s it on next open.
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
            presenter.IsResizable = false;
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

        Closed += OnClosed;
    }

    /// <summary>Picks a connected display taller than it is wide — i.e. actually rotated to
    /// portrait in Windows Display Settings, not just guessed from this window's own fixed size.
    /// Prefers a non-primary one: a portrait panel is almost always a secondary accessory monitor,
    /// and auto-filling the user's actual primary/landscape display would be actively wrong.</summary>
    private static DisplayArea? FindPortraitDisplay()
    {
        var portraitDisplays = DisplayArea.FindAll()
            .Where(d => d.OuterBounds.Height > d.OuterBounds.Width)
            .ToList();

        return portraitDisplays.Count == 0
            ? null
            : portraitDisplays.FirstOrDefault(d => !d.IsPrimary) ?? portraitDisplays[0];
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

    private void OnFanNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PortraitFanRow fan } textBox)
        {
            ViewModel.RenameFan(fan.FanId, textBox.Text.Trim());
        }
    }
}
