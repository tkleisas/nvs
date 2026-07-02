using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Core;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.ViewModels.Dock;

namespace NVS.ViewModels;

/// <summary>
/// Debug-session state, commands, and event handling, owned by
/// <see cref="MainViewModel"/> and exposed to views as <c>Main.Debug</c>.
/// </summary>
public sealed partial class DebugViewModel : ObservableObject
{
    private readonly IDebugService? _debugService;
    private readonly IBreakpointStore? _breakpointStore;
    private readonly ISolutionService _solutionService;
    private readonly IBuildService _buildService;
    private readonly MainViewModel _main;

    private bool _isDebugging;
    private bool _isDebugPaused;
    private bool _debugUsesTerminal;
    private int _debugSessionGeneration;
    private CancellationTokenSource? _pauseCts;
    private TerminalToolViewModel? _debugTerminal;
    private System.Diagnostics.Process? _debuggeeProcess;

    public DebugViewModel(
        IDebugService? debugService,
        IBreakpointStore? breakpointStore,
        ISolutionService solutionService,
        IBuildService buildService,
        MainViewModel main)
    {
        _debugService = debugService;
        _breakpointStore = breakpointStore;
        _solutionService = solutionService;
        _buildService = buildService;
        _main = main;

        if (_debugService is not null)
        {
            _debugService.DebuggingStarted += OnDebuggingStarted;
            _debugService.DebuggingStopped += OnDebuggingStopped;
            _debugService.DebuggingPaused += OnDebuggingPaused;
            _debugService.DebuggingContinued += OnDebuggingContinued;
            _debugService.OutputReceived += OnDebugOutput;
        }
    }

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

