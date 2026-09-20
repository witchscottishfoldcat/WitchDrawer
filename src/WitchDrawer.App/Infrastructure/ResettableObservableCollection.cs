using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WitchDrawer.App.Infrastructure;

public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    // Preserve containers/selection for small drops. Large replacements still use
    // one Reset instead of flooding the dispatcher with collection notifications.
    public void Synchronize(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var replacement = items.ToArray();
        var comparer = EqualityComparer<T>.Default;
        var prefix = 0;
        while (prefix < Count && prefix < replacement.Length
            && comparer.Equals(this[prefix], replacement[prefix]))
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < Count - prefix && suffix < replacement.Length - prefix
            && comparer.Equals(this[Count - suffix - 1], replacement[replacement.Length - suffix - 1]))
        {
            suffix++;
        }

        var removed = Count - prefix - suffix;
        var added = replacement.Length - prefix - suffix;
        if (removed + added > 32)
        {
            ReplaceAll(replacement);
            return;
        }

        for (var index = 0; index < removed; index++)
        {
            RemoveAt(prefix);
        }

        for (var index = 0; index < added; index++)
        {
            Insert(prefix + index, replacement[prefix + index]);
        }
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Snapshot first so ReplaceAll(this) cannot clear the source before it is read.
        var replacement = items.ToArray();
        if (this.SequenceEqual(replacement))
        {
            return;
        }

        Items.Clear();
        foreach (var item in replacement)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(
            NotifyCollectionChangedAction.Reset));
    }
}
