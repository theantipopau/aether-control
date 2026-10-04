using System;
using AetherControl.Core.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace AetherControl.App.Controls;

/// <summary>
/// Window-size-dependent type scale: proportional page zoom driven by the page width.
/// WinUI 3 in this project has no LayoutTransform, so the scale is applied as a RenderTransform on
/// the page's ScrollViewer while its layout Width/Height are divided by the same factor — the
/// scroller lays content out in a viewport of (pageWidth / scale), then renders it exactly back to
/// the page's real size. That keeps scroll extents correct (everything inside the scroller scales
/// together) while text, spacing and cards all grow on a maximised window and step down before a
/// narrow one crowds them.
/// <see cref="WindowScale.ForWidth"/> quantises the factor, and unchanged factors never touch the
/// transform — a resize drag inside one band is a single size assignment per event at most.
/// Set from code on SizeChanged, never from markup (FluidWrapGrid's attached-DP lesson).
/// </summary>
public static class ResponsiveScale
{
    public static void Apply(ScrollViewer scroller, double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0 || !double.IsFinite(pageWidth) || !double.IsFinite(pageHeight))
        {
            return;
        }

        var scale = WindowScale.ForWidth(pageWidth);

        // Viewport in unscaled units — content lays out against this, then the transform renders
        // it back over the real page rectangle.
        scroller.Width = pageWidth / scale;
        scroller.Height = pageHeight / scale;
        scroller.HorizontalAlignment = HorizontalAlignment.Left;
        scroller.VerticalAlignment = VerticalAlignment.Top;

        if (scroller.RenderTransform is not ScaleTransform existing ||
            Math.Abs(existing.ScaleX - scale) > 0.001 ||
            Math.Abs(existing.ScaleY - scale) > 0.001)
        {
            scroller.RenderTransform = new ScaleTransform { ScaleX = scale, ScaleY = scale };
            scroller.RenderTransformOrigin = new Point(0, 0);
        }
    }
}
