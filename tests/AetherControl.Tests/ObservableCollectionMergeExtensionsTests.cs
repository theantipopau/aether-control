using System.Collections.ObjectModel;
using AetherControl.Core.Collections;
using AetherControl.Core.Models;

namespace AetherControl.Tests;

/// <summary>
/// Pins the actual, live-captured root cause of the "flickering between ~680GB and ~340GB" storage
/// report: MergeFrom used to replace the object at an index on every call, even when the incoming
/// reading was identical in every field to what was already there — because StorageDriveInfo was a
/// plain class (reference equality) reconstructed fresh every poll. A WinUI3 ItemsControl backed by
/// a non-virtualizing panel tears its container down and rebuilds it on every such Replace, and the
/// rebuilt container's value animation starts from zero — captured directly via a UI-layer trace
/// (ui-value-trace.log): a brand-new container instance appeared roughly once a second, forever, each
/// one sweeping 0 to the real value. These tests assert the collection-mutation half of the fix
/// directly: an unchanged reading produces no Replace at all.
/// </summary>
public class ObservableCollectionMergeExtensionsTests
{
    [Fact]
    public void UnchangedItem_DoesNotReplaceTheExistingInstance_NoCollectionChangedFires()
    {
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", Model = "Predator SSD GM7 1TB", FreeBytes = 682_000_000_000, CapacityBytes = 1_000_000_000_000 }
        };
        var originalInstance = target[0];

        var changeCount = 0;
        target.CollectionChanged += (_, _) => changeCount++;

        // A freshly-constructed reading with every field identical — exactly what StorageHealthProbe
        // produces on a poll where nothing on disk changed (the overwhelmingly common case at idle).
        var identicalReading = new List<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", Model = "Predator SSD GM7 1TB", FreeBytes = 682_000_000_000, CapacityBytes = 1_000_000_000_000 }
        };

        target.MergeFrom(identicalReading, d => d.DeviceId);

        Assert.Equal(0, changeCount); // no Replace, no Insert, no Remove, no Move — nothing happened
        Assert.Same(originalInstance, target[0]); // the ORIGINAL object instance is still there
    }

    [Fact]
    public void ChangedFreeBytes_DoesReplace_RealChangesStillPropagate()
    {
        // The fix must not hide real changes — only identical readings are suppressed.
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 682_000_000_000, CapacityBytes = 1_000_000_000_000 }
        };

        var changed = new List<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 680_000_000_000, CapacityBytes = 1_000_000_000_000 }
        };

        var replaced = false;
        target.CollectionChanged += (_, e) => replaced |= e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace;

        target.MergeFrom(changed, d => d.DeviceId);

        Assert.True(replaced);
        Assert.Equal(680_000_000_000, target[0].FreeBytes);
    }

    [Fact]
    public void RepeatedIdenticalPolls_NeverReplaceAfterTheFirst_SimulatesIdleSteadyState()
    {
        // Simulates ~10 seconds of polling a drive that isn't being written to — the actual,
        // overwhelmingly common real-world condition that made the original bug visible as
        // continuous per-second flicker rather than a one-off glitch.
        var target = new ObservableCollection<StorageDriveInfo>();
        var totalReplaces = 0;
        target.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                totalReplaces++;
            }
        };

        for (var poll = 0; poll < 10; poll++)
        {
            var reading = new List<StorageDriveInfo>
            {
                new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 682_340_000_000, CapacityBytes = 1_000_000_000_000 }
            };
            target.MergeFrom(reading, d => d.DeviceId);
        }

        Assert.Equal(0, totalReplaces); // the first poll is an Insert, not a Replace; nothing changes after
    }

    [Fact]
    public void ActivelyWrittenSystemDrive_SubGigabyteByteDrift_StillCountsAsUnchanged()
    {
        // The residual gap found after the first fix: the system drive is under real, continuous
        // write activity (temp files, browser cache, logs), so its exact FreeBytes differs by a few
        // KB on nearly every poll even though the *displayed* whole-GB number never moves — a live
        // trace showed this one drive still rebuilding its card every second after the plain
        // record-equality fix, for exactly this reason. StorageDriveInfo.Equals compares at display
        // (whole GB) precision specifically to close this gap.
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 682_340_000_000, CapacityBytes = 1_000_000_000_000 }
        };
        var originalInstance = target[0];

        // A few KB less free space — real disk write activity, but still the same "635" GB a person
        // would see (682_340_000_000 bytes is ~635.48 GiB either way; 4KB doesn't move that).
        var driftedReading = new List<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 682_340_000_000 - 4096, CapacityBytes = 1_000_000_000_000 }
        };

        target.MergeFrom(driftedReading, d => d.DeviceId);

        Assert.Same(originalInstance, target[0]);
    }

    [Fact]
    public void CrossingAWholeGigabyteBoundary_DoesCountAsChanged()
    {
        // The other half of the same fix: display-precision equality must not swallow a change large
        // enough to actually move the shown number.
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 682_400_000_000, CapacityBytes = 1_000_000_000_000 } // ~635.6 GiB, displays "636"
        };
        var originalInstance = target[0];

        var reading = new List<StorageDriveInfo>
        {
            new() { DeviceId = @"\\.\PHYSICALDRIVE0", FreeBytes = 681_000_000_000, CapacityBytes = 1_000_000_000_000 } // ~634.3 GiB, displays "634"
        };

        target.MergeFrom(reading, d => d.DeviceId);

        Assert.NotSame(originalInstance, target[0]);
    }

    [Fact]
    public void ValueEqualStorageDriveInfo_AreActuallyEqual_ThisIsWhatMakesTheFixWork()
    {
        // Direct proof that the model type itself now supports the equality MergeFrom depends on —
        // the old plain-class version of StorageDriveInfo would fail this (reference equality).
        var a = new StorageDriveInfo { DeviceId = "C:", Model = "Test", FreeBytes = 100, CapacityBytes = 200 };
        var b = new StorageDriveInfo { DeviceId = "C:", Model = "Test", FreeBytes = 100, CapacityBytes = 200 };

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
    }

    [Fact]
    public void ChangingEnumerationOrder_StillConvergesToTheCorrectPositions()
    {
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = "C:", FreeBytes = 1 },
            new() { DeviceId = "D:", FreeBytes = 2 }
        };

        // D: now reported before C: — a plausible WMI/DriveInfo enumeration reorder.
        target.MergeFrom(
            new List<StorageDriveInfo> { new() { DeviceId = "D:", FreeBytes = 2 }, new() { DeviceId = "C:", FreeBytes = 1 } },
            d => d.DeviceId);

        Assert.Equal("D:", target[0].DeviceId);
        Assert.Equal("C:", target[1].DeviceId);
    }

    [Fact]
    public void RemovedVolume_IsRemovedFromTheCollection()
    {
        var target = new ObservableCollection<StorageDriveInfo>
        {
            new() { DeviceId = "C:", FreeBytes = 1 },
            new() { DeviceId = "D:", FreeBytes = 2 }
        };

        target.MergeFrom(new List<StorageDriveInfo> { new() { DeviceId = "C:", FreeBytes = 1 } }, d => d.DeviceId);

        Assert.Single(target);
        Assert.Equal("C:", target[0].DeviceId);
    }
}
