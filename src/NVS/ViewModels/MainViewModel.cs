using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Controls;
using Dock.Model.Core;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels.Dock;

namespace NVS.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IWorkspaceService _workspaceService;
    private readonly IEditorService _editorService;
    private readonly IFileSystemService _fileSystemService;
    private readonly IGitService _gitService;
    private readonly ITerminalService _terminalService;
    private readonly ISolutionService _solutionService;
    private readonly IBuildService _buildService;
    private readonly IDebugService? _debugService;
    private readonly IBreakpointStore? _breakpointStore;
    private readonly ICodeMetricsService? _codeMetricsService;
    private readonly IRoslynCompletionService? _roslynCompletionService;
    private readonly IPrerequisiteService? _prerequisiteService;
    private readonly ILanguageService? _languageService;
    private readonly IChatSessionService? _chatSessionService;
    private readonly IInlineCompletionService? _inlineCompletionService;
    private readonly ILaunchSettingsService? _launchSettingsService;
    private readonly IBrowserLauncher? _browserLauncher;
    private readonly ITerminalHost? _terminalHost;

    private string _title = "NVS - No Vim Substitute";
    private bool _isWorkspaceOpen;
    private string? _workspacePath;
    private string _statusMessage = "Ready";
    private EditorViewModel? _editor;
    private string _sidebarMode = "Explorer";
    private IRootDock? _dockLayout;
    private NvsDockFactory? _dockFactory;

    public IStorageProvider? StorageProvider { get; set; }

    public IRootDock? DockLayout
    {
        get => _dockLayout;
        set => SetProperty(ref _dockLayout, value);
    }

    public void InitializeDock()
    {
        var dockSettings = SettingsService.AppSettings.Dock;
        _dockFactory = new NvsDockFactory(this, dockSettings);
        var layout = _dockFactory.CreateLayout();
        _dockFactory.InitLayout(layout);
        DockLayout = layout as IRootDock;
        ConflictResolver = _dockFactory.ConflictResolver;
    }

    public MainViewModel(
        IWorkspaceService workspaceService,
        IEditorService editorService,
        IFileSystemService fileSystemService,
        EditorViewModel editor,
        IGitService gitService,
        ITerminalService terminalService,
        ISettingsService settingsService,
        ISolutionService solutionService,
        IBuildService buildService,
        IDebugService? debugService = null,
        IBreakpointStore? breakpointStore = null,
        ICodeMetricsService? codeMetricsService = null,
        IRoslynCompletionService? roslynCompletionService = null,
        IPrerequisiteService? prerequisiteService = null,
        ILanguageService? languageService = null,
        IChatSessionService? chatSessionService = null,
        IInlineCompletionService? inlineCompletionService = null,
        ILaunchSettingsService? launchSettingsService = null,
        IBrowserLauncher? browserLauncher = null,
        ITerminalHost? terminalHost = null)
    {
        _workspaceService = workspaceService;
        _editorService = editorService;
        _fileSystemService = fileSystemService;
        _gitService = gitService;
        _terminalService = terminalService;
        _solutionService = solutionService;
        _buildService = buildService;
        _debugService = debugService;
        _breakpointStore = breakpointStore;
        _codeMetricsService = codeMetricsService;
        _roslynCompletionService = roslynCompletionService;
        _prerequisiteService = prerequisiteService;
        _languageService = languageService;
        _chatSessionService = chatSessionService;
        _inlineCompletionService = inlineCompletionService;
        _launchSettingsService = launchSettingsService;
        _browserLauncher = browserLauncher;
        _terminalHost = terminalHost;
        SettingsService = settingsService;
        Editor = editor;

        Git = new GitViewModel(gitService, this);
        BuildRun = new BuildRunViewModel(buildService, solutionService, this);
        BuildRun.LaunchSettingsService = launchSettingsService;
        BuildRun.BrowserLauncher = browserLauncher;
        Debug = new DebugViewModel(debugService, breakpointStore, solutionService, buildService, this);
        Debug.LaunchSettingsService = launchSettingsService;
        Debug.BrowserLauncher = browserLauncher;
        Search = new SearchViewModel(fileSystemService, this);
        Explorer = new ExplorerViewModel(fileSystemService, this);
        Terminal = new TerminalViewModel(terminalService, this);
        _editorService.DocumentOpened += OnEditorDocumentOpened;

    }

    public ISettingsService SettingsService { get; }
    public IEditorService EditorService => _editorService;
    public ISolutionService SolutionService => _solutionService;
    public IBuildService BuildService => _buildService;
    public IDebugService? DebugService => _debugService;
    public ITerminalService TerminalService => _terminalService;
    public ILaunchSettingsService? LaunchSettingsService => _launchSettingsService;
    public IBrowserLauncher? BrowserLauncher => _browserLauncher;
    public ITerminalHost? TerminalHost => _terminalHost;
    public IBreakpointStore? BreakpointStore => _breakpointStore;
    public ICodeMetricsService? CodeMetricsService => _codeMetricsService;
    public IChatSessionService? ChatSessionService => _chatSessionService;
    public IGitService GitServiceAccessor => _gitService;

    /// <summary>Source-control state and commands.</summary>
    public GitViewModel Git { get; }

    /// <summary>Build, run, and stop-execution state and commands.</summary>
    public BuildRunViewModel BuildRun { get; }

    /// <summary>Debug-session state, commands, and event handling.</summary>
    public DebugViewModel Debug { get; }

    /// <summary>Workspace-wide text search state and commands.</summary>
    public SearchViewModel Search { get; }

    /// <summary>Explorer file-tree state and commands.</summary>
    public ExplorerViewModel Explorer { get; }

    /// <summary>Terminal panel visibility and I/O.</summary>
    public TerminalViewModel Terminal { get; }

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

    public EditorViewModel? Editor
    {
        get => _editor;
        set => SetProperty(ref _editor, value);
    }

    public DiffViewerToolViewModel? DiffViewer { get; set; }
    public ConflictResolverToolViewModel? ConflictResolver { get; set; }

    public ObservableCollection<InfoBarViewModel> InfoBarItems { get; } = [];

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

    public ObservableCollection<string> ProjectNames { get; } = [];

    /// <summary>Launch profile names available for the current startup project (web apps).</summary>
    public ObservableCollection<string> LaunchProfiles { get; } = [];

    private string? _selectedLaunchProfile;
    /// <summary>
    /// The launch profile to use when running/debugging the startup web project.
    /// When null, the project's default profile is used (resolved by ILaunchSettingsService).
    /// </summary>
    public string? SelectedLaunchProfile
    {
        get => _selectedLaunchProfile;
        set => SetProperty(ref _selectedLaunchProfile, value);
    }

    private string? _selectedStartupProject;
    public string? SelectedStartupProject
    {
        get => _selectedStartupProject;
        set
        {
            if (SetProperty(ref _selectedStartupProject, value) && value is not null)
            {
                _solutionService.SetStartupProject(value);
                Explorer.RefreshStartupMarker(value);
                RefreshLaunchProfiles(_solutionService.GetStartupProject());
            }
        }
    }

    private void RefreshProjectList()
    {
        ProjectNames.Clear();
        foreach (var p in _solutionService.GetLoadedProjects())
            ProjectNames.Add(p.Name);

        var startup = _solutionService.GetStartupProject();
        _selectedStartupProject = startup?.Name;
        OnPropertyChanged(nameof(SelectedStartupProject));

        RefreshLaunchProfiles(startup);
    }

    /// <summary>
    /// Rebuilds <see cref="LaunchProfiles"/> for the given startup project and
    /// seeds <see cref="SelectedLaunchProfile"/> with the default profile.
    /// </summary>
    public void RefreshLaunchModels(ProjectModel? startup)
        => RefreshLaunchProfiles(startup);

    private void RefreshLaunchProfiles(ProjectModel? startup)
    {
        LaunchProfiles.Clear();
        SelectedLaunchProfile = null;

        if (startup is null || !startup.IsWebProject || _launchSettingsService is null)
            return;

        LaunchProfile? defaultProfile = null;
        foreach (var p in _launchSettingsService.GetLaunchProfiles(startup))
        {
            LaunchProfiles.Add(p.Name);
            defaultProfile ??= _launchSettingsService.GetDefaultLaunchProfile(startup);
        }

        if (LaunchProfiles.Count > 0)
        {
            var prefs = SettingsService.WorkspaceSettings?.LaunchProfilePreferences;
            if (prefs is not null
                && !string.IsNullOrEmpty(startup.Name)
                && prefs.TryGetValue(startup.Name, out var prefName)
                && LaunchProfiles.Contains(prefName))
            {
                SelectedLaunchProfile = prefName;
            }
            else
            {
                SelectedLaunchProfile = defaultProfile?.Name ?? LaunchProfiles[0];
            }
        }
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

    [RelayCommand]
    private async Task OpenSolution()
    {
        if (StorageProvider == null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Solution",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Solution Files") { Patterns = ["*.sln", "*.slnx"] },
                new FilePickerFileType("All Files") { Patterns = ["*"] }
            ]
        });

        if (files.Count > 0)
        {
            var solutionPath = files[0].Path.LocalPath;
            var solutionDir = Path.GetDirectoryName(solutionPath);
            if (solutionDir is null) return;

            // Open the solution's directory as workspace
            WorkspacePath = solutionDir;
            IsWorkspaceOpen = true;
            Title = $"NVS - {Path.GetFileNameWithoutExtension(solutionPath)}";

            await Explorer.LoadFileTreeAsync(solutionDir);
            await Git.InitializeAsync(solutionDir);

            // Load the specific solution file
            try
            {
                var solution = await _solutionService.LoadSolutionAsync(solutionPath);
                Explorer.LoadSolutionTree(solution);
                RefreshProjectList();
                StatusMessage = $"Solution loaded: {solution.Name} ({solution.Projects.Count} projects)";

                // Load Roslyn workspace for C# completions (fire-and-forget)
                _ = LoadRoslynWorkspaceAsync(solutionPath);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load solution: {ex.Message}";
            }
        }
    }

    public async Task OpenSolutionFromPathAsync(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(solutionPath);
        if (solutionDir is null) return;

        WorkspacePath = solutionDir;
        IsWorkspaceOpen = true;
        Title = $"NVS - {Path.GetFileNameWithoutExtension(solutionPath)}";

        await Explorer.LoadFileTreeAsync(solutionDir);
        await Git.InitializeAsync(solutionDir);

        try
        {
            var solution = await _solutionService.LoadSolutionAsync(solutionPath);
            Explorer.LoadSolutionTree(solution);
            RefreshProjectList();
            StatusMessage = $"Solution loaded: {solution.Name} ({solution.Projects.Count} projects)";

            // Load Roslyn workspace for C# completions (fire-and-forget)
            _ = LoadRoslynWorkspaceAsync(solutionPath);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load solution: {ex.Message}";
        }
    }

    private async Task LoadRoslynWorkspaceAsync(string solutionPath)
    {
        if (_roslynCompletionService is null) return;
        try
        {
            await _roslynCompletionService.LoadWorkspaceAsync(solutionPath);
            StatusMessage = _roslynCompletionService.IsWorkspaceLoaded
                ? "Roslyn workspace ready"
                : StatusMessage;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[Roslyn] Workspace load failed for {Path}", solutionPath);
        }
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        WorkspacePath = folderPath;
        IsWorkspaceOpen = true;
        StatusMessage = $"Opened: {folderPath}";
        Title = $"NVS - {Path.GetFileName(folderPath)}";

        await Explorer.LoadFileTreeAsync(folderPath);
        await Git.InitializeAsync(folderPath);
        await DetectAndLoadSolutionAsync(folderPath);
        _ = CheckPrerequisitesAsync(folderPath);
        await InitializeChatSessionsAsync(folderPath);
    }

    private async Task DetectAndLoadSolutionAsync(string folderPath)
    {
        try
        {
            var solutionFile = await _solutionService.DetectSolutionFileAsync(folderPath);
            if (solutionFile is not null)
            {
                var solution = await _solutionService.LoadSolutionAsync(solutionFile);
                Explorer.LoadSolutionTree(solution);
                RefreshProjectList();
                StatusMessage = $"Solution loaded: {solution.Name} ({solution.Projects.Count} projects)";

                _ = LoadRoslynWorkspaceAsync(solutionFile);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load solution: {ex.Message}";
        }
    }

    internal async Task CheckPrerequisitesAsync(string folderPath)
    {
        if (_prerequisiteService is null || _languageService is null)
            return;

        try
        {
            var detectedLanguages = DetectWorkspaceLanguages(folderPath);
            if (detectedLanguages.Count == 0)
                return;

            var missing = await _prerequisiteService.CheckPrerequisitesAsync(detectedLanguages);

            foreach (var prereq in missing)
            {
                var message = $"{prereq.DisplayName} is not installed. {prereq.InstallHint}";
                var infoBar = new InfoBarViewModel(message, InfoBarSeverity.Warning);
                infoBar.Dismissed += (_, _) => InfoBarItems.Remove(infoBar);
                InfoBarItems.Add(infoBar);
            }
        }
        catch (Exception)
        {
            // Don't let prerequisite checking crash the workspace open
        }
    }

    internal HashSet<NVS.Core.Enums.Language> DetectWorkspaceLanguages(string folderPath)
    {
        var languages = new HashSet<NVS.Core.Enums.Language>();
        if (_languageService is null)
            return languages;

        try
        {
            var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(folderPath, "*.*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                MaxRecursionDepth = 4,
            }))
            {
                var ext = Path.GetExtension(file);
                if (!string.IsNullOrEmpty(ext))
                    extensions.Add(ext);
            }

            foreach (var ext in extensions)
            {
                var dummyFile = $"file{ext}";
                var lang = _languageService.DetectLanguage(dummyFile);
                if (lang != NVS.Core.Enums.Language.Unknown)
                    languages.Add(lang);
            }
        }
        catch (Exception)
        {
            // Silently ignore filesystem errors
        }

        return languages;
    }

    public async Task AddProjectToSolutionAsync(string projectName, string template)
    {
        var solution = _solutionService.CurrentSolution;
        if (solution is null || string.IsNullOrWhiteSpace(projectName)) return;

        var solutionDir = Path.GetDirectoryName(solution.FilePath)!;
        var projectDir = Path.Combine(solutionDir, projectName);

        try
        {
            Directory.CreateDirectory(projectDir);

            // Create project with dotnet new
            var psi = new System.Diagnostics.ProcessStartInfo("dotnet", $"new {template} -n {projectName} -o \"{projectDir}\"")
            {
                WorkingDirectory = solutionDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync();
                StatusMessage = $"Failed to create project: {err.Trim()}";
                return;
            }

            // Add to solution
            var csprojPath = Path.Combine(projectDir, $"{projectName}.csproj");
            await _solutionService.AddProjectToSolutionAsync(solution.FilePath, csprojPath);

            // Reload solution
            var reloaded = await _solutionService.LoadSolutionAsync(solution.FilePath);
            Explorer.LoadSolutionTree(reloaded);
            Git.RefreshFiles();

            StatusMessage = $"Added project: {projectName} ({template})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Add project failed: {ex.Message}";
        }
    }

    private void WireInlineCompletion(DocumentViewModel docVm)
    {
        if (_inlineCompletionService is null) return;

        var service = _inlineCompletionService;
        var filePath = docVm.Document.FilePath ?? docVm.Document.Path ?? "";
        var language = docVm.Language.ToString();

        docVm.InlineCompletionFunc = (line, col, prefix, suffix, ct) =>
            service.GetInlineCompletionAsync(filePath, line, col, prefix, suffix, language, ct);
    }

    internal BuildOutputToolViewModel? FindBuildOutputTool()
    {
        return FindToolInDock<BuildOutputToolViewModel>();
    }

    internal ProblemsToolViewModel? FindProblemsTool()
    {
        return FindToolInDock<ProblemsToolViewModel>();
    }

    internal TerminalToolViewModel? FindTerminalTool()
    {
        return FindToolInDock<TerminalToolViewModel>();
    }

    internal T? FindToolInDock<T>() where T : class
    {
        if (DockLayout is null) return null;

        return FindDockableRecursive<T>(DockLayout);
    }

    private static T? FindDockableRecursive<T>(IDockable dockable) where T : class
    {
        if (dockable is T match) return match;

        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                var found = FindDockableRecursive<T>(child);
                if (found is not null) return found;
            }
        }

        return null;
    }

    private async Task InitializeChatSessionsAsync(string folderPath)
    {
        if (_chatSessionService is null) return;

        try
        {
            await _chatSessionService.OpenWorkspaceAsync(folderPath);

            // Notify the chat panel to load sessions
            if (_dockFactory?.LlmChat is LlmChatToolViewModel chatVm)
            {
                await chatVm.LoadSessionsAsync();
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Failed to initialize chat sessions for {Path}", folderPath);
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
        if (IsDatabaseExtension(filePath))
        {
            await OpenDatabaseFile(filePath);
            return;
        }

        await _editorService.OpenDocumentAsync(filePath);
        StatusMessage = $"Opened: {Path.GetFileName(filePath)}";
    }

    private static bool IsDatabaseExtension(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".db" or ".sqlite" or ".sqlite3";
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
    private void ShowDatabaseExplorer()
    {
        var dbTool = FindToolInDock<DatabaseExplorerToolViewModel>();
        if (dbTool is not null)
        {
            // Find the parent ToolDock so we can set it as the active dockable
            ActivateToolInDock(dbTool);
        }
    }

    [RelayCommand]
    private void ShowApiClient()
    {
        var apiTool = FindToolInDock<ApiClientToolViewModel>();
        if (apiTool is not null)
            ActivateToolInDock(apiTool);
    }

    [RelayCommand]
    private void ShowWelcome()
    {
        var welcome = FindToolInDock<WelcomeDocumentViewModel>();
        if (welcome is not null)
            ActivateToolInDock(welcome);
    }

    [RelayCommand]
    private void ShowHelp()
    {
        var help = FindToolInDock<HelpToolViewModel>();
        if (help is not null)
            ActivateToolInDock(help);
    }

    [RelayCommand]
    private void ShowCodeMetrics()
    {
        var metrics = FindToolInDock<CodeMetricsToolViewModel>();
        if (metrics is not null)
            ActivateToolInDock(metrics);
    }

    [RelayCommand]
    private void SplitEditorRight() => Editor?.SplitRightCommand.Execute(null);

    [RelayCommand]
    private void SplitEditorDown() => Editor?.SplitDownCommand.Execute(null);

    [RelayCommand]
    private void CloseEditorSplit() => Editor?.CloseSplitCommand.Execute(null);

    /// <summary>
    /// Opens a database file in the Database Explorer panel.
    /// </summary>
    public async Task OpenDatabaseFile(string filePath)
    {
        var dbTool = FindToolInDock<DatabaseExplorerToolViewModel>();
        if (dbTool is not null)
        {
            await dbTool.OpenDatabase(filePath);
            ActivateToolInDock(dbTool);
            StatusMessage = $"Opened database: {Path.GetFileName(filePath)}";
        }
    }

    /// <summary>
    /// Executes SQL text in the Database Explorer panel.
    /// </summary>
    public async Task ExecuteSqlInDatabaseExplorer(string sql)
    {
        var dbTool = FindToolInDock<DatabaseExplorerToolViewModel>();
        if (dbTool is null) return;

        if (!dbTool.IsConnected)
        {
            StatusMessage = "No database connected — open a database first";
            return;
        }

        await dbTool.ExecuteSql(sql);
        ActivateToolInDock(dbTool);
        StatusMessage = "SQL executed in Database Explorer";
    }

    private void ActivateToolInDock(IDockable tool)
    {
        if (DockLayout is null) return;
        SetActiveDockableRecursive(DockLayout, tool);
    }

    private static bool SetActiveDockableRecursive(IDockable dockable, IDockable target)
    {
        if (dockable is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                if (child == target)
                {
                    dock.ActiveDockable = target;
                    return true;
                }
                if (SetActiveDockableRecursive(child, target))
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Opens (or activates) the diff viewer document in the dock.</summary>
    internal DiffViewerToolViewModel? OpenDiffDocument() => _dockFactory?.OpenDiffDocument();

    internal async Task ReloadOpenDocumentsFromDiskAsync()
    {
        if (Editor?.OpenDocuments is null) return;
        var toClose = new List<DocumentViewModel>();
        foreach (var doc in Editor.OpenDocuments)
        {
            if (doc.Document.FilePath is not null)
            {
                if (File.Exists(doc.Document.FilePath))
                {
                    var content = await File.ReadAllTextAsync(doc.Document.FilePath);
                    doc.Text = content;
                    doc.IsDirty = false;
                }
                else
                {
                    toClose.Add(doc);
                }
            }
        }
        foreach (var doc in toClose)
            Editor.CloseDocument(doc);
    }

    private void OnEditorDocumentOpened(object? sender, Core.Models.Document e)
    {
        var editorDoc = FindToolInDock<EditorDocumentViewModel>();
        if (editorDoc is not null)
            ActivateToolInDock(editorDoc);

        // Wire inline completion on the newly opened document
        var docVm = Editor?.OpenDocuments.LastOrDefault();
        if (docVm is not null)
            WireInlineCompletion(docVm);
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

}
