using System;

namespace AetherControl.Core.Models;

/// <summary>A vendor brand recognised from a hardware name string, with the logo asset that
/// represents it on the dashboard.</summary>
public sealed record BrandInfo(string Key, string Name, string LogoUri);

/// <summary>
/// Maps detected hardware names (CPU / GPU / motherboard / drive) to the vendor's logo so the
/// Overview and related pages can show brand imagery beside each section. Pure string matching —
/// same product rule as
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

    // Drive vendors — badge the Storage cards. WMI's Model field frequently omits the vendor
    // entirely ("ST3000LM024-1SL174", "WDC WD10EZEX-08WN4A0"), so DetectDrive also recognises
    // bare model-code prefixes alongside friendly names.
    public static readonly BrandInfo Seagate = new("seagate", "Seagate", AssetRoot + "seagate.png");
    public static readonly BrandInfo Wd = new("wd", "Western Digital", AssetRoot + "wd.png");
    public static readonly BrandInfo Samsung = new("samsung", "Samsung", AssetRoot + "samsung.png");
    public static readonly BrandInfo Crucial = new("crucial", "Crucial", AssetRoot + "crucial.png");
    public static readonly BrandInfo Kingston = new("kingston", "Kingston", AssetRoot + "kingston.png");
    public static readonly BrandInfo Acer = new("acer", "Acer", AssetRoot + "acer.png");

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

    /// <summary>Vendor mark for a storage drive, from its model string (StorageDriveInfo.Model).
    /// Same product rule as the rest of this class: only vendors we actually hold a logo for are
    /// matched — SanDisk, Micron, SK hynix, Kioxia etc. fall through to null rather than borrowing
    /// a related brand's mark (SanDisk is a WD subsidiary, but a WD badge on a SanDisk drive would
    /// still be the wrong badge).</summary>
    public static BrandInfo? DetectDrive(string? model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return null;
        }

        // Friendly-name matches first ("Seagate BarraCuda", "Predator SSD GM7", "INTEL SSDPE…").
        if (Contains(model, "seagate")) return Seagate;
        if (Contains(model, "western digital")) return Wd;
        if (Contains(model, "samsung")) return Samsung;
        if (Contains(model, "crucial")) return Crucial;
        if (Contains(model, "kingston", "hyperx")) return Kingston;
        if (Contains(model, "predator", "acer")) return Acer;
        if (Contains(model, "intel")) return Intel;

        // Bare model codes — WMI Model often carries only the code. Each prefix is deliberately
        // constrained (letter-run + digit, or an exact long code) so a coincidental start like
        // "ST…" on an unknown ODM drive can't earn a Seagate badge.
        if (CodeStartsWith(model, "ST", requireDigitAfter: true)) return Seagate;
        if (CodeStartsWith(model, "WDC") || CodeStartsWith(model, "WDS") ||
            CodeStartsWith(model, "WD", requireSeparatorAfter: true)) return Wd;
        if (CodeStartsWith(model, "CT", requireDigitAfter: true)) return Crucial;
        if (CodeStartsWith(model, "KC", requireDigitAfter: true) || CodeStartsWith(model, "SKC") ||
            CodeStartsWith(model, "SA400") || CodeStartsWith(model, "SUV500")) return Kingston;
        if (CodeStartsWith(model, "MZ")) return Samsung;

        return null;
    }

    /// <summary>Ordinal-ignore-case prefix check with an optional constraint on the character that
    /// follows the prefix (a digit for two-letter codes like ST/CT, or a digit-or-space so
    /// "WD Blue" matches but an arbitrary "WD…" word does not).</summary>
    private static bool CodeStartsWith(string value, string prefix, bool requireDigitAfter = false, bool requireSeparatorAfter = false)
    {
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!requireDigitAfter && !requireSeparatorAfter)
        {
            return true;
        }

        if (value.Length <= prefix.Length)
        {
            return false;
        }

        var next = value[prefix.Length];
        return char.IsDigit(next) || (requireSeparatorAfter && next == ' ');
    }

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
