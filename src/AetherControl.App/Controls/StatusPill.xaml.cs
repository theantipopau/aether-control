using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>Severity tier for a <see cref="StatusPill"/> — deliberately separate from
/// <see cref="MetricTone"/> (sensor severity) since pill states are about connection/mode.</summary>
public enum StatusPillState
{
    Neutral,
    Info,
    Ok,
    Warning,
    Critical
}

/// <summary>
/// Compact dot-plus-label status chip. Both dot and label follow the state brush from the shared
/// tokens; the text always names the state so it survives colour-blindness and High Contrast.
/// </summary>
public sealed partial class StatusPill : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(StatusPill), new PropertyMetadata(string.Empty, OnTextChanged));

    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(StatusPillState), typeof(StatusPill), new PropertyMetadata(StatusPillState.Neutral, OnStateChanged));

    public StatusPill()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public StatusPillState State
    {
        get => (StatusPillState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusPill)d).Label.Text = (string)e.NewValue;

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((StatusPill)d).ApplyState((StatusPillState)e.NewValue);

    private void ApplyState(StatusPillState state)
    {
        var key = state switch
        {
            StatusPillState.Info => "AetherAccentBrush",
            StatusPillState.Ok => "StatusNormalBrush",
            StatusPillState.Warning => "StatusWarningBrush",
            StatusPillState.Critical => "StatusCriticalBrush",
            _ => "TextSecondaryBrush"
        };
        var brush = (Brush)Application.Current.Resources[key];
        Dot.Fill = brush;
        Label.Foreground = brush;
    }
}
