using System.Collections.Specialized;
using Avalonia.Controls;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class ProblemsView : UserControl
{
    public ProblemsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ProblemsToolViewModel vm)
        {
            vm.Problems.CollectionChanged += OnProblemsChanged;
        }
    }

    private void OnProblemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var list = this.FindControl<ListBox>("ProblemsList");
                if (list?.ItemCount > 0)
                    list.ScrollIntoView(list.ItemCount - 1);
            });
        }
    }
}
