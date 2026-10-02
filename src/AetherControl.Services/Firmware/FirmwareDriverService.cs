using System.Management;
using AetherControl.Core.Interfaces;
using AetherControl.Core.Models;
using Microsoft.Extensions.Logging;

namespace AetherControl.Services.Firmware;

/// <summary>
/// Reports the *current* BIOS/GPU/chipset/network driver versions via WMI — all of which
/// Windows knows locally and reliably — and, on explicit request only, asks the local
/// Windows Update agent what driver/firmware updates are pending.
/// <para>
/// Two honesty rules shape this service: (1) it never installs anything — the update check
/// only *lists* what Windows Update offers, and the user reviews it in Windows Settings;
/// (2) every row must be what it says it is — the old "Chipset" row actually showed the
/// motherboard's PCB revision (from <c>Win32_BaseBoard.Version</c>), so a real chipset-driver
/// query replaced it, with an honestly-labelled "Motherboard" model row as the fallback when
/// no chipset INF is found. Vendor links are derived from what's installed
/// (<see cref="FirmwareVendorLinks"/>) rather than assumed.
/// </para>
/// </summary>
public sealed class FirmwareDriverService(ILogger<FirmwareDriverService> logger) : IFirmwareDriverService
{
    // Two separate searches so a Windows build that doesn't understand the 'Firmware' category
    // (it's newer than 'Driver') still returns real driver rows — each failure is recorded
    // independently instead of poisoning the whole check.
    private static readonly (string Criteria, string Kind)[] UpdateSearches =
    [
        ("IsInstalled=0 and Type='Driver'", "Driver"),
        ("IsInstalled=0 and Type='Firmware'", "Firmware")
    ];

    public Task<IReadOnlyList<DriverStatus>> GetStatusAsync(CancellationToken ct = default) =>
        // WMI enumeration can take a few hundred ms on a cold repository — never do it on the
        // calling (UI) thread just because the result itself is synchronous.
        Task.Run<IReadOnlyList<DriverStatus>>(() =>
        {
            var results = new List<DriverStatus>();
            var boardManufacturer = TryQuery(BaseBoardManufacturer, "baseboard manufacturer");

            AddBios(results);
            AddGpu(results);
            AddChipsetOrMotherboard(results, boardManufacturer);
            AddNetwork(results);
            return results;
        }, ct);

    public Task<DriverUpdateCheck> CheckForUpdatesAsync(CancellationToken ct = default) =>
        Task.Run(() => SearchWindowsUpdate(ct), CancellationToken.None);

    // ── Installed versions ────────────────────────────────────────────────────────────────────

