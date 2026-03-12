using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels.Dock;

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
    private string _sidebarMode = "Explorer";
    private bool _isTerminalVisible;
    private string _terminalOutput = "";
    private string _terminalInput = "";
    private string _commitMessage = "";
    private string _searchQuery = "";
    private bool _isSearching;
    private CancellationTokenSource? _searchCts;
    private IRootDock? _dockLayout;

    public event PropertyChangedEventHandler? PropertyChanged;

    public IStorageProvider? StorageProvider { get; set; }

    public IRootDock? DockLayout
    {
        get => _dockLayout;
        set => SetProperty(ref _dockLayout, value);
    }

    public void InitializeDock()
    {
        var dockSettings = SettingsService.AppSettings.Dock;
        var factory = new NvsDockFactory(this, dockSettings);
        var layout = factory.CreateLayout();
        factory.InitLayout(layout);
        DockLayout = layout as IRootDock;
    }

    public MainViewModel(
        IWorkspaceService workspaceService,
        IEditorService editorService,
        IFileSystemService fileSystemService,
        EditorViewModel editor,
        IGitService gitService,
        ITerminalService terminalService,
        ISettingsService settingsService)
    {
        _workspaceService = workspaceService;
        _editorService = editorService;
        _fileSystemService = fileSystemService;
        _gitService = gitService;
        _terminalService = terminalService;
        SettingsService = settingsService;
        Editor = editor;

        _gitService.StatusChanged += OnGitStatusChanged;
    }

    public ISettingsService SettingsService { get; }

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

    // Sidebar mode: "Explorer", "Git", "Search"
    public string SidebarMode
    {
        get => _sidebarMode;
        set
        {
            if (SetProperty(ref _sidebarMode, value))
            {
                OnPropertyChanged(nameof(IsSidebarShowingExplorer));
                OnPropertyChanged(nameof(IsSidebarShowingGit));
                OnPropertyChanged(nameof(IsSidebarShowingSearch));
            }
        }
    }

    public bool IsSidebarShowingGit
    {
        get => _sidebarMode == "Git";
        set { if (value) SidebarMode = "Git"; }
    }

    public bool IsSidebarShowingExplorer => _sidebarMode == "Explorer";
    public bool IsSidebarShowingSearch => _sidebarMode == "Search";

    // Search properties
    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public bool IsSearching
    {
        get => _isSearching;
        set => SetProperty(ref _isSearching, value);
    }

    public ObservableCollection<FileSearchResult> SearchResults { get; } = [];

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
            Name = new DirectoryInfo(folderPath).Name,
            Path = folderPath,
            IsDirectory = true
        };

        await LoadDirectoryContents(rootNode, folderPath);
        rootNode.IsExpanded = true;
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
        SidebarMode = SidebarMode == "Git" ? "Explorer" : "Git";
    }

    [RelayCommand]
    private void ShowExplorer()
    {
        SidebarMode = "Explorer";
    }

    [RelayCommand]
    private void ShowSourceControl()
    {
        SidebarMode = "Git";
    }

    [RelayCommand]
    private void ShowSearch()
    {
        SidebarMode = "Search";
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
        if (terminal is null)
        {
            CreateNewTerminal();
            terminal = _terminalService.ActiveTerminal;
        }
        if (terminal is null) return;

        terminal.WriteLine(TerminalInput);
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
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                TerminalOutput += e.Data;
            });
        };

        TerminalOutput = "";
        IsTerminalVisible = true;
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

    [RelayCommand]
    private async Task SearchFiles()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || _workspacePath is null) return;

        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        IsSearching = true;
        SearchResults.Clear();
        var query = SearchQuery;

        try
        {
            IReadOnlyList<string> files;
            try
            {
                files = await _fileSystemService.GetFilesAsync(_workspacePath, "*", recursive: true, token);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Fallback: enumerate with error tolerance
                files = await Task.Run(() => EnumerateFilesSafe(_workspacePath), token);
            }

            foreach (var file in files)
            {
                if (token.IsCancellationRequested) break;
                if (IsInExcludedDirectory(file) || IsBinaryExtension(file)) continue;

                try
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.Exists && fileInfo.Length > 5 * 1024 * 1024) continue;
                    }
                    catch { /* If we can't check size, try reading anyway */ }

                    var content = await _fileSystemService.ReadAllTextAsync(file, token);
                    var lines = content.Split('\n');
                    for (var i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                        {
                            SearchResults.Add(new FileSearchResult
                            {
                                FilePath = file,
                                RelativePath = System.IO.Path.GetRelativePath(_workspacePath, file),
                                LineNumber = i + 1,
                                LineText = lines[i].Trim(),
                            });

                            if (SearchResults.Count >= 500) break;
                        }
                    }
                    if (SearchResults.Count >= 500) break;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Skip files that can't be read
                }
            }

            StatusMessage = $"Search: {SearchResults.Count} result(s) for \"{query}\"";
        }
        catch (OperationCanceledException)
        {
            // Search was cancelled
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search error: {ex.Message}";
        }
        finally
        {
            IsSearching = false;
        }
    }

    private static List<string> EnumerateFilesSafe(string rootPath)
    {
        var results = new List<string>();
        try
        {
            foreach (var file in Directory.EnumerateFiles(rootPath, "*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint,
            }))
            {
                results.Add(file);
            }
        }
        catch
        {
            // Return whatever we collected
        }
        return results;
    }

    [RelayCommand]
    private async Task OpenSearchResult(FileSearchResult? result)
    {
        if (result is null) return;
        await OpenFileAsync(result.FilePath);
        if (Editor?.ActiveDocument is { } doc)
        {
            doc.CursorLine = result.LineNumber;
        }
    }

    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", "__pycache__", ".vs", ".idea",
        "packages", "TestResults", ".nuget", "dist", "build", ".cache",
    };

    internal static bool IsInExcludedDirectory(string filePath)
    {
        var parts = filePath.Split(
            System.IO.Path.DirectorySeparatorChar,
            System.IO.Path.AltDirectorySeparatorChar);
        return parts.Any(p => ExcludedDirectories.Contains(p));
    }

    internal static bool IsBinaryExtension(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext is ".exe" or ".dll" or ".pdb" or ".obj" or ".o" or ".a"
            or ".so" or ".dylib" or ".lib" or ".bin" or ".class" or ".pyc"
            or ".pyo" or ".wasm" or ".node"
            or ".zip" or ".gz" or ".tar" or ".7z" or ".rar" or ".nupkg"
            or ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico"
            or ".svg" or ".webp" or ".tiff" or ".tif"
            or ".mp3" or ".mp4" or ".avi" or ".mov" or ".wav" or ".flac"
            or ".ogg" or ".webm" or ".mkv"
            or ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx"
            or ".snk" or ".pfx" or ".p12"
            or ".woff" or ".woff2" or ".ttf" or ".eot" or ".otf"
            or ".sqlite" or ".db" or ".mdb"
            or ".suo" or ".user";
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

public class FileTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded;

    public string Name { get; init; } = "";
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

public class FileSearchResult
{
    public string FilePath { get; init; } = "";
    public string RelativePath { get; init; } = "";
    public int LineNumber { get; init; }
    public string LineText { get; init; } = "";
    public string Display => $"{RelativePath}:{LineNumber}";
}
