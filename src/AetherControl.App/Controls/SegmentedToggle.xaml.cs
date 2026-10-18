using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace AetherControl.App.Controls;

/// <summary>
/// Two-state segmented pill control — Armoury Crate's own Windows/Silence/Performance row uses
/// exactly this shape for its mode switches, and the roadmap called out replacing the plain
/// ToggleSwitch on Gaming Profile with one. Deliberately built for the two-state case that exists
/// today rather than a generic N-option control for fan-curve presets that don't exist yet.
/// </summary>
public sealed partial class SegmentedToggle : UserControl
{
    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(
        nameof(IsOn), typeof(bool), typeof(SegmentedToggle), new PropertyMetadata(false, OnStateChanged));

    public static readonly DependencyProperty OffLabelProperty = DependencyProperty.Register(
        nameof(OffLabel), typeof(string), typeof(SegmentedToggle), new PropertyMetadata("Off", OnStateChanged));

    public static readonly DependencyProperty OnLabelProperty = DependencyProperty.Register(
        nameof(OnLabel), typeof(string), typeof(SegmentedToggle), new PropertyMetadata("On", OnStateChanged));

    public event EventHandler<bool>? Toggled;

    private readonly TranslateTransform _pillShift = new();

    public SegmentedToggle()
    {
        InitializeComponent();
        Pill.RenderTransform = _pillShift;
        Loaded += (_, _) => UpdateVisualState();
        SizeChanged += (_, _) => PlacePill(snap: true);
    }

    public bool IsOn
    {
        get => (bool)GetValue(IsOnProperty);
        set => SetValue(IsOnProperty, value);
    }

    public string OffLabel
    {
        get => (string)GetValue(OffLabelProperty);
        set => SetValue(OffLabelProperty, value);
    }

    public string OnLabel
    {
        get => (string)GetValue(OnLabelProperty);
        set => SetValue(OnLabelProperty, value);
    }

    private void OnOffClicked(object sender, RoutedEventArgs e) => SetIsOn(false);

    private void OnOnClicked(object sender, RoutedEventArgs e) => SetIsOn(true);

    private void SetIsOn(bool value)
    {
        if (IsOn == value)
        {
            return;
        }

        IsOn = value;
        Toggled?.Invoke(this, value);
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SegmentedToggle)d).UpdateVisualState();

    private void UpdateVisualState()
    {
        OffButton.Content = OffLabel;
        OnButton.Content = OnLabel;

        // Text only — the sliding pill behind carries the accent now, so both buttons stay
        // transparent and the selection reads as one moving surface, not two swapping fills.
        var onAccentText = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var secondaryText = (Brush)Application.Current.Resources["AetherTextSecondaryBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        OffButton.Background = transparent;
        OffButton.Foreground = IsOn ? secondaryText : onAccentText;
        OnButton.Background = transparent;
        OnButton.Foreground = IsOn ? onAccentText : secondaryText;

        PlacePill(snap: false);
    }

    /// <summary>Sizes the pill to the active button and glides it there. Snaps (no storyboard)
    /// before first layout, on resize, or under reduced motion — same rule as every other
    /// animation in the app: the value still changes, only the glide is skipped.</summary>
    private void PlacePill(bool snap)
    {
        var active = IsOn ? OnButton : OffButton;
        if (active.ActualWidth <= 0)
        {
            return; // before first layout — Loaded re-fires UpdateVisualState once sizes exist
        }

        Pill.Width = active.ActualWidth;
        var target = active.TransformToVisual(TrackGrid).TransformPoint(new Windows.Foundation.Point(0, 0)).X;

        if (snap || !Theming.Motion.AnimationsEnabled)
        {
            _pillShift.X = target;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = Theming.Motion.ModeChange,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(anim, _pillShift);
        Storyboard.SetTargetProperty(anim, "X");
        var storyboard = new Storyboard();
        storyboard.Children.Add(anim);
        storyboard.Begin();
    }
}
