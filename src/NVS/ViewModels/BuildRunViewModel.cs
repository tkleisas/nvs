using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Launch;

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
    private bool _webAppRunning;
    private CancellationTokenSource? _buildCts;
    private System.Diagnostics.Process? _runningProcess;

    public BuildRunViewModel(IBuildService buildService, ISolutionService solutionService, MainViewModel main)
    {
        _buildService = buildService;
        _solutionService = solutionService;
        _main = main;
    }

    /// <summary>Resolves launch profiles for web projects. May be null in test contexts.</summary>
    public ILaunchSettingsService? LaunchSettingsService { get; set; }

    /// <summary>Used to open a browser when a web app starts. May be null in test contexts.</summary>
    public IBrowserLauncher? BrowserLauncher { get; set; }

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

            // Run — web apps run with the selected launch profile in the terminal
            // and (optionally) launch a browser to the profile's applicationUrl.
            // GUI apps (WinExe) launch as detached processes;
            // console apps run in the integrated terminal.
            _main.StatusMessage = "Running...";

            var isWebProject = startup is not null && startup.IsWebProject;
            var isGuiApp = startup is not null
                && string.Equals(startup.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);

            if (isWebProject)
            {
                await RunWebProjectAsync(startup!);
            }
            else if (isGuiApp)
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
            _webAppRunning = false;
        }
    }

    /// <summary>
    /// Runs a web project in the integrated terminal using <c>dotnet run --launch-profile</c>.
    /// Subscribes to the terminal's observable output for live "Now listening on:" URL
    /// scraping (falling back to the profile's static applicationUrl after a delay).
    /// Pressing Stop sends Ctrl+C to the terminal to kill the running dotnet process tree.
    /// </summary>
    private async Task RunWebProjectAsync(ProjectModel startup)
    {
        var profile = ResolveWebProfile(startup, out var hasProfiles);

        if (profile is not null && !profile.IsProjectLaunch)
        {
            _main.StatusMessage =
                $"Cannot run profile '{profile.Name}'. Only 'Project' launch profiles are supported. " +
                $"IIS Express, Docker, and Executable profiles are not implemented yet.";
            return;
        }

        var args = new StringBuilder("dotnet run --no-build");
        args.Append($" --project \"{startup.FilePath}\"");
        if (profile is not null && !string.IsNullOrWhiteSpace(profile.Name))
            args.Append($" --launch-profile \"{profile.Name}\"");
        args.Append(" --nologo");
        var runCommand = args.ToString();

        _main.Terminal.IsVisible = true;

        var terminalTool = _main.FindTerminalTool();
        if (terminalTool is null)
        {
            _main.StatusMessage = "Terminal not available — open a terminal first";
            return;
        }

        // Subscribe the URL watcher to the terminal's observable output (now possible
        // with the Porta.Pty-backed ProcessTerminal that exposes OutputObservable).
        var watcher = new NVS.Services.Launch.ListeningUrlWatcher();
        IDisposable? sub = null;
        if (terminalTool.Terminal is not null && profile?.LaunchBrowser == true)
        {
            sub = terminalTool.Terminal.OutputObservable.Subscribe(
                new NVS.Services.Terminal.ObserverAdapter<NVS.Core.Interfaces.TerminalOutputChunk>(
                    chunk => watcher.Append(chunk.Text),
                    () => { }));
            watcher.UrlDetected += url => Avalonia.Threading.Dispatcher.UIThread.Post(
                () => BrowserLauncher?.Launch(url));
        }

        await terminalTool.SendCommandToTerminalAsync(runCommand);
        _webAppRunning = true;

        if (profile is not null && profile.LaunchBrowser && BrowserLauncher is not null)
        {
            var url = WebLaunchHelper.ComposeBrowserUrl(profile);
            if (!string.IsNullOrEmpty(url))
                _ = LaunchBrowserDelayedAsync(url, TimeSpan.FromSeconds(6));
        }

        _main.StatusMessage = hasProfiles
            ? $"Web app started (profile: {profile?.Name ?? "default"})" + (profile?.LaunchBrowser == true ? " — opening browser…" : "")
            : "Web app started (no launch profiles)";

        // Unsubscribe the watcher when this method's fire-and-forget completes
        _ = Task.Run(async () =>
        {
            try { while (_webAppRunning) await Task.Delay(500); } catch { }
            sub?.Dispose();
        });
    }

    /// <summary>Resolves the launch profile selected by the user, or the project default.</summary>
    private LaunchProfile? ResolveWebProfile(ProjectModel startup, out bool hasProfiles)
    {
        hasProfiles = false;
        if (LaunchSettingsService is null)
            return null;

        var profiles = LaunchSettingsService.GetLaunchProfiles(startup);
        if (profiles.Count == 0)
            return null;
        hasProfiles = true;

        var selected = _main.SelectedLaunchProfile;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            var named = LaunchSettingsService.GetLaunchProfile(startup, selected);
            if (named is not null) return named;
        }

        return LaunchSettingsService.GetDefaultLaunchProfile(startup);
    }

    /// <summary>
    /// Fires off a browser launch after a delay without awaiting it, so the UI thread
    /// is not blocked while the web server comes up.
    /// </summary>
    private async Task LaunchBrowserDelayedAsync(string url, TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => BrowserLauncher?.Launch(url));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Delayed browser launch failed for {Url}", url);
        }
    }

    [RelayCommand]
    private async Task StopExecution()
    {
        // Web app running in terminal: send Ctrl+C to gracefully kill the dotnet process tree
        if (_webAppRunning)
        {
            _webAppRunning = false;
            var terminalTool = _main.FindTerminalTool();
            if (terminalTool?.Terminal is not null && terminalTool.Terminal.IsRunning)
            {
                await terminalTool.Terminal.SendInputAsync("\x03"); // Ctrl+C
                await Task.Delay(200);
                await terminalTool.Terminal.KillAsync();
            }
            _main.StatusMessage = "Web app stopped";
            return;
        }

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
