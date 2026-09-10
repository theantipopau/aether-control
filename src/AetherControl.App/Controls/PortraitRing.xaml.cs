using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>Circular progress ring for a 0-100 percent value, ported from Portrait Stats'
/// <c>RadialGauge</c> (renamed to avoid clashing with the main app's own <c>RadialGauge</c>,
/// which is a different, text-labelled 270° arc design — Portrait Mode's ring is deliberately
/// minimal, a full 360° track with the number shown separately beside it).</summary>
public sealed partial class PortraitRing : UserControl
{
    public PortraitRing()
    {
        InitializeComponent();
    }

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(PortraitRing), new PropertyMetadata(0.0, (d, _) => ((PortraitRing)d).Redraw()));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(PortraitRing), new PropertyMetadata(null, (d, _) => ((PortraitRing)d).Redraw()));

    public Brush? Stroke
    {
        get => (Brush?)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public static readonly DependencyProperty RingThicknessProperty = DependencyProperty.Register(
        nameof(RingThickness), typeof(double), typeof(PortraitRing), new PropertyMetadata(7.0, (d, _) => ((PortraitRing)d).Redraw()));

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => Redraw();

    private void Redraw()
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0)
        {
            return;
        }

        var thickness = RingThickness;
        var diameter = size - thickness;
        var radius = diameter / 2;
        var center = new Point(size / 2, size / 2);

        TrackEllipse.Width = diameter;
        TrackEllipse.Height = diameter;
        TrackEllipse.StrokeThickness = thickness;

        IndicatorPath.StrokeThickness = thickness;
        TipDot.Width = thickness * 1.4;
        TipDot.Height = thickness * 1.4;

        var percent = Math.Clamp(Value, 0.0, 100.0);
        if (percent <= 0.01)
        {
            IndicatorPath.Data = null;
            TipDot.Visibility = Visibility.Collapsed;
            return;
        }

        // Full circle can't be expressed as a single arc segment (degenerate start==end), so nudge
        // just shy of 360 to keep the ring visually solid at 100%.
        var sweepDegrees = Math.Min(percent / 100.0 * 360.0, 359.9);

        var start = PointOnCircle(center, radius, 0);
        var end = PointOnCircle(center, radius, sweepDegrees);
        var isLargeArc = sweepDegrees > 180;

        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = end,
            Size = new Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        IndicatorPath.Data = geometry;

        // A flat stroke reads as decorative; a gradient sweeping from a pale tint at the start to
        // the full accent at the tip reads as an active meter filling up.
        if (Stroke is SolidColorBrush { Color: var color })
        {
            IndicatorPath.Stroke = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 1),
                GradientStops =
                {
                    new GradientStop { Color = Lighten(color, 0.55), Offset = 0 },
                    new GradientStop { Color = color, Offset = 1 }
                }
            };
            TipDot.Fill = new SolidColorBrush(color);
            TipDot.Visibility = Visibility.Visible;
            TipDot.Margin = new Thickness(end.X - TipDot.Width / 2, end.Y - TipDot.Height / 2, 0, 0);
        }
        else
        {
            IndicatorPath.Stroke = Stroke;
            TipDot.Visibility = Visibility.Collapsed;
        }
    }

    private static Color Lighten(Color c, double amount)
    {
        byte Mix(byte channel) => (byte)(channel + (255 - channel) * amount);
        return Color.FromArgb(c.A, Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static Point PointOnCircle(Point center, double radius, double angleDegrees)
    {
        var radians = (angleDegrees - 90) * Math.PI / 180.0;
        return new Point(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
    }
}
