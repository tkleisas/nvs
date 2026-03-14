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
    private readonly ISolutionService _solutionService;
    private readonly IBuildService _buildService;
    private readonly IDebugService? _debugService;
    private readonly IBreakpointStore? _breakpointStore;

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
    private bool _isBuilding;
    private bool _isRunning;
    private CancellationTokenSource? _buildCts;
    private CancellationTokenSource? _pauseCts;
    private bool _isDebugging;
    private bool _isDebugPaused;
    private bool _debugUsesTerminal;
    private TerminalToolViewModel? _debugTerminal;
    private System.Diagnostics.Process? _debuggeeProcess;

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
        ISettingsService settingsService,
        ISolutionService solutionService,
        IBuildService buildService,
        IDebugService? debugService = null,
        IBreakpointStore? breakpointStore = null)
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
        SettingsService = settingsService;
        Editor = editor;

        _gitService.StatusChanged += OnGitStatusChanged;
        _editorService.DocumentOpened += OnEditorDocumentOpened;

        if (_debugService is not null)
        {
            _debugService.DebuggingStarted += OnDebuggingStarted;
            _debugService.DebuggingStopped += OnDebuggingStopped;
            _debugService.DebuggingPaused += OnDebuggingPaused;
            _debugService.DebuggingContinued += OnDebuggingContinued;
            _debugService.OutputReceived += OnDebugOutput;
        }
    }

    public ISettingsService SettingsService { get; }
    public IEditorService EditorService => _editorService;
    public ISolutionService SolutionService => _solutionService;
    public IBuildService BuildService => _buildService;
    public IDebugService? DebugService => _debugService;
    public IBreakpointStore? BreakpointStore => _breakpointStore;

    public bool IsDebugging
    {
        get => _isDebugging;
        set => SetProperty(ref _isDebugging, value);
    }

    public bool IsDebugPaused
    {
        get => _isDebugPaused;
        set => SetProperty(ref _isDebugPaused, value);
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

    public bool GitIsRepository => _gitService.IsRepository;
    public bool GitIsNotRepository => !_gitService.IsRepository;
    public bool HasGitStashes => GitStashes.Count > 0;
    public bool HasGitTags => GitTags.Count > 0;

    private Branch? _selectedGitBranch;
    private bool _isRefreshingBranches;
    public Branch? SelectedGitBranch
    {
        get => _selectedGitBranch;
        set
        {
            if (_isRefreshingBranches) return;
            if (value is not null && value.Name != (_selectedGitBranch?.Name))
            {
                SetProperty(ref _selectedGitBranch, value);
                _ = GitCheckoutBranch(value);
                return;
            }
            SetProperty(ref _selectedGitBranch, value);
        }
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
    public ObservableCollection<Branch> GitBranches { get; } = [];
    public ObservableCollection<StashEntry> GitStashes { get; } = [];
    public ObservableCollection<Tag> GitTags { get; } = [];
    public ObservableCollection<Commit> GitCommitLog { get; } = [];
    public ObservableCollection<Remote> GitRemotes { get; } = [];

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

    // Build properties
    public bool IsBuilding
    {
        get => _isBuilding;
        set
        {
            if (SetProperty(ref _isBuilding, value))
                OnPropertyChanged(nameof(CanBuild));
        }
    }

    public bool IsRunning
    {
        get => _isRunning;
        set
        {
            if (SetProperty(ref _isRunning, value))
            {
                OnPropertyChanged(nameof(CanRun));
                OnPropertyChanged(nameof(CanStop));
            }
        }
    }

    public bool CanBuild => !IsBuilding && IsWorkspaceOpen;
    public bool CanRun => !IsRunning && IsWorkspaceOpen;
    public bool CanStop => IsRunning || IsBuilding;

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

            await LoadFileTree(solutionDir);
            await InitializeGitAsync(solutionDir);

            // Load the specific solution file
            try
            {
                var solution = await _solutionService.LoadSolutionAsync(solutionPath);
                LoadSolutionTree(solution);
                StatusMessage = $"Solution loaded: {solution.Name} ({solution.Projects.Count} projects)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to load solution: {ex.Message}";
            }
        }
    }

    public async Task OpenWorkspaceAsync(string folderPath)
    {
        WorkspacePath = folderPath;
        IsWorkspaceOpen = true;
        StatusMessage = $"Opened: {folderPath}";
        Title = $"NVS - {Path.GetFileName(folderPath)}";

        await LoadFileTree(folderPath);
        await InitializeGitAsync(folderPath);
        await DetectAndLoadSolutionAsync(folderPath);
    }

    private async Task DetectAndLoadSolutionAsync(string folderPath)
    {
        try
        {
            var solutionFile = await _solutionService.DetectSolutionFileAsync(folderPath);
            if (solutionFile is not null)
            {
                var solution = await _solutionService.LoadSolutionAsync(solutionFile);
                LoadSolutionTree(solution);
                StatusMessage = $"Solution loaded: {solution.Name} ({solution.Projects.Count} projects)";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load solution: {ex.Message}";
        }
    }

    private void LoadSolutionTree(Core.Models.SolutionModel solution)
    {
        FileTree.Clear();

        var solutionDir = Path.GetDirectoryName(solution.FilePath)!;

        var solutionNode = new FileTreeNode
        {
            Name = $"{solution.Name} ({Path.GetFileName(solution.FilePath)})",
            Path = solutionDir,
            IsDirectory = true
        };

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
                LoadProjectFiles(projectNode, projectDir);
            }

            solutionNode.Children.Add(projectNode);
        }

        solutionNode.IsExpanded = true;
        FileTree.Add(solutionNode);
    }

    private static void LoadProjectFiles(FileTreeNode projectNode, string projectDir)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(projectDir).OrderBy(d => d))
            {
                var dirName = Path.GetFileName(dir);
                if (dirName.StartsWith('.') || IsInExcludedDirectory(dirName))
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

    [RelayCommand]
    private async Task BuildSolution()
    {
        await RunBuildCommandAsync("Build Solution", "build");
    }

    [RelayCommand]
    private async Task CleanSolution()
    {
        await RunBuildCommandAsync("Clean Solution", "clean");
    }

    [RelayCommand]
    private async Task RebuildSolution()
    {
        await RunBuildCommandAsync("Clean Solution", "clean");
        if (!IsBuilding)
            await RunBuildCommandAsync("Build Solution", "build");
    }

    private async Task RunBuildCommandAsync(string name, string dotnetVerb)
    {
        if (!CanBuild || WorkspacePath is null) return;

        IsBuilding = true;
        StatusMessage = dotnetVerb == "clean" ? "Cleaning..." : "Building...";

        var buildOutput = FindBuildOutputTool();
        var problemsTool = FindProblemsTool();
        if (dotnetVerb != "clean" || name.StartsWith("Clean"))
            buildOutput?.ClearOutput();

        _buildCts = new CancellationTokenSource();

        void OnOutput(object? sender, BuildOutputEventArgs e)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                buildOutput?.AppendOutput(e.Output, e.IsError));
        }

        _buildService.OutputReceived += OnOutput;

        try
        {
            var args = new List<string> { dotnetVerb };
            if (_solutionService.CurrentSolution is { } sol)
                args.Add(sol.FilePath);
            args.Add("--nologo");

            var task = new Core.Interfaces.BuildTask
            {
                Name = name,
                Command = "dotnet",
                Args = [.. args],
                WorkingDirectory = WorkspacePath
            };

            var result = await _buildService.RunTaskAsync(task, _buildCts.Token);

            if (dotnetVerb != "clean")
                problemsTool?.SetProblems(result.Errors, result.Warnings);

            StatusMessage = dotnetVerb switch
            {
                "clean" => result.Success ? "Clean succeeded" : "Clean failed",
                _ => result.Success
                    ? $"Build succeeded ({result.Duration.TotalSeconds:F1}s)"
                    : $"Build failed — {result.Errors.Count} error(s), {result.Warnings.Count} warning(s)"
            };
        }
        catch (Exception ex)
        {
            StatusMessage = $"{name} error: {ex.Message}";
        }
        finally
        {
            _buildService.OutputReceived -= OnOutput;
            IsBuilding = false;
            _buildCts?.Dispose();
            _buildCts = null;
        }
    }

    [RelayCommand]
    private async Task RunProject()
    {
        if (!CanRun || WorkspacePath is null) return;

        IsRunning = true;
        StatusMessage = "Building...";

        try
        {
            var startup = _solutionService.GetStartupProject();

            // Build first
            var buildOutput = FindBuildOutputTool();
            buildOutput?.ClearOutput();

            var buildArgs = new List<string> { "build" };
            if (_solutionService.CurrentSolution is { } sol)
                buildArgs.Add(sol.FilePath);
            buildArgs.Add("--nologo");

            var buildTask = new Core.Interfaces.BuildTask
            {
                Name = "Build",
                Command = "dotnet",
                Args = [.. buildArgs],
                WorkingDirectory = WorkspacePath,
            };

            void OnBuildOutput(object? sender, BuildOutputEventArgs e)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    buildOutput?.AppendOutput(e.Output, e.IsError));
            }

            _buildService.OutputReceived += OnBuildOutput;
            BuildResult buildResult;
            try
            {
                buildResult = await _buildService.RunTaskAsync(buildTask);
            }
            finally
            {
                _buildService.OutputReceived -= OnBuildOutput;
            }

            if (!buildResult.Success)
            {
                StatusMessage = $"Build failed — {buildResult.Errors.Count} error(s)";
                FindProblemsTool()?.SetProblems(buildResult.Errors, buildResult.Warnings);
                return;
            }

            // Run in the integrated terminal so the user can see console output
            StatusMessage = "Running...";

            var projectArg = startup is not null
                ? $" --project \"{startup.FilePath}\""
                : "";
            var runCommand = $"dotnet run{projectArg} --nologo";

            // Ensure terminal panel is visible
            IsTerminalVisible = true;

            var terminalTool = FindTerminalTool();
            if (terminalTool is not null)
            {
                // Queue the command — the terminal will deliver it
                // once the PTY is ready (may need a brief delay).
                await terminalTool.SendCommandToTerminalAsync(runCommand);
                StatusMessage = "Application started in terminal";
            }
            else
            {
                StatusMessage = "Terminal not available — open a terminal first";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task StopExecution()
    {
        // Kill the process tree immediately, then cancel the token
        await _buildService.CancelAsync();

        if (_buildCts is not null)
        {
            await _buildCts.CancelAsync();
            StatusMessage = "Build cancelled";
        }
        else
        {
            StatusMessage = "Stopped";
        }
    }

    // ── Debug Commands ─────────────────────────────────────────────

    [RelayCommand]
    private async Task StartDebugging()
    {
        if (_debugService is null)
        {
            StatusMessage = "Debug service not available";
            return;
        }

        if (_debugService.IsDebugging)
        {
            // If paused, continue; otherwise ignore
            if (_debugService.IsPaused)
                await _debugService.ContinueAsync();
            return;
        }

        var solution = _solutionService.CurrentSolution;
        if (solution is null)
        {
            StatusMessage = "No solution loaded — cannot debug";
            return;
        }

        var startup = _solutionService.GetStartupProject();
        if (startup is null)
        {
            StatusMessage = "No startup project set — cannot debug";
            return;
        }

        try
        {
            // Build first — debugger needs compiled output
            StatusMessage = "Building before debug...";
            var buildOutput = FindBuildOutputTool();
            buildOutput?.ClearOutput();

            var buildArgs = new List<string> { "build" };
            if (_solutionService.CurrentSolution is { } sol)
                buildArgs.Add(sol.FilePath);
            buildArgs.Add("--nologo");

            var buildTask = new Core.Interfaces.BuildTask
            {
                Name = "Build for Debug",
                Command = "dotnet",
                Args = [.. buildArgs],
                WorkingDirectory = WorkspacePath,
            };

            void OnBuildOutput(object? sender, Core.Interfaces.BuildOutputEventArgs e)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    buildOutput?.AppendOutput(e.Output, e.IsError));
            }

            _buildService.OutputReceived += OnBuildOutput;
            BuildResult buildResult;
            try
            {
                buildResult = await _buildService.RunTaskAsync(buildTask);
            }
            finally
            {
                _buildService.OutputReceived -= OnBuildOutput;
            }

            if (!buildResult.Success)
            {
                StatusMessage = $"Build failed — {buildResult.Errors.Count} error(s)";
                FindProblemsTool()?.SetProblems(buildResult.Errors, buildResult.Warnings);
                return;
            }

            StatusMessage = "Starting debugger...";
            var projectDir = System.IO.Path.GetDirectoryName(startup.FilePath) ?? ".";
            var assemblyName = startup.AssemblyName ?? startup.Name;
            var programPath = System.IO.Path.Combine(projectDir, "bin", "Debug", startup.TargetFramework, assemblyName + ".dll");

            // If exact path doesn't exist, search for the DLL under bin/Debug/
            if (!System.IO.File.Exists(programPath))
            {
                var binDir = System.IO.Path.Combine(projectDir, "bin", "Debug");
                if (System.IO.Directory.Exists(binDir))
                {
                    var found = System.IO.Directory.GetFiles(binDir, assemblyName + ".dll", System.IO.SearchOption.AllDirectories);
                    if (found.Length > 0)
                        programPath = found[0];
                }
            }

            // Console apps: launch inside a debug terminal with a startup hook
            // that pauses for debugger attach. GUI apps: launch via DAP directly.
            var isConsoleApp = string.Equals(startup.OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
                            || startup.OutputType is null;

            if (isConsoleApp)
            {
                _debugUsesTerminal = true;

                // Locate the startup hook DLL shipped with NVS
                var hookDll = System.IO.Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "tools", "DebugStartupHook.dll");
                if (!System.IO.File.Exists(hookDll))
                    throw new System.IO.FileNotFoundException("Debug startup hook not found.", hookDll);

                // Temp file where the hook will write the debuggee PID
                var pidFile = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(), $"nvs_debug_{Guid.NewGuid():N}.pid");

                CreateDebugTerminal(projectDir);

                // Build the terminal command to launch the debuggee with the hook
                string terminalCommand;
                if (OperatingSystem.IsWindows())
                {
                    terminalCommand = $"$env:DOTNET_STARTUP_HOOKS='{hookDll}'; " +
                                      $"$env:NVS_DEBUG_PID_FILE='{pidFile}'; " +
                                      $"dotnet exec \"{programPath}\"";
                }
                else
                {
                    terminalCommand = $"DOTNET_STARTUP_HOOKS='{hookDll}' " +
                                      $"NVS_DEBUG_PID_FILE='{pidFile}' " +
                                      $"dotnet exec \"{programPath}\"";
                }

                await _debugTerminal!.SendCommandToTerminalAsync(terminalCommand);

                // Wait for the startup hook to write the PID file
                int debuggeePid = await WaitForPidFileAsync(pidFile, TimeSpan.FromSeconds(15));

                var config = new NVS.Core.Models.Settings.DebugConfiguration
                {
                    Name = startup.Name,
                    Type = "coreclr",
                    Request = "attach",
                    ProcessId = debuggeePid,
                    Cwd = projectDir,
                };

                await _debugService.StartDebuggingAsync(config);
            }
            else
            {
                _debugUsesTerminal = false;

                var config = new NVS.Core.Models.Settings.DebugConfiguration
                {
                    Name = startup.Name,
                    Type = "coreclr",
                    Request = "launch",
                    Program = programPath,
                    Cwd = projectDir,
                };

                await _debugService.StartDebuggingAsync(config);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task StopDebugging()
    {
        if (_debugService is null || !_debugService.IsDebugging) return;

        try
        {
            await _debugService.StopDebuggingAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stop debug error: {ex.Message}";
        }

        // Ensure UI resets even if the DebuggingStopped event didn't fire
        IsDebugging = false;
        IsDebugPaused = false;
        _debugUsesTerminal = false;
        StatusMessage = "Debug session ended";
        ClearDebugCurrentLine();
        DestroyDebugTerminal();
        CleanupDebuggeeProcess();
    }

    [RelayCommand]
    private async Task DebugStepOver()
    {
        if (_debugService is null || !_debugService.IsPaused) return;
        try
        {
            await _debugService.StepOverAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug step-over error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DebugStepInto()
    {
        if (_debugService is null || !_debugService.IsPaused) return;
        try
        {
            await _debugService.StepIntoAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug step-into error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DebugStepOut()
    {
        if (_debugService is null || !_debugService.IsPaused) return;
        try
        {
            await _debugService.StepOutAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug step-out error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DebugContinue()
    {
        if (_debugService is null || !_debugService.IsPaused) return;
        try
        {
            await _debugService.ContinueAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug continue error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DebugPause()
    {
        if (_debugService is null || !_debugService.IsDebugging || _debugService.IsPaused) return;
        try
        {
            await _debugService.PauseAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Debug pause error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleBreakpoint()
    {
        if (_breakpointStore is null || Editor?.ActiveDocument is null) return;

        var doc = Editor.ActiveDocument;
        var filePath = doc.Document.FilePath;
        if (string.IsNullOrEmpty(filePath)) return;

        _breakpointStore.ToggleBreakpoint(filePath, doc.CursorLine);
        RefreshDocumentBreakpoints(doc);
    }

    private void RefreshDocumentBreakpoints(DocumentViewModel doc)
    {
        if (_breakpointStore is null || string.IsNullOrEmpty(doc.Document.FilePath)) return;

        var bps = _breakpointStore.GetBreakpoints(doc.Document.FilePath);
        doc.Breakpoints = bps.Select(b => (b.Line, b.IsVerified)).ToList();
    }

    // ── Debug Event Handlers ──────────────────────────────────────

    private void OnDebuggingStarted(object? sender, DebugSession session)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDebugging = true;
            IsDebugPaused = false;
            StatusMessage = $"Debugging: {session.Name}";
        });
    }

    private void OnDebuggingStopped(object? sender, DebugSession session)
    {
        _pauseCts?.Cancel();
        _pauseCts?.Dispose();
        _pauseCts = null;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDebugging = false;
            IsDebugPaused = false;
            _debugUsesTerminal = false;
            StatusMessage = "Debug session ended";
            ClearDebugCurrentLine();
            DestroyDebugTerminal();
            CleanupDebuggeeProcess();

            // Clear call stack and variables
            FindToolInDock<CallStackToolViewModel>()?.ClearFrames();
            FindToolInDock<VariablesToolViewModel>()?.ClearVariables();
        });
    }

    private async void OnDebuggingPaused(object? sender, EventArgs e)
    {
        // Cancel any previous pause processing (rapid stepping)
        _pauseCts?.Cancel();
        _pauseCts?.Dispose();
        var cts = _pauseCts = new CancellationTokenSource();

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsDebugPaused = true;
            StatusMessage = "Paused";
        });

        if (_debugService is null) return;

        try
        {
            var threads = await _debugService.GetThreadsAsync(cts.Token);
            if (cts.Token.IsCancellationRequested) return;

            if (threads.Count > 0)
            {
                var frames = await _debugService.GetStackTraceAsync(threads[0].Id, cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    FindToolInDock<CallStackToolViewModel>()?.UpdateFrames(frames));

                if (frames.Count > 0)
                {
                    var topFrame = frames[0];
                    var vars = await _debugService.GetVariablesAsync(topFrame.Id, cts.Token);
                    if (cts.Token.IsCancellationRequested) return;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var varsTool = FindToolInDock<VariablesToolViewModel>();
                        if (varsTool is not null)
                        {
                            varsTool.SetDebugService(_debugService);
                            varsTool.UpdateVariables(vars);
                        }
                    });

                    // Navigate to the stopped location in the editor
                    if (!string.IsNullOrEmpty(topFrame.Source) && topFrame.Line > 0)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await OpenFileAsync(topFrame.Source);
                            if (Editor?.ActiveDocument is { } doc)
                            {
                                doc.CursorLine = topFrame.Line;
                                doc.DebugCurrentLine = topFrame.Line;
                            }
                        });
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when a new pause event supersedes this one
        }
        catch
        {
            // Best effort UI update
        }
    }

    private void OnDebuggingContinued(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDebugPaused = false;
            StatusMessage = "Running...";
            ClearDebugCurrentLine();
        });
    }

    private void OnDebugOutput(object? sender, OutputEvent evt)
    {
        // When the debuggee runs in the integrated terminal, its stdout/stderr
        // goes directly to the terminal. Only route DAP console/telemetry output
        // to the Build Output panel to avoid duplicate text.
        if (_debugUsesTerminal && evt.Category is OutputCategory.Stdout or OutputCategory.Stderr)
            return;

        var isError = evt.Category is OutputCategory.Stderr;
        FindBuildOutputTool()?.AppendOutput(evt.Output, isError);
    }

    private void ClearDebugCurrentLine()
    {
        if (Editor?.OpenDocuments is not null)
        {
            foreach (var doc in Editor.OpenDocuments)
                doc.DebugCurrentLine = null;
        }
    }

    private BuildOutputToolViewModel? FindBuildOutputTool()
    {
        return FindToolInDock<BuildOutputToolViewModel>();
    }

    private ProblemsToolViewModel? FindProblemsTool()
    {
        return FindToolInDock<ProblemsToolViewModel>();
    }

    private TerminalToolViewModel? FindTerminalTool()
    {
        return FindToolInDock<TerminalToolViewModel>();
    }

    private void CreateDebugTerminal(string workingDirectory)
    {
        DestroyDebugTerminal();

        _debugTerminal = new TerminalToolViewModel(this)
        {
            Id = "DebugTerminal",
            Title = "🐛 Debug",
        };
        _debugTerminal.WorkingDirectory = workingDirectory;

        // Add the debug terminal to the same ToolDock as the regular terminal
        var existingTerminal = FindTerminalTool();
        if (existingTerminal is not null)
        {
            var parentDock = FindParentDock(DockLayout!, existingTerminal);
            if (parentDock is not null)
            {
                parentDock.VisibleDockables?.Add(_debugTerminal);
                parentDock.ActiveDockable = _debugTerminal;
                return;
            }
        }
    }

    private void DestroyDebugTerminal()
    {
        if (_debugTerminal is null) return;

        var parentDock = DockLayout is not null ? FindParentDock(DockLayout, _debugTerminal) : null;
        parentDock?.VisibleDockables?.Remove(_debugTerminal);

        _debugTerminal = null;
    }

    private void CleanupDebuggeeProcess()
    {
        if (_debuggeeProcess is null) return;

        try
        {
            if (!_debuggeeProcess.HasExited)
                _debuggeeProcess.Kill(entireProcessTree: true);
        }
        catch { /* process may have already exited */ }

        _debuggeeProcess.Dispose();
        _debuggeeProcess = null;
    }

    private static IDock? FindParentDock(IDockable root, IDockable target)
    {
        if (root is IDock dock && dock.VisibleDockables is not null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                if (child == target) return dock;
                var found = FindParentDock(child, target);
                if (found is not null) return found;
            }
        }
        return null;
    }

    private static async Task<int> WaitForPidFileAsync(string pidFile, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        while (!cts.Token.IsCancellationRequested)
        {
            if (System.IO.File.Exists(pidFile))
            {
                var content = await System.IO.File.ReadAllTextAsync(pidFile, cts.Token);
                if (int.TryParse(content.Trim(), out var pid))
                {
                    try { System.IO.File.Delete(pidFile); } catch { }
                    return pid;
                }
            }
            await Task.Delay(100, cts.Token);
        }
        throw new TimeoutException("Debuggee process did not start within the timeout period.");
    }

    private T? FindToolInDock<T>() where T : class
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

    private async Task InitializeGitAsync(string folderPath)
    {
        await _gitService.InitializeAsync(folderPath);
        CurrentBranch = _gitService.CurrentBranch ?? "";
        RefreshGitFiles();
        if (_gitService.IsRepository)
        {
            await RefreshGitBranches();
            await RefreshGitExtras();
        }
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

        // Auto-stage all changes if nothing is staged (like VS Code)
        if (GitStagedFiles.Count == 0 && GitChangedFiles.Count > 0)
        {
            await _gitService.StageAllAsync();
        }

        var result = await _gitService.CommitAsync(CommitMessage);
        if (result.Success)
        {
            StatusMessage = $"Committed: {result.CommitHash?[..7]}";
            CommitMessage = "";
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
            await RefreshGitExtras();
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
            await RefreshGitBranches();
            await RefreshGitExtras();
        }
    }

    [RelayCommand]
    private async Task GitInitRepository()
    {
        if (_workspacePath is null) return;

        // Auto-detect project type and create .gitignore before init
        var template = DetectGitignoreTemplate(_workspacePath);
        if (template is not null)
        {
            await _gitService.CreateGitignoreAsync(_workspacePath, template);
        }

        var result = await _gitService.InitRepositoryAsync(_workspacePath);
        if (result.Success)
        {
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
            await RefreshGitBranches();
            StatusMessage = template is not null
                ? $"Repository initialized with .gitignore ({template})"
                : "Repository initialized";
        }
        else
        {
            StatusMessage = $"Init failed: {result.ErrorMessage}";
        }
    }

    private static string? DetectGitignoreTemplate(string path)
    {
        if (Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.sln", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.slnx", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.fsproj", SearchOption.AllDirectories).Any())
            return "dotnet";

        if (File.Exists(Path.Combine(path, "package.json")))
            return "node";

        if (File.Exists(Path.Combine(path, "requirements.txt"))
            || File.Exists(Path.Combine(path, "pyproject.toml"))
            || Directory.EnumerateFiles(path, "*.py", SearchOption.TopDirectoryOnly).Any())
            return "python";

        if (File.Exists(Path.Combine(path, "go.mod")))
            return "go";

        if (File.Exists(Path.Combine(path, "Cargo.toml")))
            return "rust";

        if (File.Exists(Path.Combine(path, "pom.xml"))
            || File.Exists(Path.Combine(path, "build.gradle")))
            return "java";

        return null;
    }

    [RelayCommand]
    private async Task GitCreateGitignore(string? template)
    {
        if (_workspacePath is null || template is null) return;

        var result = await _gitService.CreateGitignoreAsync(_workspacePath, template);
        StatusMessage = result.Success ? ".gitignore created" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task GitCheckoutBranch(Branch? branch)
    {
        if (branch is null) return;
        try
        {
            await _gitService.CheckoutAsync(branch.Name);
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
            await RefreshGitBranches();

            // Reload file tree and open documents to reflect new branch
            if (_workspacePath is not null)
                await LoadFileTree(_workspacePath);
            await ReloadOpenDocumentsFromDisk();

            StatusMessage = $"Switched to branch: {branch.Name}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Checkout failed: {ex.Message}";
        }
    }

    private async Task ReloadOpenDocumentsFromDisk()
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

    public async Task GitCreateBranchAsync(string branchName, bool includeChanges = true)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return;
        var name = branchName.Trim();

        if (!includeChanges)
        {
            // Stash changes before switching, then drop the stash
            var hasChanges = _gitService.Status.Files.Count > 0;
            if (hasChanges)
                await _gitService.StashSaveAsync("temp-branch-create");

            await _gitService.CreateBranchAsync(name);
            await _gitService.CheckoutAsync(name);

            if (hasChanges)
                await _gitService.StashDropAsync(0);
        }
        else
        {
            await _gitService.CreateBranchAsync(name);
            await _gitService.CheckoutAsync(name);
        }

        CurrentBranch = _gitService.CurrentBranch ?? "";
        RefreshGitFiles();
        await RefreshGitBranches();
        await RefreshGitExtras();
        StatusMessage = $"Created and switched to: {name}";
    }

    public async Task GitDeleteBranchAsync(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return;
        var result = await _gitService.DeleteBranchAsync(branchName.Trim());
        if (result.Success)
        {
            await RefreshGitBranches();
            StatusMessage = $"Deleted branch: {branchName}";
        }
        else
        {
            StatusMessage = $"Delete failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitMergeBranch(Branch? branch)
    {
        if (branch is null) return;
        var result = await _gitService.MergeBranchAsync(branch.Name);
        if (result.Success)
        {
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
            StatusMessage = $"Merged {branch.Name} ({result.Status})";
        }
        else if (result.Status == Core.Interfaces.MergeStatus.Conflicts)
        {
            RefreshGitFiles();
            StatusMessage = $"Merge conflicts in {result.ConflictedFiles.Count} file(s)";
        }
        else
        {
            StatusMessage = $"Merge failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitStashSave()
    {
        var result = await _gitService.StashSaveAsync(includeUntracked: true);
        if (result.Success)
        {
            await RefreshGitExtras();
            StatusMessage = "Changes stashed";
        }
        else
        {
            StatusMessage = $"Stash failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitStashPop()
    {
        var result = await _gitService.StashPopAsync();
        if (result.Success)
        {
            await RefreshGitExtras();
            StatusMessage = "Stash popped";
        }
        else
        {
            StatusMessage = $"Pop failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitStashApply(StashEntry? entry)
    {
        var index = entry?.Index ?? 0;
        var result = await _gitService.StashApplyAsync(index);
        StatusMessage = result.Success ? $"Stash @{{{index}}} applied" : $"Apply failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task GitStashDrop(StashEntry? entry)
    {
        var index = entry?.Index ?? 0;
        var result = await _gitService.StashDropAsync(index);
        if (result.Success) await RefreshGitExtras();
        StatusMessage = result.Success ? $"Stash @{{{index}}} dropped" : $"Drop failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task GitCreateTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        var result = await _gitService.CreateTagAsync(tagName);
        if (result.Success)
        {
            await RefreshGitExtras();
            StatusMessage = $"Created tag: {tagName}";
        }
        else
        {
            StatusMessage = $"Tag failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitDeleteTag(Tag? tag)
    {
        if (tag is null) return;
        var result = await _gitService.DeleteTagAsync(tag.Name);
        if (result.Success)
        {
            await RefreshGitExtras();
            StatusMessage = $"Deleted tag: {tag.Name}";
        }
        else
        {
            StatusMessage = $"Delete tag failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitPush()
    {
        try
        {
            StatusMessage = "Pushing...";
            await _gitService.PushAsync();
            StatusMessage = "Push completed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Push failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GitPull()
    {
        try
        {
            StatusMessage = "Pulling...";
            await _gitService.PullAsync();
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshGitFiles();
            StatusMessage = "Pull completed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pull failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GitFetch()
    {
        try
        {
            StatusMessage = "Fetching...";
            await _gitService.FetchAsync();
            StatusMessage = "Fetch completed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Fetch failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task GitCherryPick(Commit? commit)
    {
        if (commit is null) return;
        var result = await _gitService.CherryPickAsync(commit.Hash);
        if (result.Success)
        {
            RefreshGitFiles();
            StatusMessage = $"Cherry-picked: {commit.Hash[..7]}";
        }
        else
        {
            StatusMessage = $"Cherry-pick failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task GitAddRemote(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return;
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        var result = await _gitService.AddRemoteAsync(parts[0], parts[1]);
        if (result.Success) await RefreshGitExtras();
        StatusMessage = result.Success ? $"Added remote: {parts[0]}" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task GitRemoveRemote(Remote? remote)
    {
        if (remote is null) return;
        var result = await _gitService.RemoveRemoteAsync(remote.Name);
        if (result.Success) await RefreshGitExtras();
        StatusMessage = result.Success ? $"Removed remote: {remote.Name}" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task GitLoadMoreHistory()
    {
        var moreCommits = await _gitService.GetLogAsync(limit: 50, skip: GitCommitLog.Count);
        foreach (var c in moreCommits)
            GitCommitLog.Add(c);
    }

    private async Task RefreshGitBranches()
    {
        _isRefreshingBranches = true;
        try
        {
            GitBranches.Clear();
            var branches = await _gitService.GetBranchesAsync();
            foreach (var b in branches)
                GitBranches.Add(b);

            _selectedGitBranch = GitBranches.FirstOrDefault(b => b.IsCurrent);
            OnPropertyChanged(nameof(SelectedGitBranch));
        }
        finally
        {
            _isRefreshingBranches = false;
        }
    }

    private async Task RefreshGitExtras()
    {
        GitStashes.Clear();
        GitTags.Clear();
        GitRemotes.Clear();
        GitCommitLog.Clear();

        var stashes = await _gitService.GetStashListAsync();
        foreach (var s in stashes) GitStashes.Add(s);

        var tags = await _gitService.GetTagsAsync();
        foreach (var t in tags) GitTags.Add(t);

        var remotes = await _gitService.GetRemotesAsync();
        foreach (var r in remotes) GitRemotes.Add(r);

        var commits = await _gitService.GetLogAsync(limit: 50);
        foreach (var c in commits) GitCommitLog.Add(c);

        OnPropertyChanged(nameof(HasGitStashes));
        OnPropertyChanged(nameof(HasGitTags));
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

    private void OnEditorDocumentOpened(object? sender, Core.Models.Document e)
    {
        var editorDoc = FindToolInDock<EditorDocumentViewModel>();
        if (editorDoc is not null)
            ActivateToolInDock(editorDoc);
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

        OnPropertyChanged(nameof(GitIsRepository));
        OnPropertyChanged(nameof(GitIsNotRepository));
        OnPropertyChanged(nameof(HasGitStashes));
        OnPropertyChanged(nameof(HasGitTags));
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

    private const int MaxSearchResults = 200;

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
        var resultsCapped = false;

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
                files = await Task.Run(() => EnumerateFilesSafe(_workspacePath), token);
            }

            var batch = new List<FileSearchResult>();

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
                            batch.Add(new FileSearchResult
                            {
                                FilePath = file,
                                RelativePath = System.IO.Path.GetRelativePath(_workspacePath, file),
                                LineNumber = i + 1,
                                LineText = lines[i].Trim(),
                            });

                            if (batch.Count >= MaxSearchResults)
                            {
                                resultsCapped = true;
                                break;
                            }
                        }
                    }
                    if (resultsCapped) break;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    // Skip files that can't be read
                }
            }

            foreach (var result in batch)
                SearchResults.Add(result);

            StatusMessage = resultsCapped
                ? $"Search: showing first {MaxSearchResults} result(s) for \"{query}\" (refine your query for more)"
                : $"Search: {SearchResults.Count} result(s) for \"{query}\"";
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
