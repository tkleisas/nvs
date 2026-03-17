using Avalonia.Controls;

namespace NVS.Views.Dock;

public partial class DiffViewerView : UserControl
{
    private bool _syncing;

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
}
