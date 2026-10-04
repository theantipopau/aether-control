using System;

namespace AetherControl.Core.Layout;

/// <summary>
/// Maps a window/page width to a content scale factor, so text and spacing grow on a wide window
/// and shrink before a narrow one crowds them. Quantised to <see cref="Step"/> so a resize drag
/// doesn't re-run layout on every pixel: scale changes only when the window crosses a band edge.
/// </summary>
public static class WindowScale
{
    /// <summary>Width at which content renders 1:1 (comfortable wide-window reference).</summary>
    public const double ReferenceWidth = 1400;

    /// <summary>Smallest scale — small windows get slightly smaller type so more fits.</summary>
    public const double Min = 0.90;

    /// <summary>Largest scale — maximised windows get noticeably larger type.</summary>
    public const double Max = 1.15;

    public const double Step = 0.05;

    public static double ForWidth(double width)
    {
        if (!double.IsFinite(width) || width <= 0)
        {
            return 1.0;
        }

        var clamped = Math.Clamp(width / ReferenceWidth, Min, Max);
        // Round the product too: 23 * 0.05 lands on 1.1500000000000001 in double arithmetic,
        // which wouldn't compare equal to the Max literal callers/tests expect.
        return Math.Round(Math.Round(clamped / Step) * Step, 2);
    }
}
