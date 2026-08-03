using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NVS.Helpers;

/// <summary>
/// ObservableCollection with a bulk add that raises a single Reset notification —
/// used where bursts of appends (build output) would otherwise flood the binding
/// layer with one notification per item.
/// </summary>
public sealed class BatchedObservableCollection<T> : ObservableCollection<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        var list = items as IList<T> ?? items.ToList();
        if (list.Count == 0)
        {
            return;
        }

        foreach (var item in list)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
