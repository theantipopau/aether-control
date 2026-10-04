using AetherControl.Core.Layout;

namespace AetherControl.Tests;

/// <summary>Portrait Mode's shrink cycle and monitor-hop maths. These drive real window bounds,
/// so wrapping, display-fitting and the MinWidth floor all need to hold before the UI runs.</summary>
public class PortraitSizingTests
{
    [Fact]
    public void StepsCycleWithWraparound()
    {
        Assert.Equal(1, PortraitSizing.NextStep(0));
        Assert.Equal(2, PortraitSizing.NextStep(1));
        Assert.Equal(3, PortraitSizing.NextStep(2));
        Assert.Equal(0, PortraitSizing.NextStep(3));
    }

    [Fact]
    public void NextStep_ClampsOutOfRangeInputBeforeWrapping()
    {
        Assert.Equal(1, PortraitSizing.NextStep(-5)); // clamps to step 0, then advances
        Assert.Equal(0, PortraitSizing.NextStep(99));  // clamps to last step, then wraps
    }

    [Theory]
    [InlineData(768, 0)]   // exact full size
    [InlineData(614, 1)]   // exact 80%
    [InlineData(499, 2)]   // exact 65%
    [InlineData(384, 3)]   // exact 50%
    [InlineData(900, 0)]   // wider than base (e.g. a FILLed display) clamps to full
    [InlineData(200, 3)]   // narrower than the smallest preset clamps to smallest
    [InlineData(700, 0)]   // 700 is nearer 768 than 614
    [InlineData(560, 1)]   // 560 is nearer 614 than 499
    public void NearestStep_MapsLiveWidthsToPresets(double width, int expected)
    {
        Assert.Equal(expected, PortraitSizing.NearestStep(width));
    }

    [Fact]
    public void NearestStep_InvalidWidthsFallBackToFull()
    {
        Assert.Equal(0, PortraitSizing.NearestStep(0));
        Assert.Equal(0, PortraitSizing.NearestStep(-100));
        Assert.Equal(0, PortraitSizing.NearestStep(double.NaN));
    }

    [Fact]
    public void SizeForStep_FullOnASpaceyDisplayIsTheBaseSize()
    {
        var (width, height) = PortraitSizing.SizeForStep(0, 1920, 1920);
        Assert.Equal(768, width);
        Assert.Equal(1366, height);
    }

    [Fact]
    public void SizeForStep_AppliesPresetFractions()
    {
        Assert.Equal((614, 1093), PortraitSizing.SizeForStep(1, 1920, 1920));
        Assert.Equal((499, 888), PortraitSizing.SizeForStep(2, 1920, 1920));
        Assert.Equal((384, 683), PortraitSizing.SizeForStep(3, 1920, 1920));
    }

    [Fact]
    public void SizeForStep_FitsToADisplaySmallerThanThePreset()
    {
        // 500x900 display, full preset: aspect-preserved fit must not spill off-screen.
        var (width, height) = PortraitSizing.SizeForStep(0, 500, 900);
        Assert.Equal(500, width);
        Assert.InRange(height, 1, 900);
        Assert.True((double)height / width >= 1366.0 / 768.0 - 0.02,
            $"aspect drifted: {width}x{height}");
    }

    [Fact]
    public void SizeForStep_NeverShrinksBelowMinWidthWhenTheDisplayAllowsIt()
    {
        // Height-constrained 500x400 display: a pure aspect fit would be ~225px wide — the
        // MinWidth floor lifts it to 340 (still within the 500px display).
        var (width, height) = PortraitSizing.SizeForStep(0, 500, 400);
        Assert.Equal(PortraitSizing.MinWidth, width);
        Assert.Equal(400, height);

        // A display narrower than MinWidth itself caps the width instead of spilling.
        var (narrowWidth, _) = PortraitSizing.SizeForStep(0, 300, 1000);
        Assert.Equal(300, narrowWidth);
    }

    [Fact]
    public void SizeForStep_DegenerateDisplayBoundsDoNotThrow()
    {
        var (width, height) = PortraitSizing.SizeForStep(0, 0, -10);
        Assert.Equal(0, width);
        Assert.Equal(0, height);
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 80)]
    [InlineData(2, 65)]
    [InlineData(3, 50)]
    public void PercentLabels_MatchThePresets(int step, int expected)
    {
        Assert.Equal(expected, PortraitSizing.PercentForStep(step));
    }

    [Fact]
    public void NextDisplay_WrapsAroundAndToleratesDegenerateCounts()
    {
        Assert.Equal(1, PortraitSizing.NextDisplay(0, 3));
        Assert.Equal(0, PortraitSizing.NextDisplay(2, 3));
        Assert.Equal(0, PortraitSizing.NextDisplay(0, 1)); // single display: nowhere to go
        Assert.Equal(0, PortraitSizing.NextDisplay(5, 0)); // defensive: nothing to hop to
        Assert.Equal(0, PortraitSizing.NextDisplay(-1, 3)); // -1 normalises to 2 (last), then wraps
    }
}
