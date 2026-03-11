using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

public partial class MainViewModel : INotifyPropertyChanged
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IEditorService _editorService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IGitService _gitService;
    private readonly ITerminalService _terminalService;

    private string _title = "NVS - No Vim Substitute";
    private bool _isWorkspaceOpen;
    private string? _workspacePath;
    private string _statusMessage = "Ready";
    private string _currentBranch = "";
    private EditorViewModel? _editor;
    private bool _isSidebarShowingGit;
    private bool _isTerminalVisible;
    private string _terminalOutput = "";
    private string _terminalInput = "";
    private string _commitMessage = "";

    public event PropertyChangedEventHandler? PropertyChanged;

    public IStorageProvider? StorageProvider { get; set; }

    public MainViewModel(
        IWorkspaceService workspaceService,
        IEditorService editorService,
        IFileSystemService fileSystemService,
        EditorViewModel editor,
        IGitService gitService,
        ITerminalService terminalService)
    {
        _workspaceService = workspaceService;
        _editorService = editorService;
        _fileSystemService = fileSystemService;
        _gitService = gitService;
        _terminalService = terminalService;
        Editor = editor;

        _gitService.StatusChanged += OnGitStatusChanged;
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public bool IsWorkspaceOpen
    {
        get => _isWorkspaceOpen;
        set => SetProperty(ref _isWorkspaceOpen, value);
    }

    public string? WorkspacePath
    {
        get => _workspacePath;
        set => SetProperty(ref _workspacePath, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CurrentBranch
    {
        get => _currentBranch;
        set => SetProperty(ref _currentBranch, value);
    }

    public EditorViewModel? Editor
    {
        get => _editor;
        set => SetProperty(ref _editor, value);
    }

    public ObservableCollection<FileTreeNode> FileTree { get; } = [];

    // Sidebar mode
    public bool IsSidebarShowingGit
    {
        get => _isSidebarShowingGit;
        set
        {
            if (SetProperty(ref _isSidebarShowingGit, value))
            {
                OnPropertyChanged(nameof(IsSidebarShowingExplorer));
            }
        }
    }

    public bool IsSidebarShowingExplorer => !_isSidebarShowingGit;

    // Git properties
    public ObservableCollection<GitFileStatus> GitChangedFiles { get; } = [];
    public ObservableCollection<GitFileStatus> GitStagedFiles { get; } = [];

    public string CommitMessage
    {
        get => _commitMessage;
        set => SetProperty(ref _commitMessage, value);
    }

    // Terminal properties
    public bool IsTerminalVisible
    {
        get => _isTerminalVisible;
        set => SetProperty(ref _isTerminalVisible, value);
    }

    public string TerminalOutput
    {
        get => _terminalOutput;
        set => SetProperty(ref _terminalOutput, value);
    }

    public string TerminalInput
    {
        get => _terminalInput;
        set => SetProperty(ref _terminalInput, value);
    }

    [RelayCommand]
    private async Task OpenFolder()
    {
        if (StorageProvider == null) return;
        
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            var folderPath = folders[0].Path.LocalPath;
            await OpenWorkspaceAsync(folderPath);
        }
    }

    private async Task OpenWorkspaceAsync(string folderPath)
    {
        WorkspacePath = folderPath;
        IsWorkspaceOpen = true;
        StatusMessage = $"Opened: {folderPath}";
        Title = $"NVS - {Path.GetFileName(folderPath)}";

        await LoadFileTree(folderPath);
        await InitializeGitAsync(folderPath);
    }

    private async Task InitializeGitAsync(string folderPath)
    {
        await _gitService.InitializeAsync(folderPath);
        CurrentBranch = _gitService.CurrentBranch ?? "";
        RefreshGitFiles();
    }

    private async Task LoadFileTree(string folderPath)
    {
        FileTree.Clear();

        var rootNode = new FileTreeNode
        {
            Name = Path.GetFileName(folderPath),
            Path = folderPath,
            IsDirectory = true
        };

        await LoadDirectoryContents(rootNode, folderPath);
        FileTree.Add(rootNode);
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
                if (dirName.StartsWith(".") || dirName == "node_modules" || dirName == "bin" || dirName == "obj")
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
            StatusMessage = $"Error loading directory: {ex.Message}";
        }
    }

    [RelayCommand]
    private Task NewFile()
    {
        if (Editor != null)
        {
            Editor.NewFile();
            StatusMessage = "New file created";
        }
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task OpenFile()
    {
        if (StorageProvider == null) return;
        
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open File",
            AllowMultiple = true,
            FileTypeFilter = GetFileTypes()
        });

        foreach (var file in files)
        {
            var filePath = file.Path.LocalPath;
            await OpenFileAsync(filePath);
        }
    }

    public async Task OpenFileAsync(string filePath)
    {
        await _editorService.OpenDocumentAsync(filePath);
        StatusMessage = $"Opened: {Path.GetFileName(filePath)}";
    }

    [RelayCommand]
    private async Task SaveFile()
    {
        if (Editor?.ActiveDocument == null) return;

        if (string.IsNullOrEmpty(Editor.ActiveDocument.Document.FilePath))
        {
            await SaveFileAs();
        }
        else
        {
            await Editor.SaveFile();
            StatusMessage = $"Saved: {Editor.ActiveDocument.Document.Name}";
        }
    }

    [RelayCommand]
    private async Task SaveFileAs()
    {
        if (StorageProvider == null || Editor?.ActiveDocument == null) return;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save File As",
            FileTypeChoices = GetFileTypes(),
            SuggestedFileName = Editor.ActiveDocument.Document.Name
        });

        if (file != null)
        {
            var filePath = file.Path.LocalPath;
            Editor.ActiveDocument.Document.FilePath = filePath;
            Editor.ActiveDocument.Document.Name = Path.GetFileName(filePath);
            await Editor.SaveFile();
            StatusMessage = $"Saved: {Editor.ActiveDocument.Document.Name}";
        }
    }

    [RelayCommand]
    private async Task SaveAll()
    {
        if (Editor != null)
        {
            await Editor.SaveAll();
            StatusMessage = "All files saved";
        }
    }

    [RelayCommand]
    private void CloseFile()
    {
        Editor?.CloseFile();
        StatusMessage = "File closed";
    }

    [RelayCommand]
    private void CloseAllFiles()
    {
        Editor?.CloseAllFiles();
        StatusMessage = "All files closed";
    }

    [RelayCommand]
    private void ToggleTerminal()
    {
        IsTerminalVisible = !IsTerminalVisible;
        if (IsTerminalVisible && _terminalService.ActiveTerminal is null)
        {
            CreateNewTerminal();
        }
    }

    [RelayCommand]
    private void ToggleSidebar()
    {
        IsSidebarShowingGit = !IsSidebarShowingGit;
    }

    [RelayCommand]
    private void ShowExplorer()
    {
        IsSidebarShowingGit = false;
    }

    [RelayCommand]
    private void ShowSourceControl()
    {
        IsSidebarShowingGit = true;
    }

    [RelayCommand]
    private async Task GitStageFile(GitFileStatus? file)
    {
        if (file is null) return;
        await _gitService.StageAsync(file.Path);
        StatusMessage = $"Staged: {file.Path}";
    }

    [RelayCommand]
    private async Task GitUnstageFile(GitFileStatus? file)
    {
        if (file is null) return;
        await _gitService.UnstageAsync(file.Path);
        StatusMessage = $"Unstaged: {file.Path}";
    }

    [RelayCommand]
    private async Task GitStageAll()
    {
        await _gitService.StageAllAsync();
        StatusMessage = "Staged all changes";
    }

    [RelayCommand]
    private async Task GitCommit()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage)) return;

        var result = await _gitService.CommitAsync(CommitMessage);
        if (result.Success)
        {
            StatusMessage = $"Committed: {result.CommitHash?[..7]}";
            CommitMessage = "";
            CurrentBranch = _gitService.CurrentBranch ?? "";
        }
        else
        {
            StatusMessage = $"Commit failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitRefresh()
    {
        if (_workspacePath is not null)
        {
            await _gitService.InitializeAsync(_workspacePath);
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
        }
    }

    [RelayCommand]
    private void SendTerminalInput()
    {
        if (string.IsNullOrEmpty(TerminalInput)) return;

        var terminal = _terminalService.ActiveTerminal;
        if (terminal is null) return;

        terminal.WriteLine(TerminalInput);
        TerminalOutput += $"> {TerminalInput}\n";
        TerminalInput = "";
    }

    [RelayCommand]
    private void NewTerminal()
    {
        CreateNewTerminal();
    }

    [RelayCommand]
    private void CloseTerminal()
    {
        var active = _terminalService.ActiveTerminal;
        if (active is not null)
        {
            _terminalService.CloseTerminal(active);
        }

        if (_terminalService.Terminals.Count == 0)
        {
            IsTerminalVisible = false;
            TerminalOutput = "";
        }
    }

    private void CreateNewTerminal()
    {
        var terminal = _terminalService.CreateTerminal(new TerminalOptions
        {
            Name = "Terminal",
            WorkingDirectory = _workspacePath,
        });

        terminal.DataReceived += (_, e) =>
        {
            TerminalOutput += e.Data + "\n";
        };

        TerminalOutput = "";
        StatusMessage = "Terminal opened";
    }

    private void OnGitStatusChanged(object? sender, Core.Interfaces.RepositoryStatus status)
    {
        RefreshGitFiles();
    }

    private void RefreshGitFiles()
    {
        GitChangedFiles.Clear();
        GitStagedFiles.Clear();

        foreach (var file in _gitService.Status.Files)
        {
            if (file.IsStaged)
                GitStagedFiles.Add(file);
            else
                GitChangedFiles.Add(file);
        }
    }

    [RelayCommand]
    private async Task OpenFileFromTree(FileTreeNode? node)
    {
        if (node != null && !node.IsDirectory)
        {
            await OpenFileAsync(node.Path);
        }
    }

    [RelayCommand]
    private async Task RefreshFileTree()
    {
        if (_workspacePath is not null)
        {
            await LoadFileTree(_workspacePath);
        }
    }

    private static List<FilePickerFileType> GetFileTypes()
    {
        return
        [
            new FilePickerFileType("All Files") { Patterns = ["*"] },
            new FilePickerFileType("C# Files") { Patterns = ["*.cs"] },
            new FilePickerFileType("C/C++ Files") { Patterns = ["*.c", "*.cpp", "*.h", "*.hpp"] },
            new FilePickerFileType("JavaScript/TypeScript") { Patterns = ["*.js", "*.ts", "*.jsx", "*.tsx"] },
            new FilePickerFileType("Python Files") { Patterns = ["*.py"] },
            new FilePickerFileType("Text Files") { Patterns = ["*.txt", "*.md"] },
            new FilePickerFileType("JSON Files") { Patterns = ["*.json"] },
            new FilePickerFileType("XML Files") { Patterns = ["*.xml", "*.xaml", "*.axaml"] }
        ];
    }

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public class FileTreeNode
{
    public string Name { get; init; } = "";
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public ObservableCollection<FileTreeNode> Children { get; } = [];
    public string Icon => IsDirectory ? "📁" : GetFileIcon();
    public ICommand? OpenCommand { get; set; }
    
    private string GetFileIcon()
    {
        var ext = System.IO.Path.GetExtension(Name).ToLowerInvariant();
        return ext switch
        {
            ".cs" => "🟢",
            ".cpp" or ".c" or ".h" or ".hpp" => "🔵",
            ".js" or ".jsx" => "🟡",
            ".ts" or ".tsx" => "🔷",
            ".py" => "🐍",
            ".rs" => "🦀",
            ".go" => "🔹",
            ".json" => "{ }",
            ".xml" or ".xaml" or ".axaml" => "📐",
            ".html" or ".htm" => "🌐",
            ".css" or ".scss" or ".less" => "🎨",
            ".md" => "📝",
            ".txt" or ".log" => "📃",
            ".yaml" or ".yml" => "⚙️",
            ".toml" => "⚙️",
            ".sln" or ".slnx" or ".csproj" => "🔧",
            ".gitignore" or ".editorconfig" => "⚙️",
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".svg" or ".ico" => "🖼️",
            ".sh" or ".bash" or ".ps1" or ".bat" or ".cmd" => "⚡",
            _ => "📄"
        };
    }
}
