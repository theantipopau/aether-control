using System.Collections.ObjectModel;
using AetherControl.Core.Collections;
using AetherControl.Core.Models;

namespace AetherControl.Tests;

/// <summary>
/// <c>MergeFrom</c> is generic collection-sync infrastructure (still used in production for Portrait
/// Mode's Fans list, whose <c>PortraitFanRow</c> already carries pre-formatted, display-quantized
/// strings — so whole-record equality is the right tool there). These tests exercise its mechanics
/// using <see cref="StorageDriveInfo"/> only as a convenient test type; storage itself has since moved
/// to <see cref="LiveCollectionSync{TViewModel,TSnapshot,TKey}"/> + a long-lived
/// <c>StorageDriveViewModel</c> per device (see <c>LiveCollectionSyncTests</c> and ROADMAP.md
/// Phase 32) specifically because whole-object equality degenerates into an equality-exclusion chase
/// for any type carrying naturally-volatile telemetry alongside a stable identity.
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

        // A field-identical, separately-constructed reading — this is what MergeFrom is actually
        // meant to no-op on (e.g. Portrait Mode's Fans list on a poll where no RPM rounded differently).
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
    public void ValueEqualRecord_ComparesEqualDespiteBeingASeparateInstance()
    {
        // What makes the no-op-on-identical-reading behaviour above possible at all — a plain class
        // (reference equality) would fail this.
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
