using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AetherControl.App.Controls;

/// <summary>
/// A plain <see cref="CardBorderStyle"/> border with arbitrary content and
/// the same hover-brightens-to-accent feedback as <see cref="MetricCard"/>
/// (via the shared <see cref="HoverBorderEffect"/>) — for the list-style rows
/// on Optimisation/RGB/Firmware/Device Utilities that don't fit MetricCard's
/// label/value/unit shape but should still feel like part of the same app.
/// </summary>
[ContentProperty(Name = nameof(CardContent))]
public sealed partial class HoverCard : UserControl
{
    public static readonly DependencyProperty CardContentProperty =
        DependencyProperty.Register(nameof(CardContent), typeof(object), typeof(HoverCard), new PropertyMetadata(null, OnCardContentChanged));

    public HoverCard()
    {
        InitializeComponent();
        HoverBorderEffect.Attach(this, RootBorder);
    }

    public object CardContent
    {
        get => GetValue(CardContentProperty);
        set => SetValue(CardContentProperty, value);
    }

    private static void OnCardContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((HoverCard)d).ContentHost.Content = e.NewValue;
}
