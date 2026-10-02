using System;

namespace AetherControl.Core.Models;

/// <summary>A vendor brand recognised from a hardware name string, with the logo asset that
/// represents it on the dashboard.</summary>
public sealed record BrandInfo(string Key, string Name, string LogoUri);

/// <summary>
/// Maps detected hardware names (CPU / GPU / motherboard) to the vendor's logo so the Overview can
/// show brand imagery beside each section. Pure string matching — same product rule as
/// <c>FirmwareVendorLinks</c>: unknown hardware yields null rather than a plausible-looking but
/// wrong brand. Logos are white silhouettes in Assets/Vendors (see README for their sources);
/// vendor names and logos are trademarks of their owners, shown for identification only.
/// </summary>
public static class HardwareBrands
{
    public const string AssetRoot = "ms-appx:///Assets/Vendors/";

    public static readonly BrandInfo Amd = new("amd", "AMD", AssetRoot + "amd.png");
    public static readonly BrandInfo Intel = new("intel", "Intel", AssetRoot + "intel.png");
    public static readonly BrandInfo Nvidia = new("nvidia", "NVIDIA", AssetRoot + "nvidia.png");
    public static readonly BrandInfo Radeon = new("radeon", "Radeon", AssetRoot + "radeon.png");
    public static readonly BrandInfo Asus = new("asus", "ASUS", AssetRoot + "asus.png");
    public static readonly BrandInfo Asrock = new("asrock", "ASRock", AssetRoot + "asrock.png");
    public static readonly BrandInfo Gigabyte = new("gigabyte", "GIGABYTE", AssetRoot + "gigabyte.png");
    public static readonly BrandInfo Msi = new("msi", "MSI", AssetRoot + "msi.png");

    public static BrandInfo? DetectCpu(string? name) =>
        Contains(name, "ryzen", "threadripper", "epyc", "athlon", "amd") ? Amd :
        Contains(name, "intel", "core", "xeon", "pentium", "celeron") ? Intel :
        null;

    public static BrandInfo? DetectGpu(string? name) =>
        Contains(name, "geforce", "rtx", "gtx", "quadro", "tesla", "nvidia") ? Nvidia :
        Contains(name, "radeon", "firepro") ? Radeon :
        Contains(name, "amd") ? Amd :
        Contains(name, "arc", "intel") ? Intel :
        null;

    public static BrandInfo? DetectMotherboard(string? name) =>
        Contains(name, "asrock") ? Asrock :
        Contains(name, "asus") ? Asus :
        Contains(name, "gigabyte") ? Gigabyte :
        Contains(name, "msi", "micro-star") ? Msi :
        null;

    private static bool Contains(string? value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        foreach (var needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
