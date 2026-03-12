using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Views.Dock;

public partial class ExplorerView : UserControl
{
    public ExplorerView()
    {
        InitializeComponent();
    }

    private MainViewModel? GetMain() => (DataContext as ExplorerToolViewModel)?.Main;

    private async void OnFileTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (FileTreeView.SelectedItem is FileTreeNode node && !node.IsDirectory)
        {
            var main = GetMain();
            if (main is not null)
            {
                await main.OpenFileFromTreeCommand.ExecuteAsync(node);
            }
        }
    }

    private FileTreeNode? GetSelectedTreeNode() => FileTreeView.SelectedItem as FileTreeNode;

    private string GetContextDirectory()
    {
        var node = GetSelectedTreeNode();
        if (node is null)
        {
            return GetMain()?.WorkspacePath ?? "";
        }
        return node.IsDirectory ? node.Path : System.IO.Path.GetDirectoryName(node.Path) ?? "";
    }

    private async void OnNewFileInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var dir = GetContextDirectory();
        if (string.IsNullOrEmpty(dir)) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var name = await DialogHelper.PromptForNameAsync(window, "New File", "File name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        var filePath = System.IO.Path.Combine(dir, name);
        await System.IO.File.WriteAllTextAsync(filePath, "");
        await RefreshExplorer();
        if (GetMain() is { } main) main.StatusMessage = $"Created: {name}";
    }

    private async void OnNewFolderInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var dir = GetContextDirectory();
        if (string.IsNullOrEmpty(dir)) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var name = await DialogHelper.PromptForNameAsync(window, "New Folder", "Folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, name));
        await RefreshExplorer();
        if (GetMain() is { } main) main.StatusMessage = $"Created folder: {name}";
    }

    private async void OnRenameInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        if (node is null) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var newName = await DialogHelper.PromptForNameAsync(window, "Rename", "New name:", node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

        var parentDir = System.IO.Path.GetDirectoryName(node.Path) ?? "";
        var newPath = System.IO.Path.Combine(parentDir, newName);

        if (node.IsDirectory)
            System.IO.Directory.Move(node.Path, newPath);
        else
            System.IO.File.Move(node.Path, newPath);

        await RefreshExplorer();
        if (GetMain() is { } main) main.StatusMessage = $"Renamed to: {newName}";
    }

    private async void OnDeleteInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        if (node is null) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var confirmed = await DialogHelper.ConfirmDeleteAsync(window, node.Name);
        if (!confirmed) return;

        if (node.IsDirectory)
            System.IO.Directory.Delete(node.Path, recursive: true);
        else
            System.IO.File.Delete(node.Path);

        await RefreshExplorer();
        if (GetMain() is { } main) main.StatusMessage = $"Deleted: {node.Name}";
    }

    private async void OnRefreshExplorerClick(object? sender, RoutedEventArgs e)
    {
        await RefreshExplorer();
    }

    private async Task RefreshExplorer()
    {
        if (GetMain() is { } main)
        {
            await main.RefreshFileTreeCommand.ExecuteAsync(null);
        }
    }
}
