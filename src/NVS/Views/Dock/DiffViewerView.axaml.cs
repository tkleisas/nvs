using Avalonia.Controls;
using Avalonia.Interactivity;
using NVS.Services.Git;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class DiffViewerView : UserControl
{
    private bool _syncing;
    private int _currentChangeIndex = -1;

    public DiffViewerView()
    {
        InitializeComponent();
    }

    private void OnLeftScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        RightScroll.Offset = new Avalonia.Vector(RightScroll.Offset.X, LeftScroll.Offset.Y);
        _syncing = false;
    }

    private void OnRightScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_syncing) return;
        _syncing = true;
        LeftScroll.Offset = new Avalonia.Vector(LeftScroll.Offset.X, RightScroll.Offset.Y);
        _syncing = false;
    }

    private void OnNextChangeClick(object? sender, RoutedEventArgs e) => JumpToChange(+1);

    private void OnPrevChangeClick(object? sender, RoutedEventArgs e) => JumpToChange(-1);

    private void JumpToChange(int direction)
    {
        if (DataContext is not DiffViewerToolViewModel vm || vm.DiffLines.Count == 0) return;

        var count = vm.DiffLines.Count;
        var index = _currentChangeIndex;

        for (var step = 0; step < count; step++)
        {
            index = (index + direction + count) % count;
            var pair = vm.DiffLines[index];

            if (pair.Left.Type is DiffSideLineType.Deleted || pair.Right.Type is DiffSideLineType.Added)
            {
                _currentChangeIndex = index;
                LeftItems.ScrollIntoView(pair);
                RightItems.ScrollIntoView(pair);
                return;
            }
        }
    }
}
