using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace AetherControl.App.Controls;

/// <summary>A section heading with a small diagonal accent mark, echoing the angled "speed line" motif on Armoury Crate's own section headers.</summary>
[ContentProperty(Name = nameof(Action))]
public sealed partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty LogoProperty =
        DependencyProperty.Register(nameof(Logo), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, OnLogoChanged));

    public static readonly DependencyProperty ActionProperty =
        DependencyProperty.Register(nameof(Action), typeof(object), typeof(SectionHeader), new PropertyMetadata(null, OnActionChanged));

    public SectionHeader()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>ms-appx URI of a vendor logo; empty collapses the image entirely.</summary>
    public string Logo
    {
        get => (string)GetValue(LogoProperty);
        set => SetValue(LogoProperty, value);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectionHeader)d).LabelText.Text = (string)e.NewValue;

    private static void OnLogoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectionHeader)d).LogoElement.SourceUri = (string)e.NewValue;

    /// <summary>Optional right-aligned action content (a button, a StatusPill). Null (the default)
    /// leaves the slot empty and the header renders exactly as before.</summary>
    public object? Action
    {
        get => GetValue(ActionProperty);
        set => SetValue(ActionProperty, value);
    }

    private static void OnActionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectionHeader)d).ActionHost.Content = e.NewValue;
}
