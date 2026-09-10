using AetherControl.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace AetherControl.App.Controls;

/// <summary>Small vendor wordmark pill (text, not logo artwork), ported from Portrait Stats'
/// <c>VendorBadge</c>.</summary>
public sealed partial class PortraitVendorBadge : UserControl
{
    public PortraitVendorBadge()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public static readonly DependencyProperty VendorProperty = DependencyProperty.Register(
        nameof(Vendor), typeof(PortraitVendor), typeof(PortraitVendorBadge), new PropertyMetadata(PortraitVendor.None, OnVendorChanged));

    public PortraitVendor Vendor
    {
        get => (PortraitVendor)GetValue(VendorProperty);
        set => SetValue(VendorProperty, value);
    }

    private static void OnVendorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((PortraitVendorBadge)d).Refresh();

    private void Refresh()
    {
        var (text, brushKey) = Vendor switch
        {
            PortraitVendor.Amd => ("AMD", "PortraitAmdBrandBrush"),
            PortraitVendor.Nvidia => ("NVIDIA", "PortraitNvidiaBrandBrush"),
            PortraitVendor.Intel => ("INTEL", "PortraitIntelBrandBrush"),
            _ => (null, null)
        };

        if (text is null)
        {
            Root.Visibility = Visibility.Collapsed;
            return;
        }

        var brush = (Brush)Application.Current.Resources[brushKey!];
        Root.Visibility = Visibility.Visible;
        Root.BorderBrush = brush;
        Label.Text = text;
        Label.Foreground = brush;
    }
}
