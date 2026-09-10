using AetherControl.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>Ported from Portrait Stats' <c>MetricTile</c> — a labelled value card with an
/// optional thin severity-coloured progress bar. The original's WPF <c>ProgressBar</c> template
/// override is replaced here with a plain two-Border track/indicator pair (avoids fighting
/// WinUI3's own ProgressBar template for a simple rounded bar).</summary>
public sealed partial class PortraitMetricTile : UserControl
{
    public PortraitMetricTile()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(PortraitMetricTile), new PropertyMetadata(string.Empty, OnAnyChanged));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(string), typeof(PortraitMetricTile), new PropertyMetadata("--", OnAnyChanged));

    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(double), typeof(PortraitMetricTile), new PropertyMetadata(0.0, OnAnyChanged));

    public double Percent
    {
        get => (double)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public static readonly DependencyProperty SeverityProperty = DependencyProperty.Register(
        nameof(Severity), typeof(PortraitSeverity), typeof(PortraitMetricTile), new PropertyMetadata(PortraitSeverity.Normal, OnAnyChanged));

    public PortraitSeverity Severity
    {
        get => (PortraitSeverity)GetValue(SeverityProperty);
        set => SetValue(SeverityProperty, value);
    }

    public static readonly DependencyProperty AccentProperty = DependencyProperty.Register(
        nameof(Accent), typeof(Brush), typeof(PortraitMetricTile), new PropertyMetadata(null, OnAnyChanged));

    public Brush? Accent
    {
        get => (Brush?)GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    public static readonly DependencyProperty HasBarProperty = DependencyProperty.Register(
        nameof(HasBar), typeof(bool), typeof(PortraitMetricTile), new PropertyMetadata(true, OnAnyChanged));

    public bool HasBar
    {
        get => (bool)GetValue(HasBarProperty);
        set => SetValue(HasBarProperty, value);
    }

    private static void OnAnyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PortraitMetricTile)d).Refresh();

    private void OnBarTrackSizeChanged(object sender, SizeChangedEventArgs e) => UpdateBarWidth();

    private void Refresh()
    {
        TitleText.Text = Title;
        ValueText.Text = Value;

        var color = Severity switch
        {
            PortraitSeverity.Critical => (Brush)Application.Current.Resources["PortraitSeverityCriticalBrush"],
            PortraitSeverity.Warning => (Brush)Application.Current.Resources["PortraitSeverityWarningBrush"],
            _ => Accent ?? (Brush)Application.Current.Resources["PortraitTextPrimaryBrush"]
        };

        ValueText.Foreground = color;
        BarIndicator.Background = color;
        BarTrack.Visibility = HasBar ? Visibility.Visible : Visibility.Collapsed;
        UpdateBarWidth();
    }

    private void UpdateBarWidth()
    {
        var trackWidth = BarTrack.ActualWidth;
        if (trackWidth <= 0)
        {
            return;
        }

        var fraction = Math.Clamp(Percent, 0, 100) / 100.0;
        BarIndicator.Width = trackWidth * fraction;
    }
}