    private void AddBios(List<DriverStatus> results)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (ManagementObject item in searcher.Get())
            {
                var version = item["SMBIOSBIOSVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                // The ROM's own build date next to the version — the one extra piece of data that
                // makes "is this current?" answerable at a glance for a desktop board.
                var releaseDate = item["ReleaseDate"] is DateTime date ? $" ({date:yyyy-MM-dd})" : string.Empty;
                results.Add(new DriverStatus
                {
                    Component = "BIOS / UEFI",
                    CurrentVersion = version + releaseDate,
                    VendorPageUrl = FirmwareVendorLinks.ForMotherboard(TryQuery(BaseBoardManufacturer, "bios vendor"))
                });
                return;
            }
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query BIOS version via WMI");
        }
    }

    private void AddGpu(List<DriverStatus> results)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion FROM Win32_VideoController");

            // Integrated + discrete both show up here. Prefer the real vendor driver over the
            // fallback "Microsoft Basic Display Adapter", and never present two rows that are
            // really the same head — distinct names joined, distinct versions joined.
            var names = new List<string>();
            var versions = new List<string>();
            foreach (ManagementObject item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                var version = item["DriverVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Microsoft Basic", StringComparison.OrdinalIgnoreCase))
                {
                    AddDistinct(names, name);
                }

                AddDistinct(versions, version);
            }

            if (versions.Count > 0)
            {
                results.Add(new DriverStatus
                {
                    Component = names.Count > 0 ? $"GPU Driver — {string.Join(", ", names)}" : "GPU Driver",
                    CurrentVersion = string.Join(" / ", versions),
                    VendorPageUrl = FirmwareVendorLinks.ForGpu(names.Count > 0 ? names[0] : null)
                });
            }
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query GPU driver version via WMI");
        }
    }

    private void AddChipsetOrMotherboard(List<DriverStatus> results, string? boardManufacturer)
    {
        // A real chipset-driver query: the chipset software installs INFs whose device/inf names
        // say "chipset" (AMD Chipset Software, Intel INF/ME drivers). If this board exposes none
        // of that (or WMI refuses), fall through to an honestly-labelled motherboard row instead
        // of pretending a PCB revision is a driver version.
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, Manufacturer, DriverVersion FROM Win32_PnPSignedDriver " +
                "WHERE (DeviceName LIKE '%Chipset%' OR InfName LIKE '%chipset%')");
            foreach (ManagementObject item in searcher.Get())
            {
                var version = item["DriverVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                var deviceName = item["DeviceName"]?.ToString() ?? "Chipset";
                var vendor = item["Manufacturer"]?.ToString();
                results.Add(new DriverStatus
                {
                    Component = $"Chipset — {deviceName}",
                    CurrentVersion = version,
                    VendorPageUrl = FirmwareVendorLinks.ForChipset(vendor) ?? FirmwareVendorLinks.ForChipset(deviceName)
                });
                return;
            }
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query chipset driver via WMI");
        }

        // Honest fallback: the board itself (model, not a fake "chipset version").
        var product = TryQuery(BaseBoardProduct, "baseboard product");
        if (!string.IsNullOrWhiteSpace(product))
        {
            results.Add(new DriverStatus
            {
                Component = "Motherboard",
                CurrentVersion = product,
                VendorPageUrl = FirmwareVendorLinks.ForMotherboard(boardManufacturer)
            });
        }
    }

    private void AddNetwork(List<DriverStatus> results)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT DeviceName, DriverVersion FROM Win32_PnPSignedDriver WHERE DeviceClass='NET'");
            foreach (ManagementObject item in searcher.Get())
            {
                var version = item["DriverVersion"]?.ToString();
                if (string.IsNullOrWhiteSpace(version))
                {
                    continue;
                }

                var name = item["DeviceName"]?.ToString();
                results.Add(new DriverStatus
                {
                    Component = string.IsNullOrWhiteSpace(name) ? "Network Adapter" : $"Network — {name}",
                    CurrentVersion = version
                });
                return; // first real adapter is plenty — the row is informational
            }
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query network driver version via WMI");
        }
    }

    // ── Pending-update search (opt-in, listing only) ──────────────────────────────────────────

    private DriverUpdateCheck SearchWindowsUpdate(CancellationToken ct)
    {
        List<PendingDriverUpdate> found;
        var failedKinds = new List<string>();

        try
        {
            var progId = Type.GetTypeFromProgID("Microsoft.Update.Session");
            if (progId is null)
            {
                return new DriverUpdateCheck
                {
                    Succeeded = false,
                    Message = "The Windows Update agent is not available on this system."
                };
            }

            if (Activator.CreateInstance(progId) is not { } session)
            {
                return new DriverUpdateCheck
                {
                    Succeeded = false,
                    Message = "Could not start the Windows Update agent."
                };
            }

            dynamic updateSession = session;
            dynamic searcher = updateSession.CreateUpdateSearcher();
            found = [];

            foreach (var (criteria, kind) in UpdateSearches)
            {
                if (ct.IsCancellationRequested)
                {
                    break;
                }

                try
                {
                    dynamic result = searcher.Search(criteria);
                    dynamic updates = result.Updates;
                    var count = (int)updates.Count;
                    for (var i = 1; i <= count; i++) // IUpdateCollection is 1-based
                    {
                        dynamic update = updates[i];
                        var title = (string)update.Title;
                        if (string.IsNullOrWhiteSpace(title))
                        {
                            continue;
                        }

                        string? supportUrl = null;
                        try
                        {
                            var raw = (string?)update.SupportUrl;
                            supportUrl = string.IsNullOrWhiteSpace(raw) || !raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                ? null
                                : raw;
                        }
                        catch (Exception ex)
                        {
                            logger.LogDebug(ex, "Update row has no support URL");
                        }

                        found.Add(new PendingDriverUpdate { Title = title, Kind = kind, SupportUrl = supportUrl });
                    }
                }
                catch (Exception ex)
                {
                    // One category failing (e.g. 'Firmware' unsupported on this build, or the WU
                    // service disabled) must not discard the other's real results.
                    logger.LogDebug(ex, "Windows Update search failed for {Kind}", kind);
                    failedKinds.Add(kind);
                }
            }
        }
        catch (Exception ex)
        {
            // Best-effort I/O path: never crash the app — surface an honest status instead.
            logger.LogWarning(ex, "Windows Update driver search failed");
            return new DriverUpdateCheck
            {
                Succeeded = false,
                Message = $"Windows Update search failed: {ex.Message}"
            };
        }

        if (found.Count == 0 && failedKinds.Count == UpdateSearches.Length)
        {
            return new DriverUpdateCheck
            {
                Succeeded = false,
                Message = $"Windows Update search failed ({string.Join(", ", failedKinds)}) — the service may be disabled or paused."
            };
        }

        // De-dupe (the same update can surface from both categories) and lead with firmware —
        // a pending BIOS update is the row people most want to see.
        var distinct = found
            .GroupBy(u => u.Title, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(u => u.Kind == "Firmware" ? 0 : 1).First())
            .OrderBy(u => u.Kind == "Firmware" ? 0 : 1)
            .ThenBy(u => u.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var message = distinct.Count == 0
            ? "Windows Update reports no pending driver or firmware updates."
            : $"{distinct.Count} pending update{(distinct.Count == 1 ? "" : "s")} reported by Windows Update. Review and install them in Windows Settings — Aether Control never installs anything itself.";
        if (failedKinds.Count > 0)
        {
            message += $" ({string.Join(", ", failedKinds)} search not supported on this system.)";
        }

        return new DriverUpdateCheck { Succeeded = true, Message = message, Updates = distinct };
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private string? TryQuery(Func<string?> query, string what)
    {
        try
        {
            return query();
        }
        catch (ManagementException ex)
        {
            logger.LogDebug(ex, "Could not query {What} via WMI", what);
            return null;
        }
    }

    private static string? BaseBoardManufacturer()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Manufacturer FROM Win32_BaseBoard");
        foreach (ManagementObject item in searcher.Get())
        {
            return item["Manufacturer"]?.ToString();
        }

        return null;
    }

    private static string? BaseBoardProduct()
    {
        using var searcher = new ManagementObjectSearcher("SELECT Product FROM Win32_BaseBoard");
        foreach (ManagementObject item in searcher.Get())
        {
            return item["Product"]?.ToString();
        }

        return null;
    }

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            list.Add(value);
        }
    }
}
