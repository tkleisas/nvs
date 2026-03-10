using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using NVS.ViewModels;

namespace NVS;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnFileTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is TreeView treeView)
        {
            var selectedItem = treeView.SelectedItem as FileTreeNode;
            if (selectedItem != null && !selectedItem.IsDirectory)
            {
                if (DataContext is MainViewModel viewModel)
                {
                    viewModel.OpenFileFromTreeCommand.Execute(selectedItem);
                }
            }
        }
    }
}
