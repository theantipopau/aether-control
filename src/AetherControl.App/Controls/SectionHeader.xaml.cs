using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Controls;

/// <summary>A section heading with a small diagonal accent mark, echoing the angled "speed line" motif on Armoury Crate's own section headers.</summary>
public sealed partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, OnLabelChanged));

    public static readonly DependencyProperty LogoProperty =
        DependencyProperty.Register(nameof(Logo), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, OnLogoChanged));

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
}
