using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

/// <summary>
/// Explorer file-tree state and commands, owned by <see cref="MainViewModel"/>
/// and exposed to views as <c>Main.Explorer</c>.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private readonly IFileSystemService _fileSystemService;
    private readonly MainViewModel _main;

    public ObservableCollection<FileTreeNode> FileTree { get; } = [];

    public ExplorerViewModel(IFileSystemService fileSystemService, MainViewModel main)
    {
        _fileSystemService = fileSystemService;
        _main = main;
    }

    /// <summary>Loads the plain directory tree for a workspace folder.</summary>
    public async Task LoadFileTreeAsync(string folderPath)
    {
        FileTree.Clear();

        var rootNode = new FileTreeNode
        {
            Name = new DirectoryInfo(folderPath).Name,
            Path = folderPath,
            IsDirectory = true
        };

        await LoadDirectoryContents(rootNode, folderPath);
        rootNode.IsExpanded = true;
        FileTree.Add(rootNode);
    }

    /// <summary>Reloads the file tree for the current workspace.</summary>
    public async Task ReloadAsync()
    {
        if (_main.WorkspacePath is not null)
            await LoadFileTreeAsync(_main.WorkspacePath);
    }

    /// <summary>Builds the solution-structured tree (projects as top-level nodes).</summary>
    public void LoadSolutionTree(SolutionModel solution)
    {
        FileTree.Clear();

        var solutionDir = Path.GetDirectoryName(solution.FilePath)!;

        var solutionNode = new FileTreeNode
        {
            Name = $"{solution.Name} ({Path.GetFileName(solution.FilePath)})",
            Path = solutionDir,
            IsDirectory = true
        };

        // Check if single project lives in the same directory as solution
        var isFlatLayout = solution.Projects.Count == 1
            && string.Equals(
                Path.GetDirectoryName(Path.GetFullPath(Path.Combine(solutionDir, solution.Projects[0].RelativePath))),
                solutionDir,
                StringComparison.OrdinalIgnoreCase);

        if (isFlatLayout)
        {
            // Show files directly under solution node
            LoadProjectFiles(solutionNode, solutionDir);
        }
        else
        {
            // Collect all project directories so root-level projects can exclude sibling project dirs
            var projectDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var projRef in solution.Projects)
            {
                var projPath = Path.GetFullPath(Path.Combine(solutionDir, projRef.RelativePath));
                var projDir = Path.GetDirectoryName(projPath);
                if (projDir is not null)
                    projectDirs.Add(projDir);
            }

            foreach (var projRef in solution.Projects)
            {
                var projectPath = Path.GetFullPath(Path.Combine(solutionDir, projRef.RelativePath));
                var projectDir = Path.GetDirectoryName(projectPath);
                var isStartup = solution.StartupProjectPath is not null
                    && Path.GetFullPath(solution.StartupProjectPath) == Path.GetFullPath(projectPath);

                var projectNode = new FileTreeNode
                {
                    Name = isStartup ? $"▶ {projRef.Name}" : projRef.Name,
                    Path = projectDir ?? projectPath,
                    IsDirectory = true
                };

                if (projectDir is not null && Directory.Exists(projectDir))
                {
                    // If project lives in solution root, exclude sibling project directories
                    var excludeDirs = string.Equals(projectDir, solutionDir, StringComparison.OrdinalIgnoreCase)
                        ? projectDirs.Where(d => !string.Equals(d, solutionDir, StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                        : null;
                    LoadProjectFiles(projectNode, projectDir, excludeDirs);
                }

                solutionNode.Children.Add(projectNode);
            }
        }

        solutionNode.IsExpanded = true;
        FileTree.Add(solutionNode);
    }

    /// <summary>Prefixes the startup project's node with a marker.</summary>
    public void RefreshStartupMarker(string? startupProjectName)
    {
        if (FileTree.Count == 0) return;
        var solutionNode = FileTree[0];
        foreach (var child in solutionNode.Children)
        {
            var baseName = child.Name.StartsWith("▶ ") ? child.Name[2..] : child.Name;
            child.Name = (startupProjectName is not null
                && string.Equals(baseName, startupProjectName, StringComparison.OrdinalIgnoreCase))
                ? $"▶ {baseName}"
                : baseName;
        }
    }

    [RelayCommand]
    private async Task OpenFileFromTree(FileTreeNode? node)
    {
        if (node != null && !node.IsDirectory)
        {
            await _main.OpenFileAsync(node.Path);
        }
    }

    [RelayCommand]
    private async Task RefreshFileTree()
    {
        await ReloadAsync();
    }

    private async Task LoadDirectoryContents(FileTreeNode parentNode, string directoryPath)
    {
        try
        {
            var directories = await _fileSystemService.GetDirectoriesAsync(directoryPath);
            var files = await _fileSystemService.GetFilesAsync(directoryPath, "*", false);

            foreach (var dir in directories.OrderBy(d => d))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.') || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
                    continue;

                var node = new FileTreeNode
                {
                    Name = dirName,
                    Path = dir,
                    IsDirectory = true
                };
                await LoadDirectoryContents(node, dir);
                parentNode.Children.Add(node);
            }

            foreach (var file in files.OrderBy(f => f))
            {
                parentNode.Children.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(file),
                    Path = file,
                    IsDirectory = false
                });
            }
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Error loading directory: {ex.Message}";
        }
    }

    private static void LoadProjectFiles(FileTreeNode projectNode, string projectDir, HashSet<string>? excludeFullPaths = null)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(projectDir).OrderBy(d => d))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.') || SearchViewModel.IsInExcludedDirectory(dirName))
                    continue;

                // Skip sibling project directories when loading a root-level project
                if (excludeFullPaths is not null && excludeFullPaths.Contains(dir))
                    continue;

                var dirNode = new FileTreeNode
                {
                    Name = dirName,
                    Path = dir,
                    IsDirectory = true
                };

                LoadProjectFiles(dirNode, dir);
                projectNode.Children.Add(dirNode);
            }

            foreach (var file in Directory.GetFiles(projectDir).OrderBy(f => f))
            {
                projectNode.Children.Add(new FileTreeNode
                {
                    Name = Path.GetFileName(file),
                    Path = file,
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }
}

public class FileTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;
    private string _name = "";

    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }
    }
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];
    public string Icon => IsDirectory ? "▸" : "●";
    public string IconColor => IsDirectory ? "#E8A848" : GetFileIconColor();
    public ICommand? OpenCommand { get; set; }

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private string GetFileIconColor()
    {
        var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "#57A64A",
            ".cpp" or ".c" or ".h" or ".hpp" => "#569CD6",
            ".js" or ".jsx" => "#DCDCAA",
            ".ts" or ".tsx" => "#3178C6",
            ".py" => "#3572A5",
            ".rs" => "#DEA584",
            ".go" => "#00ADD8",
            ".json" => "#CBB886",
            ".xml" or ".xaml" or ".axaml" => "#E06C75",
            ".html" or ".htm" => "#E44D26",
            ".css" or ".scss" or ".less" => "#C586C0",
            ".md" => "#519ABA",
            ".txt" or ".log" => "#9DA5B4",
            ".yaml" or ".yml" => "#CB171E",
            ".toml" => "#9C4121",
            ".sln" or ".slnx" or ".csproj" => "#854CC7",
            ".gitignore" or ".editorconfig" => "#6D8086",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" => "#A074C4",
            ".sh" or ".bash" or ".ps1" or ".bat" or ".cmd" => "#89E051",
            _ => "#9DA5B4"
        };
    }
}
