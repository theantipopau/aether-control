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
/// and the Portrait* controls), sized to match a real rotated-portrait monitor (768x1366, the same
/// fixed size Portrait Stats itself used).
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
        // monitor reports once rotated in Windows Display Settings.
        AppWindow.Resize(new SizeInt32(768, 1366));

        Closed += OnClosed;
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

    /// <summary>Resizes and repositions to fill whatever monitor the window is currently on —
    /// drag it onto the portrait monitor first (a fixed 768x1366 window won't automatically match
    /// every monitor's real resolution), then toggle this to fill it exactly. Programmatic resize
    /// via AppWindow works regardless of the presenter's IsResizable=false (that flag only affects
    /// user edge-dragging). Remembers the pre-fill position/size so toggling off restores it —
    /// otherwise "maximise" would be a one-way trip with no way back short of closing the window.</summary>
    private void OnFillScreenChecked(object sender, RoutedEventArgs e)
    {
        _preFillPosition = AppWindow.Position;
        _preFillSize = AppWindow.Size;

        var displayArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest);
        AppWindow.MoveAndResize(displayArea.OuterBounds);
    }

    private void OnFillScreenUnchecked(object sender, RoutedEventArgs e) =>
        AppWindow.MoveAndResize(new RectInt32(_preFillPosition.X, _preFillPosition.Y, _preFillSize.Width, _preFillSize.Height));

    private void OnFanNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { DataContext: PortraitFanRow fan } textBox)
        {
            ViewModel.RenameFan(fan.FanId, textBox.Text.Trim());
        }
    }
}
