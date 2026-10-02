using AetherControl.Core.Models;

namespace AetherControl.Tests;

/// <summary>
/// Dashboard logo badges must identify the real vendor of the detected hardware — same rule as
/// FirmwareVendorLinks: unknown hardware yields no logo rather than a wrong brand.
/// </summary>
public class HardwareBrandsTests
{
    [Theory]
    [InlineData("AMD Ryzen 7 9800X3D", "amd")]
    [InlineData("AMD FX-8350", "amd")]
    [InlineData("AMD Ryzen 9 7950X", "amd")]
    [InlineData("13th Gen Intel(R) Core(TM) i5-13600K", "intel")]
    [InlineData("Intel(R) Xeon(R) CPU E5-2680 v4", "intel")]
    public void DetectCpu_MatchesKnownVendors(string cpuName, string expectedKey)
    {
        var brand = HardwareBrands.DetectCpu(cpuName);

        Assert.NotNull(brand);
        Assert.Equal(expectedKey, brand!.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not detected")]
    public void DetectCpu_Unknown_ReturnsNull(string? cpuName)
    {
        Assert.Null(HardwareBrands.DetectCpu(cpuName));
    }

    [Theory]
    [InlineData("NVIDIA GeForce RTX 4080", "nvidia")]
    [InlineData("NVIDIA GeForce GTX 1660 SUPER", "nvidia")]
    [InlineData("AMD Radeon RX 9070 XT", "radeon")]
    [InlineData("Radeon RX 6700 XT", "radeon")]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", "intel")]
    public void DetectGpu_MatchesActualVendor(string gpuName, string expectedKey)
    {
        var brand = HardwareBrands.DetectGpu(gpuName);

        Assert.NotNull(brand);
        Assert.Equal(expectedKey, brand!.Key);
    }

    [Fact]
    public void DetectGpu_UnknownVendor_ReturnsNull()
    {
        Assert.Null(HardwareBrands.DetectGpu("Microsoft Basic Display Adapter"));
        Assert.Null(HardwareBrands.DetectGpu(null));
    }

    [Theory]
    [InlineData("ASUS PRIME B650EM-A WIFI", "asus")]
    [InlineData("ASRock B650E PG Lightning WiFi", "asrock")]
    [InlineData("Gigabyte Technology Co., Ltd. B550 AORUS ELITE", "gigabyte")]
    [InlineData("MSI MAG B650 TOMAHAWK WIFI", "msi")]
    [InlineData("Micro-Star International Co., Ltd. PRO B660M-A", "msi")]
    public void DetectMotherboard_MatchesKnownVendors(string board, string expectedKey)
    {
        var brand = HardwareBrands.DetectMotherboard(board);

        Assert.NotNull(brand);
        Assert.Equal(expectedKey, brand!.Key);
    }

    [Fact]
    public void DetectMotherboard_Unknown_ReturnsNull()
    {
        // Unknown must stay null — a wrong brand badge is worse than none.
        Assert.Null(HardwareBrands.DetectMotherboard("Some ODM Ltd."));
        Assert.Null(HardwareBrands.DetectMotherboard("Not detected"));
        Assert.Null(HardwareBrands.DetectMotherboard(null));
    }

    [Fact]
    public void AllBrands_PointAtARealAppAsset()
    {
        var brands = new[]
        {
            HardwareBrands.Amd, HardwareBrands.Intel, HardwareBrands.Nvidia, HardwareBrands.Radeon,
            HardwareBrands.Asus, HardwareBrands.Asrock, HardwareBrands.Gigabyte, HardwareBrands.Msi,
        };

        foreach (var brand in brands)
        {
            Assert.StartsWith(HardwareBrands.AssetRoot, brand.LogoUri, StringComparison.Ordinal);
            Assert.EndsWith(".png", brand.LogoUri, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(brand.Name));
        }
    }
}
