using System;

namespace AetherControl.Core.Layout;

/// <summary>
/// Size presets and display-cycling maths for Portrait Mode — the "shrink it" and "hop it to the
/// other monitor" buttons live on these, kept in Core (ints only, no WinRT types) so the behaviour
/// is unit-testable the same way <see cref="WindowScale"/> and <see cref="FlowLayout"/> are.
/// <para>
/// Presets are width fractions of 768×1366 — Portrait Stats' own fixed size, which is also this
/// window's floating fallback size — so the cycle is relative to a size the layout was designed
/// for rather than to whatever display the window happens to be on. A step that would overshoot
/// the current display is fitted down (aspect preserved) instead of spilling off-screen.
/// </para>
/// </summary>
public static class PortraitSizing
{
    public const int BaseWidth = 768;
    public const int BaseHeight = 1366;

    /// <summary>Full size, then three shrink steps. Cycling wraps: 100 → 80 → 65 → 50 → 100.</summary>
    public static readonly double[] Steps = [1.00, 0.80, 0.65, 0.50];

    /// <summary>Never shrink narrower than this while a display allows it — below ~340px the
    /// header (title + clock + five buttons) stops fitting on one line even with the adaptive
    /// collapse in PortraitWindow.</summary>
    public const int MinWidth = 340;

    /// <summary>Wraps to the first step after the last one.</summary>
    public static int NextStep(int step)
    {
        var clamped = Math.Clamp(step, 0, Steps.Length - 1);
        return (clamped + 1) % Steps.Length;
    }

    /// <summary>Which preset a live window width is closest to. Used so the cycle button stays
    /// sensible after the user edge-resizes by hand, and after FILL (which sets the width to a
    /// whole display's) — a width past the full preset clamps to step 0.</summary>
    public static int NearestStep(double currentWidth)
    {
        if (!double.IsFinite(currentWidth) || currentWidth <= 0)
        {
            return 0;
        }

        var best = 0;
        var bestDistance = double.PositiveInfinity;
        for (var i = 0; i < Steps.Length; i++)
        {
            var distance = Math.Abs(currentWidth - BaseWidth * Steps[i]);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>Pixel size for a preset, fitted to the display it will sit on: never larger than
    /// the target display, aspect preserved (up to rounding), and never narrower than
    /// <see cref="MinWidth"/> unless the display itself is narrower.</summary>
    public static (int Width, int Height) SizeForStep(int step, int maxWidth, int maxHeight)
    {
        if (maxWidth <= 0 || maxHeight <= 0)
        {
            return (Math.Max(maxWidth, 0), Math.Max(maxHeight, 0));
        }

        var clamped = Math.Clamp(step, 0, Steps.Length - 1);
        var width = BaseWidth * Steps[clamped];
        var height = BaseHeight * Steps[clamped];

        var fit = Math.Min(1.0, Math.Min(maxWidth / width, maxHeight / height));
        var fittedWidth = Math.Max(1, (int)Math.Round(width * fit));
        var fittedHeight = Math.Max(1, (int)Math.Round(height * fit));

        // Keep the "not narrower than MinWidth" floor only while the display can afford it.
        fittedWidth = Math.Max(fittedWidth, Math.Min(MinWidth, maxWidth));
        return (fittedWidth, fittedHeight);
    }

    /// <summary>Label for a preset — "100%", "80%", "65%", "50%".</summary>
    public static int PercentForStep(int step) =>
        (int)Math.Round(Steps[Math.Clamp(step, 0, Steps.Length - 1)] * 100);

    /// <summary>Index of the display to hop to next, wrapping at the end. A single display (or a
    /// degenerate count) has nowhere to go, so it returns the current index unchanged.</summary>
    public static int NextDisplay(int currentIndex, int displayCount)
    {
        if (displayCount <= 1)
        {
            return 0;
        }

        var normalized = ((currentIndex % displayCount) + displayCount) % displayCount;
        return (normalized + 1) % displayCount;
    }
}
