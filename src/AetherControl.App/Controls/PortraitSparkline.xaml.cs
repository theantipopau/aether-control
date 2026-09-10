using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>Lightweight history chart for a 0-100 percent series, ported from Portrait Stats'
/// <c>Sparkline</c>.</summary>
public sealed partial class PortraitSparkline : UserControl
{
    private const double MaxValue = 100.0;

    public PortraitSparkline()
    {
        InitializeComponent();
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
    }
}
