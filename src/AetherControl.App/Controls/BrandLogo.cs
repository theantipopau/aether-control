using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace AetherControl.App.Controls;

/// <summary>
/// Vendor logo image (AMD, NVIDIA, ASUS, … white silhouettes from Assets/Vendors). Collapses
/// entirely when <see cref="SourceUri"/> is empty, so sections whose hardware brand is unknown
/// show nothing rather than a broken image. Source is set from code on the DP change instead of
/// a markup x:Bind/attached-property path — that path is what failed inside InitializeComponent
/// for FluidWrapGrid (see FluidPanel's history comment).
/// </summary>
public sealed class BrandLogo : UserControl
{
    public static readonly DependencyProperty SourceUriProperty = DependencyProperty.Register(
        nameof(SourceUri), typeof(string), typeof(BrandLogo), new PropertyMetadata(string.Empty, OnSourceUriChanged));

    private readonly Image _image;

    public BrandLogo()
    {
        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = _image;
        Visibility = Visibility.Collapsed;
    }

    public string SourceUri
    {
        get => (string)GetValue(SourceUriProperty);
        set => SetValue(SourceUriProperty, value);
    }

    private static void OnSourceUriChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((BrandLogo)d).ApplySource(e.NewValue as string);

    private void ApplySource(string? sourceUri)
    {
        if (string.IsNullOrWhiteSpace(sourceUri))
        {
            _image.Source = null;
            Visibility = Visibility.Collapsed;
            return;
        }

        _image.Source = new BitmapImage(new Uri(sourceUri, UriKind.Absolute));
        Visibility = Visibility.Visible;
    }
}
