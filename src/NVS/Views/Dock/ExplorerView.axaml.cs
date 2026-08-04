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
                await main.Explorer.OpenFileFromTreeCommand.ExecuteAsync(node);
            }
        }
    }

    private async void OnFileTreeKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F2)
        {
            OnRenameInExplorerClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            OnDeleteInExplorerClick(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (FileTreeView.SelectedItem is FileTreeNode node && !node.IsDirectory && GetMain() is { } main)
            {
                await main.Explorer.OpenFileFromTreeCommand.ExecuteAsync(node);
            }
            e.Handled = true;
        }
    }

    private static void ReportIoError(MainViewModel? main, string operation, Exception ex)
    {
        if (main is not null)
        {
            main.StatusMessage = $"{operation} failed: {ex.Message}";
        }
        Serilog.Log.Warning(ex, "Explorer IO operation failed: {Operation}", operation);
    }

    private FileTreeNode? GetSelectedTreeNode() => FileTreeView.SelectedItem as FileTreeNode;

    private string GetContextDirectory()
    {
        var node = GetSelectedTreeNode();
        if (node is null)
        {
            return GetMain()?.WorkspacePath ?? "";
        }
        var dir = node.IsDirectory ? node.Path : System.IO.Path.GetDirectoryName(node.Path) ?? "";
        // If the path is not actually a directory on disk, use its parent
        if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
            dir = System.IO.Path.GetDirectoryName(dir) ?? "";
        return dir;
    }

    private async void OnNewFileInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var dir = GetContextDirectory();
        if (string.IsNullOrEmpty(dir)) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var name = await DialogHelper.PromptForNameAsync(window, "New File", "File name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            var filePath = System.IO.Path.Combine(dir, name);
            await System.IO.File.WriteAllTextAsync(filePath, "");
            await RefreshExplorer();
            if (GetMain() is { } main) main.StatusMessage = $"Created: {name}";
        }
        catch (Exception ex)
        {
            ReportIoError(GetMain(), "Create file", ex);
        }
    }

    private async void OnNewFolderInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var dir = GetContextDirectory();
        if (string.IsNullOrEmpty(dir)) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var name = await DialogHelper.PromptForNameAsync(window, "New Folder", "Folder name:");
        if (string.IsNullOrWhiteSpace(name)) return;

        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(dir, name));
            await RefreshExplorer();
            if (GetMain() is { } main) main.StatusMessage = $"Created folder: {name}";
        }
        catch (Exception ex)
        {
            ReportIoError(GetMain(), "Create folder", ex);
        }
    }

    private async void OnRenameInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        if (node is null) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var newName = await DialogHelper.PromptForNameAsync(window, "Rename", "New name:", node.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == node.Name) return;

        try
        {
            var parentDir = System.IO.Path.GetDirectoryName(node.Path) ?? "";
            var newPath = System.IO.Path.Combine(parentDir, newName);

            if (node.IsDirectory)
                System.IO.Directory.Move(node.Path, newPath);
            else
                System.IO.File.Move(node.Path, newPath);

            await RefreshExplorer();
            if (GetMain() is { } main) main.StatusMessage = $"Renamed to: {newName}";
        }
        catch (Exception ex)
        {
            ReportIoError(GetMain(), "Rename", ex);
        }
    }

    private async void OnDeleteInExplorerClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        if (node is null) return;

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var confirmed = await DialogHelper.ConfirmDeleteAsync(window, node.Name);
        if (!confirmed) return;

        try
        {
            if (node.IsDirectory)
                System.IO.Directory.Delete(node.Path, recursive: true);
            else
                System.IO.File.Delete(node.Path);

            await RefreshExplorer();
            if (GetMain() is { } main) main.StatusMessage = $"Deleted: {node.Name}";
        }
        catch (Exception ex)
        {
            ReportIoError(GetMain(), "Delete", ex);
        }
    }

    private async void OnRefreshExplorerClick(object? sender, RoutedEventArgs e)
    {
        await RefreshExplorer();
    }

    private async void OnAddProjectClick(object? sender, RoutedEventArgs e)
    {
        var main = GetMain();
        if (main?.SolutionService.CurrentSolution is null)
        {
            if (main is not null)
                main.StatusMessage = "No solution loaded";
            return;
        }

        var window = TopLevel.GetTopLevel(this) as Window;
        if (window is null) return;

        var result = await DialogHelper.PromptNewProjectAsync(window);
        if (result is null || string.IsNullOrWhiteSpace(result.Value.Name)) return;

        await main.AddProjectToSolutionAsync(result.Value.Name, result.Value.Template ?? "console");
    }

    private void OnSetStartupProjectClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        if (node is null || !node.IsDirectory) return;

        var main = GetMain();
        if (main is null) return;

        // Use the node name (strip ▶ prefix if present)
        var projectName = node.Name.StartsWith("▶ ") ? node.Name[2..] : node.Name;
        main.SelectedStartupProject = projectName;
        main.StatusMessage = $"Startup project: {projectName}";
    }

    private NVS.Core.Models.ProjectModel? GetProjectForNode(FileTreeNode? node)
    {
        if (node is null || !node.IsDirectory) return null;

        return GetMain()?.SolutionService.GetLoadedProjects().FirstOrDefault(p =>
            p.IsExecutable
            && string.Equals(
                System.IO.Path.GetDirectoryName(p.FilePath),
                node.Path,
                StringComparison.OrdinalIgnoreCase));
    }

    private void OnExplorerContextMenuOpened(object? sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;

        var hasProject = GetProjectForNode(GetSelectedTreeNode()) is not null;
        var hasSolution = GetMain()?.SolutionService.CurrentSolution is not null;
        var isFileInProject = GetMain() is { } main
                              && GetSelectedTreeNode() is { IsDirectory: false } node
                              && GetContainingProject(node.Path, main) is not null;

        foreach (var item in menu.Items)
        {
            switch (item)
            {
                case MenuItem { Tag: "ContainerProject" } mi:
                    mi.IsVisible = hasProject;
                    break;
                case MenuItem { Tag: "ContainerSolution" } mi:
                    mi.IsVisible = hasSolution;
                    break;
                case MenuItem { Tag: "CopyToOutput" } mi:
                    mi.IsVisible = isFileInProject;
                    break;
            }
        }
    }

    private async void OnCopyToOutputClick(object? sender, RoutedEventArgs e)
    {
        var node = GetSelectedTreeNode();
        var main = GetMain();
        if (node is null || node.IsDirectory || main is null || sender is not MenuItem { Tag: string mode }) return;

        var project = GetContainingProject(node.Path, main);
        if (project is null)
        {
            main.StatusMessage = "File is not inside a loaded project";
            return;
        }

        var copyMode = mode switch
        {
            "Always" => NVS.Core.Models.CopyToOutputMode.Always,
            "PreserveNewest" => NVS.Core.Models.CopyToOutputMode.PreserveNewest,
            _ => NVS.Core.Models.CopyToOutputMode.Never,
        };

        try
        {
            var projectDir = System.IO.Path.GetDirectoryName(project.FilePath)!;
            var relative = System.IO.Path.GetRelativePath(projectDir, node.Path);
            await main.SolutionService.SetCopyToOutputDirectoryAsync(project.FilePath, relative, copyMode);
            main.StatusMessage = copyMode == NVS.Core.Models.CopyToOutputMode.Never
                ? $"{node.Name}: will not be copied to output"
                : $"{node.Name}: copy to output = {mode}";
        }
        catch (Exception ex)
        {
            main.StatusMessage = $"Failed to update {System.IO.Path.GetFileName(project.FilePath)}: {ex.Message}";
        }
    }

    private static NVS.Core.Models.ProjectModel? GetContainingProject(string filePath, MainViewModel main) =>
        main.SolutionService.GetLoadedProjects()
            .Select(p => (Project: p, Dir: System.IO.Path.GetDirectoryName(p.FilePath)))
            .Where(x => x.Dir is not null
                        && filePath.StartsWith(x.Dir + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => x.Dir!.Length)
            .Select(x => x.Project)
            .FirstOrDefault();

    private void OnGenerateDockerfileForNodeClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProjectForNode(GetSelectedTreeNode());
        var main = GetMain();
        if (project is null || main is null) return;

        try
        {
            var projectDir = System.IO.Path.GetDirectoryName(project.FilePath)!;
            var path = System.IO.Path.Combine(projectDir, "Dockerfile");
            if (System.IO.File.Exists(path))
            {
                main.StatusMessage = $"Dockerfile already exists: {path} — not overwriting";
                return;
            }

            var content = NVS.Services.Containers.DockerfileScaffolder.GenerateDotNetDockerfile(project);
            System.IO.File.WriteAllText(path, content);
            main.StatusMessage = $"Dockerfile created: {path}";
        }
        catch (Exception ex)
        {
            main.StatusMessage = $"Failed to write Dockerfile: {ex.Message}";
        }
    }

    private async void OnBuildContainerImageForNodeClick(object? sender, RoutedEventArgs e)
    {
        var project = GetProjectForNode(GetSelectedTreeNode());
        var main = GetMain();
        if (project is null || main is null) return;

        await main.BuildContainerImageForProjectAsync(project);
    }

    private void OnGenerateComposeClick(object? sender, RoutedEventArgs e)
    {
        GetMain()?.GenerateComposeFileCommand.Execute(null);
    }

    private async Task RefreshExplorer()
    {
        if (GetMain() is { } main)
        {
            await main.Explorer.RefreshFileTreeCommand.ExecuteAsync(null);
        }
    }
}
