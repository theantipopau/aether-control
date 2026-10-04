using AetherControl.Core.Layout;

namespace AetherControl.Tests;

/// <summary>The window → content scale drives every page's type size; it must stay inside the
/// designed band and only move in quantised steps (a resize drag must not re-run layout per pixel).</summary>
public class WindowScaleTests
{
    [Fact]
    public void ReferenceWidth_IsOneToOne()
    {
        Assert.Equal(1.0, WindowScale.ForWidth(WindowScale.ReferenceWidth));
    }

    [Fact]
    public void NarrowWindow_ClampsAtMin()
    {
        Assert.Equal(WindowScale.Min, WindowScale.ForWidth(400));
        Assert.Equal(WindowScale.Min, WindowScale.ForWidth(1000));
    }

    [Fact]
    public void WideWindow_ClampsAtMax()
    {
        Assert.Equal(WindowScale.Max, WindowScale.ForWidth(2560));
        Assert.Equal(WindowScale.Max, WindowScale.ForWidth(5000));
    }

    [Theory]
    [InlineData(1400, 1.00)]
    [InlineData(1470, 1.05)] // 1.05 exactly on a band edge
    [InlineData(1500, 1.05)] // 1.071 -> nearest 0.05 step down/up lands on 1.05
    [InlineData(1540, 1.10)] // 1.10
    [InlineData(1260, 0.90)] // 0.90
    [InlineData(1330, 0.95)] // 0.95
    public void WidthsMapToQuantisedSteps(double width, double expected)
    {
        Assert.Equal(expected, WindowScale.ForWidth(width), 3);
    }

    [Fact]
    public void StepsAreAlwaysMultiplesOfTheQuantum()
    {
        foreach (var width in new double[] { 640, 960, 1111, 1280, 1444, 1601, 1920, 2560 })
        {
            var s = WindowScale.ForWidth(width);
            var steps = s / WindowScale.Step;
            Assert.Equal(Math.Round(steps), steps, 3);
            Assert.InRange(s, WindowScale.Min, WindowScale.Max);
        }
    }

    [Fact]
    public void InvalidWidth_ReturnsNeutralScale()
    {
        Assert.Equal(1.0, WindowScale.ForWidth(0));
        Assert.Equal(1.0, WindowScale.ForWidth(-50));
        Assert.Equal(1.0, WindowScale.ForWidth(double.NaN));
    }
}
