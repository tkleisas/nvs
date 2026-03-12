using System.Collections.Specialized;
using Avalonia.Controls;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class BuildOutputView : UserControl
{
    public BuildOutputView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is BuildOutputToolViewModel vm)
        {
            vm.OutputLines.CollectionChanged += OnOutputLinesChanged;
        }
    }

    private void OnOutputLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                var scrollViewer = this.FindControl<ScrollViewer>("OutputScrollViewer");
                scrollViewer?.ScrollToEnd();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
