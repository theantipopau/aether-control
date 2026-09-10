using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.UI;

namespace AetherControl.App.Controls;

/// <summary>
/// Shared "brighten the border to the accent colour on hover" effect used by
/// both <see cref="MetricCard"/> and <see cref="HoverCard"/>, so every card
/// in the app gets the same feedback instead of only the dashboard's tiles.
/// Gives the target Border its own per-instance brush first — CardBorderStyle
/// points every card at the same shared BorderBrush resource, and animating
/// that directly would flash every card on the page at once.
/// </summary>
internal static class HoverBorderEffect
{
    private static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(150);

    public static void Attach(UIElement hoverSource, Border targetBorder)
    {
        var brush = new SolidColorBrush((Color)Application.Current.Resources["AetherBorderColor"]);
        targetBorder.BorderBrush = brush;

        hoverSource.PointerEntered += (_, _) => AnimateTo(brush, (Color)Application.Current.Resources["AetherAccentColor"]);
        hoverSource.PointerExited += (_, _) => AnimateTo(brush, (Color)Application.Current.Resources["AetherBorderColor"]);
    }

    private static void AnimateTo(SolidColorBrush brush, Color target)
    {
        var animation = new ColorAnimation
        {
            To = target,
            Duration = Duration,
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(animation, brush);
        Storyboard.SetTargetProperty(animation, "Color");

        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        storyboard.Begin();
    }
}
