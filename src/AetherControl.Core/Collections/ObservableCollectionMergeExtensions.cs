using System.Collections.ObjectModel;

namespace AetherControl.Core.Collections;

/// <summary>
/// Updates an <see cref="ObservableCollection{T}"/>'s contents to match a fresh snapshot in place —
/// replacing changed items via the indexer, and only inserting/removing when the key set itself
/// changes — instead of ever reassigning the collection's own reference.
/// <para>
/// <b>Correction, evidenced by a live trace:</b> an earlier version of this doc comment claimed that
/// replacing an item via the indexer (<c>CollectionChanged</c> with <c>Replace</c>) let a WinUI3
/// <c>ItemsControl</c> recycle the existing container and just rebind it — that was an unverified
/// assumption, and it was wrong. Instrumenting the bound control directly (a new instance id logged
/// per container, correlated to a drive's device id) proved that <c>ItemsControl</c> backed by a
/// <c>VariableSizedWrapGrid</c> <c>ItemsPanel</c> (no virtualization support) tears down and recreates
/// the container on every <c>Replace</c>, exactly like it does on a full <c>Reset</c>. Since every
/// poll produced a brand-new model instance even when every field was identical, every <c>Replace</c>
/// fired every single poll — a live capture showed a fresh container (and its zero-starting value
/// animation) appearing roughly once a second, forever, each one sweeping from 0 up to the real
/// value — which for a value like ~680 visibly passes through ~340 on the way. That is the confirmed,
/// trace-evidenced root cause of a reported "flickering between ~680GB and ~340GB" on a storage card:
/// not a wrong value, a value climbing from zero to itself once a second.
/// </para>
/// <para>
/// The actual fix has two parts. First, this method now skips the indexer assignment entirely when
/// the incoming item is value-equal to what's already at that position — no assignment, no
/// <c>CollectionChanged</c> event, no container touched at all, which is what converting the affected
/// model types (e.g. <c>StorageDriveInfo</c>) to <c>record</c>s (value equality instead of reference
/// equality) makes possible: two separately-constructed readings with identical field values are now
/// actually equal. Second, on a poll where the value genuinely did change, the container still gets
/// recreated — a real, if imperfect, WinUI3/<c>ItemsControl</c> limitation this fix works around
/// rather than eliminates — but a real change animating in is correct behaviour; the bug was a
/// container rebuilding for a change that never happened.
/// </para>
/// </summary>
public static class ObservableCollectionMergeExtensions
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

                // The one-line fix for a real, live-captured bug: assigning here unconditionally
                // fired a Replace (and tore down/recreated the bound UI container) every single poll,
                // even when nothing about this item actually changed. EqualityComparer<T>.Default
                // uses value equality once the model type involved is a record.
                if (!EqualityComparer<T>.Default.Equals(target[i], item))
                {
                    target[i] = item;
                }
            }
        }

        while (target.Count > source.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }
}
