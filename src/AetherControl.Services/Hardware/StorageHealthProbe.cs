using System.Management;
using AetherControl.Core.Enums;
using AetherControl.Core.Models;

namespace AetherControl.Services.Hardware;

/// <summary>
/// Enriches LibreHardwareMonitor's per-drive temperature readings with
/// capacity, free space and a coarse health verdict pulled from WMI.
/// LHM and WMI enumerate physical disks independently, so drives are matched
/// by model name rather than list position — an earlier version paired them
/// ordinally ("both APIs return them in the same enclosure order"), which
/// turned out not to hold poll-to-poll: WMI's enumeration order isn't
/// guaranteed stable, so on a multi-drive system the free-space figure shown
/// on a given card could silently swap to a different physical disk's number
/// every second, visible as the value flicking between two readings. Model
/// names don't change between polls, so matching on those is actually stable.
/// </summary>
internal static class StorageHealthProbe
{
    public static IReadOnlyList<StorageDriveInfo> Enrich(IReadOnlyList<StorageDriveInfo> lhmDrives)
    {
        var unclaimed = new List<StorageDriveInfo>(QueryPhysicalDisks());
        var enriched = new List<StorageDriveInfo>(lhmDrives.Count);

        foreach (var drive in lhmDrives)
        {
            var wmiMatch = FindBestModelMatch(drive.Model, unclaimed);
            if (wmiMatch is not null)
            {
                unclaimed.Remove(wmiMatch);
            }

            enriched.Add(new StorageDriveInfo
            {
                DeviceId = drive.DeviceId,
                Model = drive.Model,
                TemperatureCelsius = drive.TemperatureCelsius,
                IsNvme = drive.IsNvme,
                CapacityBytes = wmiMatch?.CapacityBytes ?? 0,
                FreeBytes = wmiMatch?.FreeBytes ?? 0,
                Health = wmiMatch?.Health ?? DriveHealthStatus.Unknown
            });
        }

        return enriched;
    }

    private static StorageDriveInfo? FindBestModelMatch(string lhmModel, List<StorageDriveInfo> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(lhmModel))
        {
            return candidates[0]; // nothing to key off for this one drive — last-resort positional pick
        }

        var normalizedLhm = Normalize(lhmModel);
        var match = candidates.FirstOrDefault(candidate =>
        {
            var normalizedWmi = Normalize(candidate.Model);
            return normalizedWmi.Contains(normalizedLhm, StringComparison.OrdinalIgnoreCase)
                   || normalizedLhm.Contains(normalizedWmi, StringComparison.OrdinalIgnoreCase);
        });

        return match ?? candidates[0];
    }

    private static string Normalize(string value) => value.Replace(" ", string.Empty).Trim();

    private static List<StorageDriveInfo> QueryPhysicalDisks()
    {
        var results = new List<StorageDriveInfo>();
        try
        {
            var freeSpaceByDiskId = BuildFreeSpaceByPhysicalDisk();

            using var searcher = new ManagementObjectSearcher("SELECT DeviceID, Model, Size, Status FROM Win32_DiskDrive");
            foreach (ManagementObject disk in searcher.Get())
            {
                var status = disk["Status"]?.ToString() ?? "Unknown";
                var health = status.Equals("OK", StringComparison.OrdinalIgnoreCase)
                    ? DriveHealthStatus.Good
                    : DriveHealthStatus.Caution;

                var capacity = disk["Size"] is not null ? Convert.ToDouble(disk["Size"]) : 0;
                var deviceId = disk["DeviceID"]?.ToString() ?? string.Empty;

                results.Add(new StorageDriveInfo
                {
                    DeviceId = deviceId,
                    Model = disk["Model"]?.ToString() ?? string.Empty,
                    CapacityBytes = capacity,
                    FreeBytes = freeSpaceByDiskId.GetValueOrDefault(deviceId, 0),
                    Health = health
                });
            }

            // WMI's own enumeration order for this query isn't guaranteed stable between separate
            // calls (unlike LHM's cached hardware list, which really is stable — this turned out to
            // be the actual remaining source of "flicker": FindBestModelMatch's fallback-to-index-0
            // path was itself picking from a differently-ordered list each poll whenever a model
            // name failed to match cleanly). Sorting by DeviceID (\\.\PHYSICALDRIVEn, assigned once
            // by Windows and stable for the session) makes even that fallback deterministic.
            results.Sort((a, b) => string.CompareOrdinal(a.DeviceId, b.DeviceId));

            ApplySmartFailurePrediction(results);
        }
        catch (ManagementException)
        {
            // WMI can be unavailable in locked-down environments; callers fall back to Unknown health.
        }

        return results;
    }

    /// <summary>
    /// Walks Win32_LogicalDisk → Win32_LogicalDiskToPartition → Win32_DiskDriveToDiskPartition
    /// — logical volume up to physical disk, the same direction (and the same unescaped DeviceID
    /// handling) Portrait Stats uses to resolve its system drive. An earlier version of this walked
    /// the opposite direction (physical disk down to logical volume) with backslash-escaped
    /// DeviceIDs for Win32_DiskDrive, which matched zero partitions on real hardware and made every
    /// drive report 0 bytes free — going through DriveInfo.GetDrives() first sidesteps needing to
    /// escape a DeviceID like \\.\PHYSICALDRIVE0 at all, since neither a drive letter ("C:") nor a
    /// partition DeviceID ("Disk #0, Partition #0") contains a backslash.
    /// </summary>
    private static Dictionary<string, double> BuildFreeSpaceByPhysicalDisk()
    {
        var freeSpaceByDiskId = new Dictionary<string, double>();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var logicalDeviceId = drive.Name.TrimEnd('\\', '/');
            try
            {
                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{logicalDeviceId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                foreach (ManagementBaseObject partition in partitionSearcher.Get())
                {
                    var partitionId = partition["DeviceID"]?.ToString();
                    if (string.IsNullOrEmpty(partitionId))
                    {
                        continue;
                    }

                    using var diskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementBaseObject disk in diskSearcher.Get())
                    {
                        var diskId = disk["DeviceID"]?.ToString();
                        if (string.IsNullOrEmpty(diskId))
                        {
                            continue;
                        }

                        freeSpaceByDiskId[diskId] = freeSpaceByDiskId.GetValueOrDefault(diskId, 0) + drive.TotalFreeSpace;
                    }
                }
            }
            catch (ManagementException)
            {
            }
        }

        return freeSpaceByDiskId;
    }

    private static void ApplySmartFailurePrediction(List<StorageDriveInfo> drives)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                @"root\wmi",
                "SELECT InstanceName, PredictFailure FROM MSStorageDriver_FailurePredictStatus");

            foreach (ManagementObject result in searcher.Get())
            {
                if (result["PredictFailure"] is bool predictFailure && predictFailure)
                {
                    // Matching InstanceName to a specific PNPDeviceID/drive is left as a future
                    // enhancement; until then a predicted failure flags every drive for attention
                    // rather than risk silently hiding it behind the wrong index.
                    foreach (var drive in drives)
                    {
                        drive.Health = DriveHealthStatus.Bad;
                    }
                }
            }
        }
        catch (ManagementException)
        {
        }
    }
}
