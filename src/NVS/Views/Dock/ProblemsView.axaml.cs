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
                // Only follow new items when the user is already at the bottom;
                // don't yank the view away from what they're reading.
                if (list?.ItemCount > 0 && list.Scroll is { } scroll &&
                    scroll.Offset.Y >= scroll.Extent.Height - scroll.Viewport.Height - 1)
                {
                    list.ScrollIntoView(list.ItemCount - 1);
                }
            });
        }
    }

    private void OnProblemDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (DataContext is ProblemsToolViewModel vm &&
            sender is ListBox { SelectedItem: ProblemItem item })
        {
            vm.NavigateToProblemCommand.Execute(item);
        }
    }
}
