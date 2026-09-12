using AetherControl.Core.Collections;

namespace AetherControl.Tests;

/// <summary>
/// Covers the collection-identity guarantees <see cref="LiveCollectionSync{TViewModel,TSnapshot,TKey}"/>
/// makes — the actual fix for the confirmed root cause of the storage "flickering between ~680GB and
/// ~340GB" report (see ROADMAP.md Phase 32): a stable-identity view model created once per key and
/// updated in place forever after, with the collection itself only ever seeing Add (new key) or
/// Remove (a key absent long enough to pass the absence policy) — never Replace. Uses small
/// test-only fake types rather than the real StorageDriveViewModel/StorageDriveInfo, since those live
/// in the App project (a different, WinUI3-specific target framework the test project can't
/// reference) — this exercises the exact same generic logic those real types run through.
/// </summary>
public class LiveCollectionSyncTests
{
    private sealed class FakeSnapshot
    {
        public required string Id { get; init; }
        public double Value { get; init; }
        public double Temperature { get; init; }
    }

    private sealed class FakeViewModel
    {
        public string Id { get; }
        public double Value { get; private set; }
        public double Temperature { get; private set; }
        public int ApplyCallCount { get; private set; }

        public FakeViewModel(FakeSnapshot snapshot)
        {
            Id = snapshot.Id;
            Apply(snapshot);
        }

        public void Apply(FakeSnapshot snapshot)
        {
            Value = snapshot.Value;
            Temperature = snapshot.Temperature;
            ApplyCallCount++;
        }
    }

    private static LiveCollectionSync<FakeViewModel, FakeSnapshot, string> CreateSync(int maxMissedSyncs = 3) =>
        new(snapshotKey: s => s.Id, viewModelKey: vm => vm.Id, create: s => new FakeViewModel(s), apply: (vm, s) => vm.Apply(s), maxMissedSyncs);

    [Fact]
    public void NewDevice_CreatesExactlyOneCard()
    {
        var sync = CreateSync();

        sync.Sync([new FakeSnapshot { Id = "C:", Value = 1 }]);

        Assert.Single(sync.Items);
        Assert.Equal("C:", sync.Items[0].Id);
    }

    [Fact]
    public void ChangedTemperature_DoesNotReplaceTheCard_UpdatesInPlace()
    {
        var sync = CreateSync();
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 100, Temperature = 30 }]);
        var original = sync.Items[0];

        sync.Sync([new FakeSnapshot { Id = "C:", Value = 100, Temperature = 45 }]); // only temperature changed

        Assert.Same(original, sync.Items[0]); // same instance — no Replace, no card recreation
        Assert.Equal(45, sync.Items[0].Temperature);
        Assert.Equal(2, original.ApplyCallCount);
    }

    [Fact]
    public void ChangedValue_UpdatesTheExistingCardInPlace()
    {
        var sync = CreateSync();
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 100 }]);
        var original = sync.Items[0];

        sync.Sync([new FakeSnapshot { Id = "C:", Value = 95 }]);

        Assert.Same(original, sync.Items[0]);
        Assert.Equal(95, sync.Items[0].Value);
    }

    [Fact]
    public void SameDeviceId_RetainsTheSameViewModelInstanceAcrossManyPolls()
    {
        var sync = CreateSync();
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 100 }]);
        var original = sync.Items[0];

        for (var i = 0; i < 50; i++)
        {
            sync.Sync([new FakeSnapshot { Id = "C:", Value = 100 - i * 0.001 }]); // continuous tiny real drift
        }

        Assert.Same(original, sync.Items[0]);
        Assert.Single(sync.Items); // never duplicated, never recreated
    }

    [Fact]
    public void EnumerationOrderChanging_DoesNotRecreateEitherCard()
    {
        var sync = CreateSync();
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 1 }, new FakeSnapshot { Id = "D:", Value = 2 }]);
        var cOriginal = sync.Items.Single(vm => vm.Id == "C:");
        var dOriginal = sync.Items.Single(vm => vm.Id == "D:");

        // D: reported before C: this poll — a plausible WMI/DriveInfo enumeration reorder.
        sync.Sync([new FakeSnapshot { Id = "D:", Value = 2 }, new FakeSnapshot { Id = "C:", Value = 1 }]);

        Assert.Same(cOriginal, sync.Items.Single(vm => vm.Id == "C:"));
        Assert.Same(dOriginal, sync.Items.Single(vm => vm.Id == "D:"));
    }

    [Fact]
    public void RemovedDevice_IsNotRemovedImmediately_AbsorbsATransientSingleMiss()
    {
        var sync = CreateSync(maxMissedSyncs: 3);
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 1 }]);

        sync.Sync([]); // one missed poll — e.g. a transient WMI hiccup

        Assert.Single(sync.Items); // still there
    }

    [Fact]
    public void RemovedDevice_IsRemovedAfterReachingTheAbsencePolicyThreshold()
    {
        var sync = CreateSync(maxMissedSyncs: 3);
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 1 }]);

        sync.Sync([]);
        sync.Sync([]);
        Assert.Single(sync.Items); // 2 misses, still under the threshold of 3

        sync.Sync([]);
        Assert.Empty(sync.Items); // 3rd consecutive miss — genuinely gone
    }

    [Fact]
    public void DeviceReappearingBeforeAbsenceThreshold_CancelsTheMissCounter_SameInstanceReused()
    {
        var sync = CreateSync(maxMissedSyncs: 3);
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 1 }]);
        var original = sync.Items[0];

        sync.Sync([]); // 1 miss
        sync.Sync([new FakeSnapshot { Id = "C:", Value = 2 }]); // back before removal — resets the counter
        sync.Sync([]);
        sync.Sync([]);

        Assert.Single(sync.Items); // still present — the reappearance reset the miss count to 0
        Assert.Same(original, sync.Items[0]);
    }

    [Fact]
    public void MultipleUnrelatedDevices_EachGetsItsOwnStableCard()
    {
        var sync = CreateSync();
        sync.Sync([
            new FakeSnapshot { Id = "C:", Value = 1 },
            new FakeSnapshot { Id = "D:", Value = 2 },
            new FakeSnapshot { Id = "E:", Value = 3 }
        ]);

        Assert.Equal(3, sync.Items.Count);
        Assert.Equal(["C:", "D:", "E:"], sync.Items.Select(vm => vm.Id));
    }
}
