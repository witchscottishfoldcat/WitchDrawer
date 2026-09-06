using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace WitchDrawer.App.Infrastructure;

public sealed class ResettableObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        // Snapshot first so ReplaceAll(this) cannot clear the source before it is read.
        var replacement = items.ToArray();
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