    [RelayCommand]
    private async Task StartDebugging()
    {
        if (_debugService is null)
        {
            _main.StatusMessage = "Debug service not available";
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
            _main.StatusMessage = "No solution loaded — cannot debug";
            return;
        }

        var startup = _solutionService.GetStartupProject();
        if (startup is null)
        {
            _main.StatusMessage = "No startup project set — cannot debug";
            return;
        }

        try
        {
            // Bump generation so stale OnDebuggingStopped posts from a previous
            // session will skip their cleanup and not destroy our new terminal.
            _debugSessionGeneration++;

            // Build first — debugger needs compiled output
            _main.StatusMessage = "Building before debug...";
            var buildOutput = _main.FindBuildOutputTool();
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
                WorkingDirectory = _main.WorkspacePath,
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
                _main.StatusMessage = $"Build failed — {buildResult.Errors.Count} error(s)";
                _main.FindProblemsTool()?.SetProblems(buildResult.Errors, buildResult.Warnings);
                return;
            }

            _main.StatusMessage = "Starting debugger...";
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

                // Signal files for the two-phase handshake with the startup hook:
                // ready = ConfigurationDone complete, go = breakpoints re-synced
                var readyFile = pidFile + ".ready";
                var goFile = pidFile + ".go";

                Serilog.Log.Debug("Debug session: pidFile={PidFile}", pidFile);

                CreateDebugTerminal(projectDir);

                // Build the terminal command to launch the debuggee with the hook
                string terminalCommand;
                if (OperatingSystem.IsWindows())
                {
                    terminalCommand = $"$env:DOTNET_STARTUP_HOOKS='{hookDll}'; " +
                                      $"$env:NVS_DEBUG_PID_FILE='{pidFile}'; " +
                                      $"$env:NVS_DEBUG_READY_FILE='{readyFile}'; " +
                                      $"$env:NVS_DEBUG_GO_FILE='{goFile}'; " +
                                      $"$env:NVS_DEBUG_PROGRAM='{programPath}'; " +
                                      $"dotnet exec \"{programPath}\"";
                }
                else
                {
                    terminalCommand = $"DOTNET_STARTUP_HOOKS='{hookDll}' " +
                                      $"NVS_DEBUG_PID_FILE='{pidFile}' " +
                                      $"NVS_DEBUG_READY_FILE='{readyFile}' " +
                                      $"NVS_DEBUG_GO_FILE='{goFile}' " +
                                      $"NVS_DEBUG_PROGRAM='{programPath}' " +
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

                // Phase 1: Signal that ConfigurationDone is complete.
                // The hook will pre-load the assembly, triggering module load in netcoredbg.
                try { await System.IO.File.WriteAllTextAsync(readyFile, "ready"); }
                catch { /* best effort */ }

                // Wait for the assembly to be loaded by the hook, then re-sync
                // breakpoints so they resolve against the now-loaded module.
                await Task.Delay(500);
                await _debugService.ResyncBreakpointsAsync();

                // Phase 2: Signal that breakpoints are re-synced — program can run.
                try { await System.IO.File.WriteAllTextAsync(goFile, "go"); }
                catch { /* best effort */ }
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
            _main.StatusMessage = $"Debug error: {ex.Message}";
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
            _main.StatusMessage = $"Stop debug error: {ex.Message}";
        }

        // Ensure UI resets even if the DebuggingStopped event didn't fire
        IsDebugging = false;
        IsDebugPaused = false;
        _debugUsesTerminal = false;
        _main.StatusMessage = "Debug session ended";
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
            _main.StatusMessage = $"Debug step-over error: {ex.Message}";
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
            _main.StatusMessage = $"Debug step-into error: {ex.Message}";
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
            _main.StatusMessage = $"Debug step-out error: {ex.Message}";
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
            _main.StatusMessage = $"Debug continue error: {ex.Message}";
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
            _main.StatusMessage = $"Debug pause error: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ToggleBreakpoint()
    {
        if (_breakpointStore is null || _main.Editor?.ActiveDocument is null) return;

        var doc = _main.Editor.ActiveDocument;
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
            _main.StatusMessage = $"Debugging: {session.Name}";
        });
    }

    private void OnDebuggingStopped(object? sender, DebugSession session)
    {
        _pauseCts?.Cancel();
        _pauseCts?.Dispose();
        _pauseCts = null;

        // Capture the current generation so the posted lambda can detect
        // whether a newer debug session has started in the meantime.
        var gen = _debugSessionGeneration;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            // A newer session is already running — don't destroy its state.
            if (gen != _debugSessionGeneration) return;

            IsDebugging = false;
            IsDebugPaused = false;
            _debugUsesTerminal = false;
            _main.StatusMessage = "Debug session ended";
            ClearDebugCurrentLine();
            ClearDebugEvaluateOnDocuments();
            DestroyDebugTerminal();
            CleanupDebuggeeProcess();

            // Clear call stack and variables
            _main.FindToolInDock<CallStackToolViewModel>()?.ClearFrames();
            _main.FindToolInDock<VariablesToolViewModel>()?.ClearVariables();
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
            _main.StatusMessage = "Paused";
        });

        if (_debugService is null) return;

        try
        {
            var threads = await _debugService.GetThreadsAsync(cts.Token);
            var activeThreadId = _debugService.ActiveThreadId;
            if (cts.Token.IsCancellationRequested) return;

            if (threads.Count > 0)
            {
                var frames = await _debugService.GetStackTraceAsync(activeThreadId, cts.Token);
                if (cts.Token.IsCancellationRequested) return;

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    _main.FindToolInDock<CallStackToolViewModel>()?.UpdateFrames(frames));

                if (frames.Count > 0)
                {
                    var topFrame = frames[0];
                    var vars = await _debugService.GetVariablesAsync(topFrame.Id, cts.Token);
                    if (cts.Token.IsCancellationRequested) return;

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        var varsTool = _main.FindToolInDock<VariablesToolViewModel>();
                        if (varsTool is not null)
                        {
                            varsTool.SetDebugService(_debugService);
                            varsTool.UpdateVariables(vars);
                        }

                        // Set up debug hover evaluate callback on all open documents
                        SetDebugEvaluateOnDocuments(topFrame.Id);
                    });

                    // Navigate to the stopped location in the editor
                    if (!string.IsNullOrEmpty(topFrame.Source) && topFrame.Line > 0)
                    {
                        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                        {
                            await _main.OpenFileAsync(topFrame.Source);
                            if (_main.Editor?.ActiveDocument is { } doc)
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
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "OnDebuggingPaused failed");
        }
    }

    private void OnDebuggingContinued(object? sender, EventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            IsDebugPaused = false;
            _main.StatusMessage = "Running...";
            ClearDebugCurrentLine();
            ClearDebugEvaluateOnDocuments();
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
        _main.FindBuildOutputTool()?.AppendOutput(evt.Output, isError);
    }

    private void ClearDebugCurrentLine()
    {
        if (_main.Editor?.OpenDocuments is not null)
        {
            foreach (var doc in _main.Editor.OpenDocuments)
                doc.DebugCurrentLine = null;
        }
    }

    private void SetDebugEvaluateOnDocuments(int frameId)
    {
        if (_main.Editor?.OpenDocuments is null || _debugService is null) return;

        var debugService = _debugService;
        Func<string, CancellationToken, Task<string?>> evaluateFunc =
            (expression, ct) => debugService.EvaluateAsync(expression, frameId, ct);

        foreach (var doc in _main.Editor.OpenDocuments)
            doc.DebugEvaluateFunc = evaluateFunc;
    }

    private void ClearDebugEvaluateOnDocuments()
    {
        if (_main.Editor?.OpenDocuments is not null)
        {
            foreach (var doc in _main.Editor.OpenDocuments)
                doc.DebugEvaluateFunc = null;
        }
    }

    private void CreateDebugTerminal(string workingDirectory)
    {
        DestroyDebugTerminal();

        _debugTerminal = new TerminalToolViewModel(_main)
        {
            Id = "DebugTerminal",
            Title = "🐛 Debug",
        };
        _debugTerminal.WorkingDirectory = workingDirectory;

        // Add the debug terminal to the same ToolDock as the regular terminal
        var existingTerminal = _main.FindTerminalTool();
        if (existingTerminal is not null)
        {
            var parentDock = FindParentDock(_main.DockLayout!, existingTerminal);
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

        var parentDock = _main.DockLayout is not null ? FindParentDock(_main.DockLayout, _debugTerminal) : null;
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
}
