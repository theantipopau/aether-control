using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>
/// A single dashboard/portrait metric tile: label, big value, unit, an
/// optional tone-coloured accent stripe + meter bar, and an optional
/// trailing icon glyph. Two ways to feed it a value: <see cref="Value"/> for
/// plain text (status strings, IPs) set directly with no animation, and
/// <see cref="NumericValue"/> (with <see cref="Format"/>, e.g. "F0"/"F1")
/// for live sensor readings, which glide from the old number to the new one
/// via <see cref="NumberTween"/> instead of snapping.
/// </summary>
public sealed partial class MetricCard : UserControl
{
    // Assigned once per instance — the correlation id UiValueTraceLog uses to tell "the same card,
    // updated repeatedly" apart from "a freshly recreated card at the same grid position", which is
    // exactly the distinction a container-recycling bug (a stale card bound to the wrong volume,
    // or a fresh card's tween restarting from zero) would show up as in the trace.
    private readonly Guid _instanceId = Guid.NewGuid();
    private readonly NumberTween _valueTween;
    private readonly NumberTween _progressTween;
    private string _format = "F0";

    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty, OnValueChanged));

    public static readonly DependencyProperty UnitProperty =
        DependencyProperty.Register(nameof(Unit), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty, OnUnitChanged));

    public static readonly DependencyProperty NumericValueProperty =
        DependencyProperty.Register(nameof(NumericValue), typeof(double), typeof(MetricCard), new PropertyMetadata(double.NaN, OnNumericValueChanged));

    public static readonly DependencyProperty FormatProperty =
        DependencyProperty.Register(nameof(Format), typeof(string), typeof(MetricCard), new PropertyMetadata("F0", OnFormatChanged));

    public static readonly DependencyProperty ProgressProperty =
        DependencyProperty.Register(nameof(Progress), typeof(double), typeof(MetricCard), new PropertyMetadata(0.0, OnProgressChanged));

    public static readonly DependencyProperty ToneProperty =
        DependencyProperty.Register(nameof(Tone), typeof(MetricTone), typeof(MetricCard), new PropertyMetadata(MetricTone.Neutral, OnToneChanged));

    public static readonly DependencyProperty IconGlyphProperty =
        DependencyProperty.Register(nameof(IconGlyph), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty, OnIconGlyphChanged));

    public static readonly DependencyProperty DetailProperty =
        DependencyProperty.Register(nameof(Detail), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty, OnDetailChanged));

    public static readonly DependencyProperty CardWidthProperty =
        DependencyProperty.Register(nameof(CardWidth), typeof(double), typeof(MetricCard), new PropertyMetadata(double.NaN, OnCardWidthChanged));

    /// <summary>Opt-in correlation tag (e.g. a drive's DeviceId) — when non-empty, this instance
    /// participates in the temporary storage-flicker diagnostics (see <see cref="StorageDiagnostics"/>):
    /// it traces every NumericValue change when <see cref="StorageDiagnostics.TraceEnabled"/> is set,
    /// and bypasses NumberTween entirely when <see cref="StorageDiagnostics.AnimationEnabled"/> is
    /// false. Empty (the default, and every non-storage card in the app) is completely unaffected by
    /// either flag — this only ever touches cards that explicitly opt in.</summary>
    public static readonly DependencyProperty DiagnosticTagProperty =
        DependencyProperty.Register(nameof(DiagnosticTag), typeof(string), typeof(MetricCard), new PropertyMetadata(string.Empty));

    public MetricCard()
    {
        InitializeComponent();
        _valueTween = new NumberTween(v =>
        {
            var text = v.ToString(_format);
            ValueText.Text = text;
            if (StorageDiagnostics.TraceEnabled && !string.IsNullOrEmpty(DiagnosticTag))
            {
                UiValueTraceLog.Write(DiagnosticTag, _instanceId, $"TextBlock.Text set: frameValue={v:R} text=\"{text}\"");
            }
        });
        _progressTween = new NumberTween(UpdateMeterFill);

        HoverBorderEffect.Attach(this, RootBorder);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>Plain text display — set directly, no transition. Use for status text/IPs, not sensor readings.</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    /// <summary>Live numeric reading — animates from the previous value to this one instead of snapping.</summary>
    public double NumericValue
    {
        get => (double)GetValue(NumericValueProperty);
        set => SetValue(NumericValueProperty, value);
    }

    /// <summary>.NET numeric format string applied to <see cref="NumericValue"/> on every animation frame (default "F0").</summary>
    public string Format
    {
        get => (string)GetValue(FormatProperty);
        set => SetValue(FormatProperty, value);
    }

    /// <summary>0-100 fill for the bottom meter bar. Defaults to 0 (an empty but visible track) so every card in a row stays the same height.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public MetricTone Tone
    {
        get => (MetricTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    /// <summary>Segoe Fluent Icons glyph shown top-right (e.g. ""). Empty hides the icon.</summary>
    public string IconGlyph
    {
        get => (string)GetValue(IconGlyphProperty);
        set => SetValue(IconGlyphProperty, value);
    }

    /// <summary>Optional small secondary line below the value (e.g. a drive's health status). Empty hides it.</summary>
    public string Detail
    {
        get => (string)GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    /// <summary>Overrides the card's default 160px width — e.g. storage cards need more room for a model name. NaN (the default) leaves the XAML-declared width alone.</summary>
    public double CardWidth
    {
        get => (double)GetValue(CardWidthProperty);
        set => SetValue(CardWidthProperty, value);
    }

    public string DiagnosticTag
    {
        get => (string)GetValue(DiagnosticTagProperty);
        set => SetValue(DiagnosticTagProperty, value);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d).LabelText.Text = (string)e.NewValue;

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d).ValueText.Text = (string)e.NewValue;

    private static void OnUnitChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        var unit = (string)e.NewValue;
        card.UnitText.Text = unit;
        card.UnitText.Visibility = string.IsNullOrEmpty(unit) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnFormatChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d)._format = (string)e.NewValue;

    private static void OnNumericValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        var newValue = (double)e.NewValue;
        var isDiagnosticCard = !string.IsNullOrEmpty(card.DiagnosticTag);

        if (isDiagnosticCard && StorageDiagnostics.TraceEnabled)
        {
            UiValueTraceLog.Write(card.DiagnosticTag, card._instanceId,
                $"NumericValue changed: raw={newValue:R} formatted=\"{FormatSafely(newValue, card._format)}\" animationEnabled={StorageDiagnostics.AnimationEnabled} tweenCurrentBefore={card._valueTween.Current:R}");
        }

        if (isDiagnosticCard && !StorageDiagnostics.AnimationEnabled)
        {
            // Diagnostic bypass: write the formatted value straight to the TextBlock, no
            // interpolation, no NumberTween involved at all — isolates the animation layer as a
            // variable. If a reported "wrong value" defect disappears with this off, the defect is
            // somewhere in NumberTween/TweenState; if it persists, it's upstream (binding, converter,
            // model, or card identity) and this bypass proves the animation layer innocent.
            card.ValueText.Text = FormatSafely(newValue, card._format);
            return;
        }

        card._valueTween.AnimateTo(newValue);
    }

    private static string FormatSafely(double value, string format) =>
        double.IsFinite(value) ? value.ToString(format) : "—";

    private static void OnProgressChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d)._progressTween.AnimateTo((double)e.NewValue);

    private static void OnIconGlyphChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        var glyph = (string)e.NewValue;
        card.IconElement.Glyph = glyph;
        card.IconElement.Visibility = string.IsNullOrEmpty(glyph) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnToneChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((MetricCard)d).ApplyTone((MetricTone)e.NewValue);

    private static void OnDetailChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var card = (MetricCard)d;
        var detail = (string)e.NewValue;
        card.DetailText.Text = detail;
        card.DetailText.Visibility = string.IsNullOrEmpty(detail) ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void OnCardWidthChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var width = (double)e.NewValue;
        if (!double.IsNaN(width))
        {
            ((MetricCard)d).RootBorder.Width = width;
        }
    }

    private void ApplyTone(MetricTone tone)
    {
        var brush = tone switch
        {
            MetricTone.Warm => (Brush)Application.Current.Resources["AetherWarmBrush"],
            MetricTone.Hot => (Brush)Application.Current.Resources["AetherHotBrush"],
            MetricTone.Cool => (Brush)Application.Current.Resources["AetherAccentBrush"],
            _ => (Brush)Application.Current.Resources["AetherBorderBrush"]
        };

        AccentStripe.Background = brush;
        MeterFillBar.Background = brush;

        ValueText.Foreground = tone == MetricTone.Neutral
            ? (Brush)Application.Current.Resources["AetherTextPrimaryBrush"]
            : brush;
    }

    private void UpdateMeterFill(double percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        MeterFillColumn.Width = new GridLength(clamped, GridUnitType.Star);
        MeterRemainderColumn.Width = new GridLength(100 - clamped, GridUnitType.Star);
    }
}
