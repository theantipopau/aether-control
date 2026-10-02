using AetherControl.Services.Firmware;

namespace AetherControl.Tests;

/// <summary>
/// The firmware page used to link *every* GPU row to nvidia.com regardless of what was actually
/// installed — a plausible-looking but wrong answer, exactly what the product rules forbid.
/// These pin link resolution to the real vendor of whatever hardware was detected.
/// </summary>
public class FirmwareVendorLinksTests
{
    [Theory]
    [InlineData("ASUSTeK COMPUTER INC.", "asus.com")]
    [InlineData("Gigabyte Technology Co., Ltd.", "gigabyte.com")]
    [InlineData("Micro-Star International Co., Ltd.", "msi.com")]
    [InlineData("ASRock", "asrock.com")]
    [InlineData("Hewlett-Packard", "support.hp.com")]
    public void ForMotherboard_MatchesKnownManufacturers(string manufacturer, string expectedHost)
    {
        var url = FirmwareVendorLinks.ForMotherboard(manufacturer);
        Assert.NotNull(url);
        Assert.Contains(expectedHost, url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForMotherboard_UnknownOrEmpty_ReturnsNull()
    {
        // Unknown must stay null — a wrong link is worse than no link.
        Assert.Null(FirmwareVendorLinks.ForMotherboard("Some ODM Ltd."));
        Assert.Null(FirmwareVendorLinks.ForMotherboard(null));
        Assert.Null(FirmwareVendorLinks.ForMotherboard("   "));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4070", "nvidia.com")]
    [InlineData("AMD Radeon RX 9070 XT", "amd.com")]
    [InlineData("Intel(R) UHD Graphics 770", "intel.com")]
    public void ForGpu_MatchesActualVendor(string gpuName, string expectedHost)
    {
        var url = FirmwareVendorLinks.ForGpu(gpuName);
        Assert.NotNull(url);
        Assert.Contains(expectedHost, url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForGpu_UnknownVendor_ReturnsNull()
    {
        Assert.Null(FirmwareVendorLinks.ForGpu("Microsoft Basic Display Adapter"));
        Assert.Null(FirmwareVendorLinks.ForGpu(null));
    }

    [Theory]
    [InlineData("AMD", "amd.com")]
    [InlineData("Advanced Micro Devices, Inc.", "amd.com")]
    [InlineData("Intel", "intel.com")]
    public void ForChipset_MatchesVendor(string vendor, string expectedHost)
    {
        var url = FirmwareVendorLinks.ForChipset(vendor);
        Assert.NotNull(url);
        Assert.Contains(expectedHost, url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ForChipset_UnknownVendor_ReturnsNull()
    {
        Assert.Null(FirmwareVendorLinks.ForChipset("VIA Technologies"));
    }
}
