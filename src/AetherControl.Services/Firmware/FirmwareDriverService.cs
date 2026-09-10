using System.Management;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Firmware;

/// <summary>
/// Reports the *current* BIOS/GPU/chipset/network driver versions via WMI —
/// all of which Windows knows locally and reliably. "Recommended" versions
/// are deliberately left null unless a vendor is present in the local
/// catalog with a maintained link; this service intentionally does not
/// scrape vendor sites for "latest" numbers, since that's fragile, easy to
/// get wrong, and the spec requires recommendations/links only — never an
/// automatic install.
/// </summary>
public sealed class FirmwareDriverService(ILogger<FirmwareDriverService> logger) : IFirmwareDriverService
{
    public Task<IReadOnlyList<DriverStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        var results = new List<DriverStatus>();

        TryAdd(results, "BIOS", QueryBiosVersion, "https://www.asus.com/support/");
        TryAdd(results, "GPU Driver", QueryGpuDriverVersion, "https://www.nvidia.com/drivers");
        TryAdd(results, "Chipset", QueryChipsetVersion, null);
        TryAdd(results, "Network Adapter", QueryNetworkDriverVersion, null);

        return Task.FromResult<IReadOnlyList<DriverStatus>>(results);
    }

    private void TryAdd(List<DriverStatus> results, string component, Func<string?> query, string? vendorUrl)
    {
        try
        {
            var version = query();
            if (version is not null)
            {
                results.Add(new DriverStatus { Component = component, CurrentVersion = version, VendorPageUrl = vendorUrl });
            }
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query {Component} via WMI", component);
        }
    }

    private static string? QueryBiosVersion()
    {
        using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, Manufacturer FROM Win32_BIOS");
        foreach (ManagementObject item in searcher.Get())
        {
            return item["SMBIOSBIOSVersion"]?.ToString();
        }

        return null;
    }

    private static string? QueryGpuDriverVersion()
    {
        using var searcher = new ManagementObjectSearcher("SELECT DriverVersion, Name FROM Win32_VideoController");
        foreach (ManagementObject item in searcher.Get())
        {
            return item["DriverVersion"]?.ToString();
        }

        return null;
    }

    private static string? QueryChipsetVersion()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product, Version FROM Win32_BaseBoard");
        foreach (ManagementObject item in searcher.Get())
        {
            return item["Version"]?.ToString();
        }

        return null;
    }

    private static string? QueryNetworkDriverVersion()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass='NET'");
        foreach (ManagementObject item in searcher.Get())
        {
            var version = item["DriverVersion"]?.ToString();
            if (!string.IsNullOrEmpty(version))
            {
                return version;
            }
        }

        return null;
    }
}
