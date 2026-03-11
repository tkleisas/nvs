using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using NVS.ViewModels;
using NVS.Views;

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

    private async void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        var about = new AboutWindow();
        await about.ShowDialog(this);
    }
}
