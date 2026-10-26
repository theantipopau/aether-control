using AetherControl.Services.Hardware;

namespace AetherControl.Tests;

public sealed class SensorContendersTests
{
    // Process names as seen live on the test rig (2026-10-26), plus tools that must not match.
    [Fact]
    public void Match_NamesKnownReaders_OncePerProduct_IgnoringOverlayTools()
    {
        var running = new[]
        {
            "ArmouryCrate.Service", "AsusFanControlService", "ArmourySocketServer", "iCUE",
            "hwinfo64", "RTSS", "RadeonSoftware", "explorer"
        };

        var labels = SensorContenders.Match(running);

        Assert.Equal(["Armoury Crate", "Corsair iCUE", "HWiNFO"], labels);
    }
}
