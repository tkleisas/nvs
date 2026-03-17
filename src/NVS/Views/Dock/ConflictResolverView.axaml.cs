using Avalonia.Controls;
using Avalonia.Interactivity;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class ConflictResolverView : UserControl
{
    public ConflictResolverView()
    {
        InitializeComponent();
    }

    private void OnAcceptCurrentClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConflictBlockViewModel vm })
            vm.AcceptCurrent();
    }

    private void OnAcceptIncomingClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConflictBlockViewModel vm })
            vm.AcceptIncoming();
    }

    private void OnAcceptBothClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ConflictBlockViewModel vm })
            vm.AcceptBoth();
    }
}
