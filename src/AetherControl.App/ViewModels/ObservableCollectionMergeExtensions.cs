using System.Collections.ObjectModel;

namespace AetherControl.App.ViewModels;

/// <summary>
/// Updates an <see cref="ObservableCollection{T}"/>'s contents to match a fresh snapshot in place —
/// replacing changed items via the indexer (fires <c>CollectionChanged</c> with <c>Replace</c>, not
/// <c>Reset</c>) and only inserting/removing when the key set itself changes — instead of ever
/// reassigning the collection's own reference.
/// <para>
/// This is why the Dashboard's storage/process/sensor tiles read as "jumping around" even once the
/// underlying sensor values were confirmed rock-stable: every poll handed <c>ItemsControl</c> a
/// brand-new <see cref="IReadOnlyList{T}"/> instance, which forces it to tear down and recreate
/// every realized <c>MetricCard</c> container from scratch — and a freshly-constructed
/// <c>MetricCard</c>'s <c>NumberTween</c> starts from zero, so its very first
/// <c>NumericValue</c> update glides 0 → the real value over ~220ms, every single second, forever.
/// Reusing the same collection instance and updating entries via the indexer instead lets
/// <c>ItemsControl</c> recycle the existing container for a key that's still present, so the same
/// live <c>NumberTween</c> glides from its *previous* value the way the CPU/GPU/RAM tiles (bound
/// directly to scalar view-model properties, never recreated) already correctly do.
/// </para>
/// </summary>
internal static class ObservableCollectionMergeExtensions
{
    public static void MergeFrom<T, TKey>(this ObservableCollection<T> target, IReadOnlyList<T> source, Func<T, TKey> keySelector)
        where TKey : notnull
    {
        for (var i = 0; i < source.Count; i++)
        {
            var item = source[i];
            var key = keySelector(item);

            // Keys are unique within the list, and positions [0, i) are already settled from
            // earlier iterations, so a still-present match can only be at index >= i.
            var currentIndex = -1;
            for (var j = i; j < target.Count; j++)
            {
                if (Equals(keySelector(target[j]), key))
                {
                    currentIndex = j;
                    break;
                }
            }

            if (currentIndex < 0)
            {
                target.Insert(i, item);
            }
            else
            {
                if (currentIndex != i)
                {
                    target.Move(currentIndex, i);
                }

                target[i] = item;
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
