using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>
/// A 270°-sweep circular gauge, ported from Radium PCs Companion's SVG
/// <c>Gauge.tsx</c> (same coordinate system: a 100x100 viewport, arc from
/// 225° to 495°, centre 50,50, radius 37) but drawn with WinUI's
/// <see cref="PathGeometry"/>/<see cref="ArcSegment"/> instead of an SVG
/// path string. Reserved for a handful of headline metrics — see
/// DashboardPage's hero row — rather than every tile, since a wall of
/// circular gauges reads as busier, not more premium.
/// </summary>
public sealed partial class RadialGauge : UserControl
{
    private const double CenterX = 50;
    private const double CenterY = 50;
    private const double Radius = 37;
    private const double StartAngle = 225;
    private const double SweepAngle = 270;
    private static readonly double[] TickFractions = [0, 0.25, 0.5, 0.75, 1.0];

    private readonly NumberTween _tween;
    private readonly List<Ellipse> _ticks = [];
    private string _format = "F0";

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(RadialGauge), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(RadialGauge), new PropertyMetadata(string.Empty, OnUnitChanged));

    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.Register(nameof(Format), typeof(string), typeof(RadialGauge), new PropertyMetadata("F0", OnFormatChanged));

    public static readonly DependencyProperty MaxProperty =
        DependencyProperty.Register(nameof(Max), typeof(double), typeof(RadialGauge), new PropertyMetadata(100.0));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(RadialGauge), new PropertyMetadata(0.0, OnValueChanged));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(MetricTone), typeof(RadialGauge), new PropertyMetadata(MetricTone.Cool, OnToneChanged));

    public RadialGauge()
    {
        InitializeComponent();
        _tween = new NumberTween(OnTweenUpdate);

        TrackPath.Data = BuildArc(StartAngle, StartAngle + SweepAngle);
        BuildTicks();
        ApplyToneBrush(MetricTone.Cool);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    public double Max
    {
        get => (double)GetValue(MaxProperty);
        set => SetValue(MaxProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public MetricTone Tone
    {
        get => (MetricTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RadialGauge)d).LabelText.Text = (string)e.NewValue;

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RadialGauge)d).UnitText.Text = (string)e.NewValue;

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RadialGauge)d)._format = (string)e.NewValue;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RadialGauge)d)._tween.AnimateTo((double)e.NewValue);

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((RadialGauge)d).ApplyToneBrush((MetricTone)e.NewValue);

    private void OnTweenUpdate(double animatedValue)
    {
        ValueText.Text = animatedValue.ToString(_format);

        var pct = Math.Clamp(Max <= 0 ? 0 : animatedValue / Max, 0, 1);
        var fillEnd = StartAngle + SweepAngle * pct;

        FillPath.Data = pct > 0.015 ? BuildArc(StartAngle, fillEnd) : null;
        UpdateTicks(pct);
    }

    private void ApplyToneBrush(MetricTone tone)
    {
        var (start, end) = tone switch
        {
            MetricTone.Warm => (Color.FromArgb(255, 0xF5, 0xC8, 0x6B), Color.FromArgb(255, 0xFA, 0xD4, 0x82)),
            MetricTone.Hot => (Color.FromArgb(255, 0xFF, 0x6D, 0x6D), Color.FromArgb(255, 0xFF, 0x99, 0x99)),
            _ => (Color.FromArgb(255, 0x55, 0xD6, 0xFF), Color.FromArgb(255, 0x84, 0xF0, 0xC4))
        };

        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        FillPath.Stroke = brush;
    }

    private void BuildTicks()
    {
        foreach (var fraction in TickFractions)
        {
            var deg = StartAngle + SweepAngle * fraction;
            var point = PointOnCircle(deg, Radius + 6);
            var dot = new Ellipse
            {
                Width = 2.5,
                Height = 2.5,
                Fill = (Brush)Application.Current.Resources["AetherBorderBrush"]
            };
            Canvas.SetLeft(dot, point.X - 1.25);
            Canvas.SetTop(dot, point.Y - 1.25);
            ArcCanvas.Children.Add(dot);
            _ticks.Add(dot);
        }
    }

    private void UpdateTicks(double pct)
    {
        var accentBrush = (Brush)Application.Current.Resources["AetherAccentBrush"];
        var inactiveBrush = (Brush)Application.Current.Resources["AetherBorderBrush"];

        for (var i = 0; i < TickFractions.Length; i++)
        {
            _ticks[i].Fill = pct > 0.02 && TickFractions[i] <= pct + 0.01 ? accentBrush : inactiveBrush;
        }
    }

    private static PathGeometry BuildArc(double startDeg, double endDeg)
    {
        var isLargeArc = Math.Abs(endDeg - startDeg) > 180;
        var figure = new PathFigure { StartPoint = PointOnCircle(startDeg, Radius), IsClosed = false };
        figure.Segments.Add(new ArcSegment
        {
            Point = PointOnCircle(endDeg, Radius),
            Size = new Size(Radius, Radius),
            SweepDirection = SweepDirection.Clockwise,
            IsLargeArc = isLargeArc
        });

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    private static Point PointOnCircle(double degrees, double radius)
    {
        var radians = (degrees - 90) * Math.PI / 180;
        return new Point(CenterX + radius * Math.Cos(radians), CenterY + radius * Math.Sin(radians));
    }
}
