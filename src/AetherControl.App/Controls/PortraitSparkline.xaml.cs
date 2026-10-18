using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.Foundation;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>Lightweight history chart for a 0-100 percent series, ported from Portrait Stats'
/// <c>Sparkline</c>.</summary>
public sealed partial class PortraitSparkline : UserControl
{
    private const double MaxValue = 100.0;

    private Storyboard? _pulse;
    private bool _hovered;

    public PortraitSparkline()
    {
        InitializeComponent();
        Loaded += (_, _) => StartPulse();
        Unloaded += (_, _) => StopPulse();
    }

    /// <summary>Gentle pulse on the last-point dot so the live end of the trace draws the eye.
    /// Skipped entirely under reduced motion; stopped on unload so a dead control can't keep a
    /// storyboard ticking.</summary>
    private void StartPulse()
    {
        StopPulse();
        if (!Theming.Motion.AnimationsEnabled)
        {
            return;
        }

        var anim = new DoubleAnimation
        {
            From = 1.0,
            To = 0.35,
            Duration = Theming.Motion.ValueChange,
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, EndpointDot);
        Storyboard.SetTargetProperty(anim, "Opacity");
        _pulse = new Storyboard();
        _pulse.Children.Add(anim);
        _pulse.Begin();
    }

    private void StopPulse()
    {
        _pulse?.Stop();
        _pulse = null;
        EndpointDot.Opacity = 1.0;
    }

    private void OnPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hovered = true;
        UpdateRangeLines();
    }

    private void OnPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _hovered = false;
        MaxLine.Visibility = Visibility.Collapsed;
        MinLine.Visibility = Visibility.Collapsed;
    }

    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IReadOnlyList<double>), typeof(PortraitSparkline),
        new PropertyMetadata(Array.Empty<double>(), (d, _) => ((PortraitSparkline)d).Redraw()));

    public IReadOnlyList<double> Values
    {
        get => (IReadOnlyList<double>)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(PortraitSparkline), new PropertyMetadata(null, OnStrokeChanged));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    private static void OnStrokeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (PortraitSparkline)d;
        self.LinePolyline.Stroke = self.Stroke;
        self.EndpointDot.Fill = self.Stroke;

        // A gradient fill fading to transparent reads as "area under the curve"; a flat low-alpha
        // fill just reads as a dim rectangle.
        if (self.Stroke is SolidColorBrush solid)
        {
            var top = Color.FromArgb(90, solid.Color.R, solid.Color.G, solid.Color.B);
            var bottom = Color.FromArgb(0, solid.Color.R, solid.Color.G, solid.Color.B);
            self.FillPolygon.Fill = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops =
                {
                    new GradientStop { Color = top, Offset = 0 },
                    new GradientStop { Color = bottom, Offset = 1 }
                }
            };
        }
        else
        {
            self.FillPolygon.Fill = self.Stroke;
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var values = Values;
        var width = ActualWidth;
        var height = ActualHeight;

        if (values.Count < 2 || width <= 0 || height <= 0)
        {
            LinePolyline.Points.Clear();
            FillPolygon.Points.Clear();
            EndpointDot.Visibility = Visibility.Collapsed;
            return;
        }

        var points = new PointCollection();
        var step = width / (values.Count - 1);

        for (var i = 0; i < values.Count; i++)
        {
            var normalized = Math.Clamp(values[i] / MaxValue, 0.0, 1.0);
            var x = i * step;
            var y = height - normalized * height;
            points.Add(new Point(x, y));
        }

        LinePolyline.Points = points;

        var fillPoints = new PointCollection();
        foreach (var p in points)
        {
            fillPoints.Add(p);
        }

        fillPoints.Add(new Point(width, height));
        fillPoints.Add(new Point(0, height));
        FillPolygon.Points = fillPoints;

        var last = points[^1];
        EndpointDot.Margin = new Thickness(last.X - EndpointDot.Width / 2, last.Y - EndpointDot.Height / 2, 0, 0);
        EndpointDot.Visibility = Visibility.Visible;
        UpdateRangeLines();
    }

    private void UpdateRangeLines()
    {
        if (!_hovered || Values.Count < 2 || ActualHeight <= 0)
        {
            return;
        }

        var min = double.MaxValue;
        var max = double.MinValue;
        foreach (var v in Values)
        {
            if (v < min)
            {
                min = v;
            }

            if (v > max)
            {
                max = v;
            }
        }

        // Same normalisation as the trace itself, so the hairlines sit exactly on the extremes.
        MaxLine.Margin = new Thickness(0, ActualHeight - Math.Clamp(max / MaxValue, 0.0, 1.0) * ActualHeight, 0, 0);
        MinLine.Margin = new Thickness(0, ActualHeight - Math.Clamp(min / MaxValue, 0.0, 1.0) * ActualHeight, 0, 0);
        MaxLine.Visibility = Visibility.Visible;
        // A flat line has no range worth marking twice.
        MinLine.Visibility = max - min > 0.5 ? Visibility.Visible : Visibility.Collapsed;
    }
}
