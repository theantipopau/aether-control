using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

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

    public SegmentedToggle()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateVisualState();
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

        var accent = (Brush)Application.Current.Resources["AetherAccentBrush"];
        var onAccentText = (Brush)Application.Current.Resources["TextOnAccentFillColorPrimaryBrush"];
        var secondaryText = (Brush)Application.Current.Resources["AetherTextSecondaryBrush"];
        var transparent = new SolidColorBrush(Colors.Transparent);

        OffButton.Background = IsOn ? transparent : accent;
        OffButton.Foreground = IsOn ? secondaryText : onAccentText;
        OnButton.Background = IsOn ? accent : transparent;
        OnButton.Foreground = IsOn ? onAccentText : secondaryText;
    }
}
