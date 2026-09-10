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
/// every second, visible as the value flicking between two readings.
/// <para>
/// A DeviceId sort on the WMI side made that fallback deterministic but the
/// swapping still recurred (reported live: one drive's free space alternating
/// between two real values ~300GB apart — the same "wrong drive at this card
/// position" signature, just not fully eliminated). Rather than chase the
/// exact remaining trigger for fuzzy model-name matching being occasionally
/// unstable, the matching itself is now only ever performed <b>once</b> per
/// physical disk: the first successful match is cached by (LHM identifier →
/// WMI DeviceID), and every later poll looks up that exact DeviceID instead
/// of re-running fuzzy matching. Whatever made re-matching occasionally
/// unstable can't matter anymore if matching only happens once.
/// </para>
/// </summary>
internal static class StorageHealthProbe
{
    // LHM Identifier -> WMI DeviceID (e.g. "\\.\PHYSICALDRIVE0"), resolved once via fuzzy
    // model-name matching and reused every subsequent poll. Re-resolved only if a cached
    // DeviceID stops appearing in the current WMI results (drive unplugged/system changed).
    private static readonly Dictionary<string, string> LhmToWmiDeviceId = new();

    public static IReadOnlyList<StorageDriveInfo> Enrich(IReadOnlyList<StorageDriveInfo> lhmDrives)
    {
        var wmiDrives = QueryPhysicalDisks();
        var wmiByDeviceId = wmiDrives.ToDictionary(d => d.DeviceId);
        var unclaimed = new List<StorageDriveInfo>(wmiDrives);

        var enriched = new List<StorageDriveInfo>(lhmDrives.Count);
        foreach (var drive in lhmDrives)
        {
            StorageDriveInfo? wmiMatch = null;

            if (LhmToWmiDeviceId.TryGetValue(drive.DeviceId, out var cachedDeviceId) &&
                wmiByDeviceId.TryGetValue(cachedDeviceId, out var cachedMatch))
            {
                wmiMatch = cachedMatch;
                unclaimed.Remove(cachedMatch);
            }
            else
            {
                wmiMatch = FindBestModelMatch(drive.Model, unclaimed);
                if (wmiMatch is not null)
                {
                    unclaimed.Remove(wmiMatch);
                    LhmToWmiDeviceId[drive.DeviceId] = wmiMatch.DeviceId;
                }
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

        // Real evidence from Matt's machine ruled out a drive-identity mix-up: all four distinct
        // physical drives jumped by a correlated ~1.55-1.59x at once (637→988GB, 435→680GB,
        // 356→562GB, 335→532GB) — a pure "wrong drive shown here" bug can't move every drive in
        // the same direction together. That points at this method double-counting a logical
        // drive's free space into a disk's total, which WMI associator queries are known to do
        // occasionally (duplicate rows for the same partition/disk pair, a real provider quirk,
        // not specific to this hardware). This set makes each (logical drive, physical disk) pair
        // contribute its free space at most once per poll, regardless of how many duplicate rows
        // WMI returns for it that particular time — the actual per-poll trigger for the duplication
        // itself was never pinned down, but it can't inflate a total if it's deduplicated here.
        var countedContributions = new HashSet<(string LogicalDeviceId, string DiskId)>();

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady))
        {
            var logicalDeviceId = drive.Name.TrimEnd('\\', '/');
            var freeGb = drive.TotalFreeSpace / 1024.0 / 1024.0 / 1024.0;
            try
            {
                using var partitionSearcher = new ManagementObjectSearcher(
                    $"ASSOCIATORS OF {{Win32_LogicalDisk.DeviceID='{logicalDeviceId}'}} WHERE AssocClass=Win32_LogicalDiskToPartition");

                var partitionIds = new HashSet<string>();
                foreach (ManagementBaseObject partition in partitionSearcher.Get())
                {
                    var partitionId = partition["DeviceID"]?.ToString();
                    if (!string.IsNullOrEmpty(partitionId))
                    {
                        partitionIds.Add(partitionId);
                    }
                }

                var diskIds = new HashSet<string>();
                foreach (var partitionId in partitionIds)
                {
                    using var diskSearcher = new ManagementObjectSearcher(
                        $"ASSOCIATORS OF {{Win32_DiskPartition.DeviceID='{partitionId}'}} WHERE AssocClass=Win32_DiskDriveToDiskPartition");

                    foreach (ManagementBaseObject disk in diskSearcher.Get())
                    {
                        var diskId = disk["DeviceID"]?.ToString();
                        if (!string.IsNullOrEmpty(diskId))
                        {
                            diskIds.Add(diskId);
                        }
                    }
                }

                StorageDiagnosticLog.Write(
                    $"{logicalDeviceId} free={freeGb:0.00}GB partitions=[{string.Join(",", partitionIds)}] disks=[{string.Join(",", diskIds)}]");

                foreach (var diskId in diskIds)
                {
                    if (countedContributions.Add((logicalDeviceId, diskId)))
                    {
                        freeSpaceByDiskId[diskId] = freeSpaceByDiskId.GetValueOrDefault(diskId, 0) + drive.TotalFreeSpace;
                    }
                    else
                    {
                        StorageDiagnosticLog.Write($"  {logicalDeviceId} -> {diskId} SKIPPED (already counted this poll)");
                    }
                }
            }
            catch (ManagementException ex)
            {
                StorageDiagnosticLog.Write($"{logicalDeviceId} WMI EXCEPTION: {ex.Message}");
            }
        }

        StorageDiagnosticLog.Write(
            $"RESULT: {string.Join(" | ", freeSpaceByDiskId.Select(kv => $"{kv.Key}={kv.Value / 1024.0 / 1024.0 / 1024.0:0.00}GB"))}");

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
