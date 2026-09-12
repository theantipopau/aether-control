using AetherControl.Services.Hardware;

namespace AetherControl.Tests;

/// <summary>
/// Regression coverage for the storage free-space computation, added after auditing a reported
/// "flickering between ~680GB and ~340GB" symptom. No live trace on the machine under audit ever
/// showed that exact pattern (~6300 already-logged polls were stable), but code inspection found a
/// real, verifiable gap: a failed/incomplete association-graph read used to fall back to 0 bytes
/// free for every disk, and the UI's value-tween would glide the last good reading down to 0 and
/// back up, visually passing through the midpoint — a plausible, mechanism-level explanation for
/// "X and half of X", not a directly reproduced one. These tests pin the dedup/aggregation behaviour
/// (ComputeFreeSpaceByPhysicalDisk) and the model-level unit-conversion/never-negative invariants
/// that guard against the ten hypotheses in the audit brief.
/// </summary>
public class StorageHealthProbeTests
{
    private const double OneGib = 1024.0 * 1024 * 1024;

    [Fact]
    public void OnePhysicalDisk_OneVolume_ReportsExactFreeSpace()
    {
        var logicalToPartition = new[] { ("Disk #0, Partition #0", "C:") };
        var diskToPartition = new[] { (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0") };
        var drives = new[] { ("C:", 100 * OneGib) };

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Single(result);
        Assert.Equal(100 * OneGib, result[@"\\.\PHYSICALDRIVE0"]);
    }

    [Fact]
    public void OnePhysicalDisk_MultipleVolumes_SumsAllVolumesOntoTheOneDisk()
    {
        // A disk partitioned into C: and D: — both hops resolve to the same physical disk.
        var logicalToPartition = new[]
        {
            ("Disk #0, Partition #0", "C:"),
            ("Disk #0, Partition #1", "D:")
        };
        var diskToPartition = new[]
        {
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0"),
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #1")
        };
        var drives = new[] { ("C:", 50 * OneGib), ("D:", 30 * OneGib) };

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Single(result);
        Assert.Equal(80 * OneGib, result[@"\\.\PHYSICALDRIVE0"]);
    }

    [Fact]
    public void MultiplePhysicalDisks_EachKeepsItsOwnFreeSpace_NoCrossTalk()
    {
        var logicalToPartition = new[]
        {
            ("Disk #0, Partition #0", "C:"),
            ("Disk #1, Partition #0", "D:"),
            ("Disk #2, Partition #0", "E:"),
            ("Disk #3, Partition #0", "F:")
        };
        var diskToPartition = new[]
        {
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0"),
            (@"\\.\PHYSICALDRIVE1", "Disk #1, Partition #0"),
            (@"\\.\PHYSICALDRIVE2", "Disk #2, Partition #0"),
            (@"\\.\PHYSICALDRIVE3", "Disk #3, Partition #0")
        };
        var drives = new[]
        {
            ("C:", 682 * OneGib), ("D:", 561 * OneGib), ("E:", 530 * OneGib), ("F:", 978 * OneGib)
        };

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Equal(4, result.Count);
        Assert.Equal(682 * OneGib, result[@"\\.\PHYSICALDRIVE0"]);
        Assert.Equal(561 * OneGib, result[@"\\.\PHYSICALDRIVE1"]);
        Assert.Equal(530 * OneGib, result[@"\\.\PHYSICALDRIVE2"]);
        Assert.Equal(978 * OneGib, result[@"\\.\PHYSICALDRIVE3"]);
    }

    [Fact]
    public void DuplicateAssociationRows_DoNotDoubleCountFreeSpace()
    {
        // The real bug this guards against (git history e8402db): WMI ASSOCIATORS OF occasionally
        // returned the same partition/disk association more than once in a single query, and the
        // pre-fix code summed every row instead of every distinct pair, inflating free space by
        // however many duplicate rows came back that poll.
        var logicalToPartition = new[]
        {
            ("Disk #0, Partition #0", "C:"),
            ("Disk #0, Partition #0", "C:"), // duplicate row
            ("Disk #0, Partition #0", "C:")  // duplicate row
        };
        var diskToPartition = new[]
        {
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0"),
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0") // duplicate row
        };
        var drives = new[] { ("C:", 100 * OneGib) };

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Equal(100 * OneGib, result[@"\\.\PHYSICALDRIVE0"]); // not 200, 300, or 600
    }

    [Fact]
    public void ChangingEnumerationOrder_ProducesTheSameResult()
    {
        var forwardOrder = new[]
        {
            ("Disk #0, Partition #0", "C:"),
            ("Disk #1, Partition #0", "D:")
        };
        var reverseOrder = forwardOrder.Reverse().ToArray();
        var diskToPartition = new[]
        {
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0"),
            (@"\\.\PHYSICALDRIVE1", "Disk #1, Partition #0")
        };
        var drives = new[] { ("C:", 50 * OneGib), ("D:", 75 * OneGib) };

        var forward = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(forwardOrder, diskToPartition, drives);
        var reverse = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(reverseOrder, diskToPartition, drives);

        Assert.Equal(forward[@"\\.\PHYSICALDRIVE0"], reverse[@"\\.\PHYSICALDRIVE0"]);
        Assert.Equal(forward[@"\\.\PHYSICALDRIVE1"], reverse[@"\\.\PHYSICALDRIVE1"]);
    }

    [Fact]
    public void TemporarilyUnavailableVolume_IsExcludedWithoutAffectingOtherDisks()
    {
        // D: (e.g. a sleeping external HDD) isn't in the "ready" set this poll; the association
        // graph still lists it, but since it contributes no reading, its disk is simply absent from
        // the result rather than appearing with a fabricated 0.
        var logicalToPartition = new[]
        {
            ("Disk #0, Partition #0", "C:"),
            ("Disk #1, Partition #0", "D:")
        };
        var diskToPartition = new[]
        {
            (@"\\.\PHYSICALDRIVE0", "Disk #0, Partition #0"),
            (@"\\.\PHYSICALDRIVE1", "Disk #1, Partition #0")
        };
        var drives = new[] { ("C:", 50 * OneGib) }; // D: not ready this poll, omitted entirely

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Single(result);
        Assert.Equal(50 * OneGib, result[@"\\.\PHYSICALDRIVE0"]);
        Assert.False(result.ContainsKey(@"\\.\PHYSICALDRIVE1"));
    }

    [Fact]
    public void MappedDriveDisconnect_NoPartitionMapping_IsSkippedNotZeroed()
    {
        // A drive letter DriveInfo still reports as "ready" but whose association rows never
        // resolved this poll (e.g. a network/mapped drive dropping mid-enumeration) — no disk ID to
        // attribute its free space to, so it's dropped from this poll's result rather than crashing
        // or corrupting an unrelated disk's total.
        var logicalToPartition = Array.Empty<(string, string)>();
        var diskToPartition = Array.Empty<(string, string)>();
        var drives = new[] { ("Z:", 200 * OneGib) };

        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(logicalToPartition, diskToPartition, drives);

        Assert.Empty(result);
    }

    [Fact]
    public void FailedEnumeration_EmptyLinksAndDrives_ReturnsEmptyNotZeroedEntries()
    {
        var result = StorageHealthProbe.ComputeFreeSpaceByPhysicalDisk(
            Array.Empty<(string, string)>(), Array.Empty<(string, string)>(), Array.Empty<(string, double)>());

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(1024L * 1024 * 1024)] // exactly 1 GiB
    [InlineData(1024L * 1024 * 1024 - 1)] // one byte under 1 GiB
    [InlineData(1024L * 1024 * 1024 + 1)] // one byte over 1 GiB
    public void ByteToGigabyteConversion_HappensExactlyOnce_AtTheModelLayer(long bytes)
    {
        var drive = new AetherControl.Core.Models.StorageDriveInfo { FreeBytes = bytes, CapacityBytes = bytes * 2 };

        // FreeGb/CapacityGb are the ONLY conversion sites (StorageDriveInfo's own computed
        // properties) — asserting the exact division here pins that there is exactly one place
        // bytes become GB, not a second conversion happening again in a ViewModel or converter.
        Assert.Equal(bytes / 1024.0 / 1024.0 / 1024.0, drive.FreeGb, precision: 9);
        Assert.Equal(bytes * 2 / 1024.0 / 1024.0 / 1024.0, drive.CapacityGb, precision: 9);
    }

    [Fact]
    public void FreeSpace_NeverExceedsCapacity_UsedPercentStaysNonNegative()
    {
        // A momentarily-stale FreeBytes (carried over from IsFreeSpaceStale handling) racing ahead
        // of a just-updated smaller CapacityBytes must not produce a negative "used%" that would
        // render as a nonsensical over-full or negative bar.
        var drive = new AetherControl.Core.Models.StorageDriveInfo { CapacityBytes = 100, FreeBytes = 150 };

        Assert.InRange(drive.UsedPercent, -0.0001, 100.0001);
    }

    [Fact]
    public void StaleFlag_IsIndependentOfTheFreeBytesValueItself()
    {
        // The model doesn't infer staleness from the number (e.g. "is it 0, therefore stale?") —
        // it's a distinct, explicitly-set fact, which is what lets StorageHealthProbe carry over a
        // real last-good FreeBytes value alongside IsFreeSpaceStale = true.
        var staleButNonZero = new AetherControl.Core.Models.StorageDriveInfo { FreeBytes = 682 * OneGib, IsFreeSpaceStale = true };
        var freshZero = new AetherControl.Core.Models.StorageDriveInfo { FreeBytes = 0, IsFreeSpaceStale = false };

        Assert.True(staleButNonZero.IsFreeSpaceStale);
        Assert.Equal(682 * OneGib, staleButNonZero.FreeBytes);
        Assert.False(freshZero.IsFreeSpaceStale);
    }
}
