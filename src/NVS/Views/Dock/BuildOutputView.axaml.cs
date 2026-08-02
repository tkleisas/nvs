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
                if (scrollViewer is null) return;

                // Only follow output when the user is already at the bottom;
                // don't yank the view away while they're reading earlier lines.
                var atBottom = scrollViewer.Offset.Y >=
                    scrollViewer.Extent.Height - scrollViewer.Viewport.Height - 1;
                if (atBottom)
                {
                    scrollViewer.ScrollToEnd();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }
    }
}
