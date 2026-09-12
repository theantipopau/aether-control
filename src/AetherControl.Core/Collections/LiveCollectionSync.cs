using System.Collections.ObjectModel;

namespace AetherControl.Core.Collections;

/// <summary>
/// Owns an <see cref="ObservableCollection{TViewModel}"/> of long-lived, stable-identity view models
/// and keeps it in sync with a fresh list of snapshots every poll — <c>Add</c> only for a genuinely
/// new key, <c>Remove</c> only for a key absent long enough to pass <paramref name="maxMissedSyncs"/>
/// consecutive polls, and for every key that's still present, an in-place <c>Apply</c> call onto the
/// SAME view-model instance. No <c>Replace</c> is ever issued for an existing key, so a bound
/// <c>ItemsControl</c> never has a reason to tear down and rebuild that container — the view model
/// itself is responsible for raising <c>PropertyChanged</c> only for the specific properties that
/// actually changed (a plain <see cref="ObservableObject"/> with <c>SetProperty</c> per field already
/// does this for free).
/// <para>
/// This replaces the earlier <c>ObservableCollectionMergeExtensions.MergeFrom</c> approach for
/// collections whose items carry naturally-volatile telemetry (temperature, activity, free space)
/// alongside a stable identity. That approach worked by comparing whole snapshot records for
/// equality and skipping the indexer assignment when equal — but any single volatile field (even one
/// nobody's looking at) makes the whole record compare unequal, so it degenerated into a chase of
/// "add another field to the equality exclusion list" for real drives under continuous real activity.
/// Separating "stable identity + who owns raising change notifications" from "immutable snapshot
/// data" removes the need for whole-object equality entirely.
/// </para>
/// </summary>
public sealed class LiveCollectionSync<TViewModel, TSnapshot, TKey>
    where TKey : notnull
{
    private readonly Func<TSnapshot, TKey> _snapshotKey;
    private readonly Func<TViewModel, TKey> _viewModelKey;
    private readonly Func<TSnapshot, TViewModel> _create;
    private readonly Action<TViewModel, TSnapshot> _apply;
    private readonly int _maxMissedSyncs;
    private readonly Dictionary<TKey, int> _missedSyncsByKey = new();

    public ObservableCollection<TViewModel> Items { get; } = [];

    /// <param name="snapshotKey">Stable identity extracted from an incoming snapshot (e.g. a physical disk's WMI DeviceID).</param>
    /// <param name="viewModelKey">The same identity extracted from an existing view model.</param>
    /// <param name="create">Constructs a brand-new view model the first time a key appears.</param>
    /// <param name="apply">Mutates an existing view model's properties from a fresh snapshot — the one place per-field change detection lives (see the type's own doc comment).</param>
    /// <param name="maxMissedSyncs">Consecutive syncs a previously-seen key may be absent from before its view model is removed — absorbs a single transient miss (a WMI hiccup, one dropped poll) without visibly dropping and re-adding a card. 1 removes immediately on first absence.</param>
    public LiveCollectionSync(
        Func<TSnapshot, TKey> snapshotKey,
        Func<TViewModel, TKey> viewModelKey,
        Func<TSnapshot, TViewModel> create,
        Action<TViewModel, TSnapshot> apply,
        int maxMissedSyncs = 3)
    {
        _snapshotKey = snapshotKey;
        _viewModelKey = viewModelKey;
        _create = create;
        _apply = apply;
        _maxMissedSyncs = maxMissedSyncs;
    }

    public void Sync(IReadOnlyList<TSnapshot> source)
    {
        var seenKeys = new HashSet<TKey>();

        foreach (var snapshot in source)
        {
            var key = _snapshotKey(snapshot);
            seenKeys.Add(key);
            _missedSyncsByKey[key] = 0;

            var existing = FindByKey(key);
            if (existing is not null)
            {
                _apply(existing, snapshot);
            }
            else
            {
                Items.Add(_create(snapshot));
            }
        }

        foreach (var key in _missedSyncsByKey.Keys.Where(k => !seenKeys.Contains(k)).ToList())
        {
            var missed = ++_missedSyncsByKey[key];
            if (missed < _maxMissedSyncs)
            {
                continue;
            }

            var toRemove = FindByKey(key);
            if (toRemove is not null)
            {
                Items.Remove(toRemove);
            }

            _missedSyncsByKey.Remove(key);
        }
    }

    private TViewModel? FindByKey(TKey key)
    {
        foreach (var item in Items)
        {
            if (EqualityComparer<TKey>.Default.Equals(_viewModelKey(item), key))
            {
                return item;
            }
        }

        return default;
    }
}
