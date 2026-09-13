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

    // Last successfully-read free space per WMI disk DeviceID. A transient failure (the bulk
    // association query throwing, or one disk's partitions not resolving that specific poll) falls
    // back to this instead of reporting 0 bytes free — "the poll failed" and "the disk is full" are
    // different facts, and showing 0 for the former is exactly what made a failed poll look like a
    // real, dramatic capacity change (see StorageDriveInfo.FreeSpaceQuality).
    private static readonly Dictionary<string, double> LastGoodFreeBytesByDiskId = new();

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
                TemperatureQuality = drive.TemperatureQuality,
                IsNvme = drive.IsNvme,
                CapacityBytes = wmiMatch?.CapacityBytes ?? 0,
                FreeBytes = wmiMatch?.FreeBytes ?? 0,
                Health = wmiMatch?.Health ?? DriveHealthStatus.Unknown,
                FreeSpaceQuality = wmiMatch?.FreeSpaceQuality ?? MetricQuality.Unavailable
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

                // A disk missing from this poll's association graph (transient WMI hiccup, or the
                // bulk query throwing entirely — see BuildFreeSpaceByPhysicalDisk) falls back to the
                // last successfully-read value for this exact DeviceID, marked stale, rather than 0.
                // Confirmed via code audit (not a live-captured trace) that the previous behaviour —
                // GetValueOrDefault(deviceId, 0) — made a transient failure display as "0 bytes
                // free", and MetricCard's NumberTween would then glide the previous good value down
                // to 0 and back up on the next successful poll, visually passing through the
                // midpoint. That's a plausible, code-verified mechanism for a value that looks like
                // it's "flickering between X and half of X" — not something caught in the act on
                // this machine, since the association graph hasn't failed once in ~6300 logged polls.
                double freeBytes;
                MetricQuality freeSpaceQuality;
                if (freeSpaceByDiskId.TryGetValue(deviceId, out var freshFreeBytes))
                {
                    freeBytes = freshFreeBytes;
                    freeSpaceQuality = MetricQuality.Good;
                    LastGoodFreeBytesByDiskId[deviceId] = freshFreeBytes;
                }
                else if (LastGoodFreeBytesByDiskId.TryGetValue(deviceId, out var lastGoodFreeBytes))
                {
                    // A real prior reading exists — just not confirmed as of this poll.
                    freeBytes = lastGoodFreeBytes;
                    freeSpaceQuality = MetricQuality.Stale;
                    StorageDiagnosticLog.Write($"{deviceId} STALE — no fresh free-space reading this poll, using last good {freeBytes / 1024.0 / 1024.0 / 1024.0:0.00}GB");
                }
                else
                {
                    // Never seen a real value for this disk at all yet (first poll, or genuinely
                    // new hardware) — there is no "last good" to fall back to, so 0 here means
                    // "unknown", not "confirmed empty".
                    freeBytes = 0;
                    freeSpaceQuality = MetricQuality.Unavailable;
                }

                results.Add(new StorageDriveInfo
                {
                    DeviceId = deviceId,
                    Model = disk["Model"]?.ToString() ?? string.Empty,
                    CapacityBytes = capacity,
                    FreeBytes = freeBytes,
                    Health = health,
                    FreeSpaceQuality = freeSpaceQuality
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
    /// <summary>
    /// Was 2 ASSOCIATORS-OF queries per drive (8 WMI round trips for Matt's 4-drive machine, every
    /// single poll). Each ASSOCIATORS OF call is its own separate query against WMI's provider —
    /// nothing guarantees the 8 calls in one poll observe a perfectly consistent snapshot of the
    /// system if anything changes state mid-poll (a drive letter remount, Explorer touching a volume,
    /// etc.), which was the remaining unproven suspect after ruling out double-counted associator
    /// rows. Two bulk, unfiltered queries — the whole of Win32_LogicalDiskToPartition and
    /// Win32_DiskDriveToDiskPartition, matched locally in memory — get the exact same associations in
    /// 2 round trips instead of 8, closing that window. This is also just how the Windows Storage
    /// Management stack (Get-Partition/Get-Disk) does it: one bulk read of each association table,
    /// not N queries keyed on a specific object.
    /// </summary>
    private static Dictionary<string, double> BuildFreeSpaceByPhysicalDisk()
    {
        var logicalToPartitionLinks = new List<(string PartitionId, string LogicalDeviceId)>();
        var diskToPartitionLinks = new List<(string DiskId, string PartitionId)>();

        try
        {
            using var logicalToPartition = new ManagementObjectSearcher("SELECT * FROM Win32_LogicalDiskToPartition");
            foreach (ManagementBaseObject link in logicalToPartition.Get())
            {
                var partitionId = ExtractDeviceId(link["Antecedent"]?.ToString());
                var logicalDeviceId = ExtractDeviceId(link["Dependent"]?.ToString());
                if (partitionId is not null && logicalDeviceId is not null)
                {
                    logicalToPartitionLinks.Add((partitionId, logicalDeviceId));
                }
            }

            using var diskToPartition = new ManagementObjectSearcher("SELECT * FROM Win32_DiskDriveToDiskPartition");
            foreach (ManagementBaseObject link in diskToPartition.Get())
            {
                var diskId = ExtractDeviceId(link["Antecedent"]?.ToString());
                var partitionId = ExtractDeviceId(link["Dependent"]?.ToString());
                if (diskId is not null && partitionId is not null)
                {
                    diskToPartitionLinks.Add((diskId, partitionId));
                }
            }
        }
        catch (ManagementException ex)
        {
            StorageDiagnosticLog.Write($"BULK QUERY EXCEPTION: {ex.Message}");
            // Empty on a failed bulk query — every disk falls back to QueryPhysicalDisks' own
            // last-good-value/stale handling rather than this method inventing a 0 of its own.
            return new Dictionary<string, double>();
        }

        var readyDrives = DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => (LogicalDeviceId: d.Name.TrimEnd('\\', '/'), FreeBytes: (double)d.TotalFreeSpace))
            .ToList();

        var result = ComputeFreeSpaceByPhysicalDisk(logicalToPartitionLinks, diskToPartitionLinks, readyDrives, StorageDiagnosticLog.Write);

        StorageDiagnosticLog.Write(
            $"RESULT: {string.Join(" | ", result.Select(kv => $"{kv.Key}={kv.Value / 1024.0 / 1024.0 / 1024.0:0.00}GB"))}");

        return result;
    }

    /// <summary>
    /// Pure core of the free-space computation — no WMI, no <see cref="DriveInfo"/>, just the
    /// association graph and each ready logical drive's own reported free space. Deterministic and
    /// unit-testable: <see cref="StorageHealthProbeTests"/> exercises one-disk/one-volume,
    /// one-disk/many-volumes, many-disks, duplicate association rows, and out-of-order input against
    /// this directly, none of which need a real machine's WMI provider to reproduce.
    /// </summary>
    internal static Dictionary<string, double> ComputeFreeSpaceByPhysicalDisk(
        IReadOnlyCollection<(string PartitionId, string LogicalDeviceId)> logicalToPartitionLinks,
        IReadOnlyCollection<(string DiskId, string PartitionId)> diskToPartitionLinks,
        IReadOnlyCollection<(string LogicalDeviceId, double FreeBytes)> readyLogicalDrives,
        Action<string>? log = null)
    {
        var partitionsByLogicalDisk = new Dictionary<string, HashSet<string>>();
        foreach (var (partitionId, logicalDeviceId) in logicalToPartitionLinks)
        {
            if (!partitionsByLogicalDisk.TryGetValue(logicalDeviceId, out var partitions))
            {
                partitions = [];
                partitionsByLogicalDisk[logicalDeviceId] = partitions;
            }

            // A HashSet, not a List — the same (partition, logical disk) association appearing as a
            // duplicate WMI row more than once collapses to one membership instead of inflating any
            // downstream count. This is what "duplicate enumeration records" resolves to here.
            partitions.Add(partitionId);
        }

        var disksByPartition = new Dictionary<string, HashSet<string>>();
        foreach (var (diskId, partitionId) in diskToPartitionLinks)
        {
            if (!disksByPartition.TryGetValue(partitionId, out var disks))
            {
                disks = [];
                disksByPartition[partitionId] = disks;
            }

            disks.Add(diskId);
        }

        var freeSpaceByDiskId = new Dictionary<string, double>();

        // Each (logical drive, physical disk) pair contributes its free space at most once,
        // regardless of how many partition hops connect them or how many times either association
        // table lists the same link — guards against the WMI duplicate-association-row quirk that
        // caused a real, evidence-confirmed over-count before (see git history: e8402db).
        var countedContributions = new HashSet<(string LogicalDeviceId, string DiskId)>();

        foreach (var (logicalDeviceId, freeBytes) in readyLogicalDrives)
        {
            if (!partitionsByLogicalDisk.TryGetValue(logicalDeviceId, out var partitionIds))
            {
                log?.Invoke($"{logicalDeviceId} free={freeBytes / 1024.0 / 1024.0 / 1024.0:0.00}GB NO PARTITIONS FOUND");
                continue;
            }

            var diskIds = new HashSet<string>();
            foreach (var partitionId in partitionIds)
            {
                if (disksByPartition.TryGetValue(partitionId, out var disks))
                {
                    diskIds.UnionWith(disks);
                }
            }

            log?.Invoke($"{logicalDeviceId} free={freeBytes / 1024.0 / 1024.0 / 1024.0:0.00}GB partitions=[{string.Join(",", partitionIds)}] disks=[{string.Join(",", diskIds)}]");

            foreach (var diskId in diskIds)
            {
                if (countedContributions.Add((logicalDeviceId, diskId)))
                {
                    freeSpaceByDiskId[diskId] = freeSpaceByDiskId.GetValueOrDefault(diskId, 0) + freeBytes;
                }
                else
                {
                    log?.Invoke($"  {logicalDeviceId} -> {diskId} SKIPPED (already counted this poll)");
                }
            }
        }

        return freeSpaceByDiskId;
    }

    /// <summary>
    /// WMI association properties (Antecedent/Dependent) come back as embedded object paths, e.g.
    /// <c>\\HOST\root\cimv2:Win32_DiskDrive.DeviceID="\\\\.\\PHYSICALDRIVE0"</c> — the DeviceID's own
    /// backslashes are doubled inside the path string. Pulls the DeviceID value back out and
    /// un-escapes it to match the plain DeviceID strings <c>Win32_DiskDrive</c> etc. return directly
    /// (e.g. from <see cref="QueryPhysicalDisks"/>'s own <c>SELECT ... FROM Win32_DiskDrive</c>).
    /// </summary>
    private static string? ExtractDeviceId(string? wmiPath)
    {
        if (string.IsNullOrEmpty(wmiPath))
        {
            return null;
        }

        const string marker = "DeviceID=\"";
        var start = wmiPath.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = wmiPath.IndexOf('"', start);
        if (end < 0)
        {
            return null;
        }

        return wmiPath[start..end].Replace("\\\\", "\\");
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
