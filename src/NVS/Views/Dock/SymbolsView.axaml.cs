using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class SymbolsView : UserControl
{
    public SymbolsView()
    {
        InitializeComponent();
    }

    private void OnSymbolDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is SymbolsToolViewModel vm && vm.SelectedSymbol is not null)
        {
            vm.NavigateToSymbolCommand.Execute(vm.SelectedSymbol);
        }
    }
}
