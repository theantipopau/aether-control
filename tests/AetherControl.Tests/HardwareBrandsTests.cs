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

    [Theory]
    // Friendly names (WMI Model sometimes includes the vendor, sometimes not).
    [InlineData("Seagate BarraCuda Compute 3TB", "seagate")]
    [InlineData("Samsung SSD 990 PRO 2TB", "samsung")]
    [InlineData("Crucial P5 Plus 1TB", "crucial")]
    [InlineData("Predator SSD GM7 1TB", "acer")]
    [InlineData("INTEL SSDPEKNW512G8", "intel")]
    // Bare model codes — the actual WMI Model shape on most machines ("ST3000LM024-1SL174",
    // "WDC WD10EZEX-08WN4A0") carries no vendor word at all.
    [InlineData("ST3000LM024-1SL174", "seagate")]
    [InlineData("ST1000DM010-2EP102", "seagate")]
    [InlineData("WDC WD10EZEX-08WN4A0", "wd")]
    [InlineData("WD Blue SN570 1TB", "wd")]
    [InlineData("WDS250G2B0A-00SGH0", "wd")]
    [InlineData("CT1000MX500SSD1", "crucial")]
    [InlineData("Kingston SA400S37480G", "kingston")]
    [InlineData("HyperX Fury 240GB", "kingston")]
    [InlineData("SKC2000S8250G", "kingston")]
    [InlineData("MZVLB1T0HALR-000L7", "samsung")]
    public void DetectDrive_MatchesRealModelStrings(string model, string expectedKey)
    {
        var brand = HardwareBrands.DetectDrive(model);

        Assert.NotNull(brand);
        Assert.Equal(expectedKey, brand!.Key);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Not detected")]
    // Unknown ODM code that merely starts with two letters must not earn a badge — a wrong
    // brand mark is worse than none (same rule as every other Detect* here).
    [InlineData("Some ODM Ltd. SSD")]
    [InlineData("SATA SSD 512GB")]
    // Vendors we hold no logo for stay null instead of borrowing a related brand's mark.
    [InlineData("SanDisk SSD PLUS 480GB")]
    [InlineData("Micron 2400 MTFDKBA1T0QFM")]
    public void DetectDrive_UnknownOrUnlogoed_ReturnsNull(string? model)
    {
        Assert.Null(HardwareBrands.DetectDrive(model));
    }

    [Fact]
    public void AllBrands_PointAtARealAppAsset()
    {
        var brands = new[]
        {
            HardwareBrands.Amd, HardwareBrands.Intel, HardwareBrands.Nvidia, HardwareBrands.Radeon,
            HardwareBrands.Asus, HardwareBrands.Asrock, HardwareBrands.Gigabyte, HardwareBrands.Msi,
            HardwareBrands.Seagate, HardwareBrands.Wd, HardwareBrands.Samsung,
            HardwareBrands.Crucial, HardwareBrands.Kingston, HardwareBrands.Acer,
        };

        foreach (var brand in brands)
        {
            Assert.StartsWith(HardwareBrands.AssetRoot, brand.LogoUri, StringComparison.Ordinal);
            Assert.EndsWith(".png", brand.LogoUri, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(brand.Name));
        }
    }
}
