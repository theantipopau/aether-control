using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AetherControl.App.Controls;

/// <summary>A section heading with a small diagonal accent mark, echoing the angled "speed line" motif on Armoury Crate's own section headers.</summary>
public sealed partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(SectionHeader), new PropertyMetadata(string.Empty, OnLabelChanged));

    public SectionHeader()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    private static void OnLabelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((SectionHeader)d).LabelText.Text = (string)e.NewValue;
}
