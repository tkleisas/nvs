using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;

namespace NVS.ViewModels;

/// <summary>
/// Build, run, and stop-execution state and commands, owned by
/// <see cref="MainViewModel"/> and exposed to views as <c>Main.BuildRun</c>.
/// </summary>
public sealed partial class BuildRunViewModel : ObservableObject
{
    private readonly IBuildService _buildService;
    private readonly ISolutionService _solutionService;
    private readonly MainViewModel _main;

    private bool _isBuilding;
    private bool _isRunning;
    private CancellationTokenSource? _buildCts;
    private System.Diagnostics.Process? _runningProcess;

    public BuildRunViewModel(IBuildService buildService, ISolutionService solutionService, MainViewModel main)
    {
        _buildService = buildService;
        _solutionService = solutionService;
        _main = main;
    }

    public bool IsBuilding
    {
        get => _isBuilding;
        set
        {
            if (SetProperty(ref _isBuilding, value))
            {
                OnPropertyChanged(nameof(CanBuild));
                OnPropertyChanged(nameof(CanStop));
            }
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

    public bool CanBuild => !IsBuilding && _main.IsWorkspaceOpen;
    public bool CanRun => !IsRunning && _main.IsWorkspaceOpen;
    public bool CanStop => IsRunning || IsBuilding;

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
        if (!CanBuild || _main.WorkspacePath is null) return;

        IsBuilding = true;
        _main.StatusMessage = dotnetVerb == "clean" ? "Cleaning..." : "Building...";

        var buildOutput = _main.FindBuildOutputTool();
        var problemsTool = _main.FindProblemsTool();
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
                WorkingDirectory = _main.WorkspacePath
            };

            var result = await _buildService.RunTaskAsync(task, _buildCts.Token);

            if (dotnetVerb != "clean")
                problemsTool?.SetProblems(result.Errors, result.Warnings);

            _main.StatusMessage = dotnetVerb switch
            {
                "clean" => result.Success ? "Clean succeeded" : "Clean failed",
                _ => result.Success
                    ? $"Build succeeded ({result.Duration.TotalSeconds:F1}s)"
                    : $"Build failed — {result.Errors.Count} error(s), {result.Warnings.Count} warning(s)"
            };
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"{name} error: {ex.Message}";
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
        if (!CanRun || _main.WorkspacePath is null) return;

        IsRunning = true;
        _main.StatusMessage = "Building...";

        try
        {
            var startup = _solutionService.GetStartupProject();

            // Build first
            var buildOutput = _main.FindBuildOutputTool();
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
                WorkingDirectory = _main.WorkspacePath,
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
                _main.StatusMessage = $"Build failed — {buildResult.Errors.Count} error(s)";
                _main.FindProblemsTool()?.SetProblems(buildResult.Errors, buildResult.Warnings);
                return;
            }

            // Run — GUI apps (WinExe) launch as detached processes;
            // console apps run in the integrated terminal.
            _main.StatusMessage = "Running...";

            var isGuiApp = startup is not null
                && string.Equals(startup.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

            if (isGuiApp)
            {
                var projectArg = $" --project \"{startup!.FilePath}\"";
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run{projectArg} --nologo",
                    WorkingDirectory = _main.WorkspacePath,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                };
                _runningProcess = System.Diagnostics.Process.Start(psi);
                _main.StatusMessage = _runningProcess is not null
                    ? $"{startup.Name} launched (PID {_runningProcess.Id})"
                    : "Failed to start application";
            }
            else
            {
                var projectArg = startup is not null
                    ? $" --project \"{startup.FilePath}\""
                    : "";
                var runCommand = $"dotnet run{projectArg} --nologo";

                _main.Terminal.IsVisible = true;

                var terminalTool = _main.FindTerminalTool();
                if (terminalTool is not null)
                {
                    await terminalTool.SendCommandToTerminalAsync(runCommand);
                    _main.StatusMessage = "Application started in terminal";
                }
                else
                {
                    _main.StatusMessage = "Terminal not available — open a terminal first";
                }
            }
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Run error: {ex.Message}";
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private async Task StopExecution()
    {
        // Kill a detached GUI process if running
        if (_runningProcess is not null && !_runningProcess.HasExited)
        {
            try { _runningProcess.Kill(entireProcessTree: true); } catch { /* best effort */ }
            _runningProcess.Dispose();
            _runningProcess = null;
            _main.StatusMessage = "Application stopped";
            return;
        }

        // Kill the process tree immediately, then cancel the token
        await _buildService.CancelAsync();

        if (_buildCts is not null)
        {
            await _buildCts.CancelAsync();
            _main.StatusMessage = "Build cancelled";
        }
        else
        {
            _main.StatusMessage = "Stopped";
        }
    }
}
