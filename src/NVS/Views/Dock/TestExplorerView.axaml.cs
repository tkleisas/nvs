using Avalonia.Controls;
using Avalonia.Input;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class TestExplorerView : UserControl
{
    public TestExplorerView()
    {
        InitializeComponent();
    }

    private void OnTestDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is TestExplorerToolViewModel vm && vm.SelectedNode is not null)
        {
            vm.NavigateToTestCommand.Execute(vm.SelectedNode);
        }
    }
}
