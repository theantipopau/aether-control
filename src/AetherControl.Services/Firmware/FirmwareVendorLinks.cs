namespace AetherControl.Services.Firmware;

/// <summary>
/// Maps what's actually installed (board manufacturer, GPU name, chipset vendor) to that
/// vendor's own driver/support page. Pure string matching so it's unit-testable — the old
/// implementation linked *every* GPU row to nvidia.com regardless of what was in the machine,
/// which is exactly the "plausible-looking but wrong" outcome the product rules forbid.
/// </summary>
public static class FirmwareVendorLinks
{
    public static string? ForMotherboard(string? manufacturer)
    {
        var m = Normalize(manufacturer);
        if (m is null)
        {
            return null;
        }

        if (m.Contains("asus")) return "https://www.asus.com/support/";
        if (m.Contains("gigabyte")) return "https://www.gigabyte.com/support";
        if (m.Contains("micro-star") || m.Contains("msi")) return "https://www.msi.com/support";
        if (m.Contains("asrock")) return "https://www.asrock.com/support";
        if (m.Contains("hewlett") || m.Contains("hp")) return "https://support.hp.com/";
        if (m.Contains("dell")) return "https://www.dell.com/support";
        if (m.Contains("lenovo")) return "https://support.lenovo.com/";
        return null;
    }

    public static string? ForGpu(string? gpuName)
    {
        var n = Normalize(gpuName);
        if (n is null)
        {
            return null;
        }

        if (n.Contains("nvidia") || n.Contains("geforce")) return "https://www.nvidia.com/drivers";
        if (n.Contains("amd") || n.Contains("radeon")) return "https://www.amd.com/en/support";
        if (n.Contains("intel")) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        return null;
    }

    public static string? ForChipset(string? vendorName)
    {
        var v = Normalize(vendorName);
        if (v is null)
        {
            return null;
        }

        if (v.Contains("amd") || v.Contains("advanced micro") || v.Contains("ryzen")) return "https://www.amd.com/en/support";
        if (v.Contains("intel")) return "https://www.intel.com/content/www/us/en/download-center/home.html";
        return null;
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
}
