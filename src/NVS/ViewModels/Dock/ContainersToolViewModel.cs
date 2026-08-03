using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Containers;

namespace NVS.ViewModels.Dock;

public partial class ContainersToolViewModel : Tool
{
    private readonly MainViewModel _main;
    private readonly IContainerService _containerService;

    /// <summary>The owning main view model (used by the view for pickers and output).</summary>
    public MainViewModel Main => _main;

    [ObservableProperty]
    private string _engineText = "Detecting engine…";

    [ObservableProperty]
    private bool _isEngineAvailable;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private ContainerInfo? _selectedContainer;

    [ObservableProperty]
    private ContainerImageInfo? _selectedImage;

    public ObservableCollection<ContainerInfo> Containers { get; } = [];
    public ObservableCollection<ContainerImageInfo> Images { get; } = [];

    /// <summary>Raised to ask the host for a project to scaffold (picker dialog in the view).</summary>
    public event EventHandler<ProjectPickRequest>? ProjectPickRequested;

    /// <summary>Raised to ask the host to show container logs in a window.</summary>
    public event EventHandler<string>? LogsRequested;

    public ContainersToolViewModel(MainViewModel main, IContainerService? containerService = null)
    {
        _main = main;
        _containerService = containerService ?? new ContainerService();
        Id = "Containers";
        Title = "🐳 Containers";
        CanClose = true;
        CanPin = true;

        _ = RefreshEngineAsync();
    }

    public IContainerService Service => _containerService;

    [RelayCommand]
    private async Task RefreshEngineAsync()
    {
        var engine = await _containerService.RefreshEngineAsync();
        IsEngineAvailable = engine != ContainerEngine.None;
        EngineText = engine switch
        {
            ContainerEngine.Docker => "Docker",
            ContainerEngine.Podman => "Podman",
            _ => "No container engine (install Docker or Podman)",
        };

        if (IsEngineAvailable)
        {
            await RefreshListsAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshListsAsync()
    {
        if (!IsEngineAvailable) return;

        IsBusy = true;
        try
        {
            var containers = await _containerService.ListContainersAsync(includeStopped: true);
            Containers.Clear();
            foreach (var container in containers)
            {
                Containers.Add(container);
            }

            var images = await _containerService.ListImagesAsync();
            Images.Clear();
            foreach (var image in images)
            {
                Images.Add(image);
            }

            StatusText = $"{Containers.Count} container(s), {Images.Count} image(s)";
        }
        catch (Exception ex)
        {
            StatusText = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartContainer()
    {
        if (SelectedContainer is null) return;
        await RunActionAsync(() => _containerService.StartContainerAsync(SelectedContainer.Id));
    }

    [RelayCommand]
    private async Task StopContainer()
    {
        if (SelectedContainer is null) return;
        await RunActionAsync(() => _containerService.StopContainerAsync(SelectedContainer.Id));
    }

    [RelayCommand]
    private async Task RemoveContainer()
    {
        if (SelectedContainer is null) return;
        await RunActionAsync(() => _containerService.RemoveContainerAsync(SelectedContainer.Id, force: SelectedContainer.IsRunning));
    }

    [RelayCommand]
    private async Task ShowLogs()
    {
        if (SelectedContainer is null) return;

        IsBusy = true;
        try
        {
            var logs = await _containerService.GetContainerLogsAsync(SelectedContainer.Id);
            LogsRequested?.Invoke(this, logs);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RunImage()
    {
        if (SelectedImage is null) return;

        var ports = SelectedImage.Repository.Contains("web") || SelectedImage.Repository.Contains("api")
            ? new List<string> { "8080:8080" }
            : null;

        var result = await _containerService.RunContainerAsync(SelectedImage.DisplayName, name: null, portMappings: ports);
        StatusText = result.Message;
        await RefreshListsAsync();
    }

    [RelayCommand]
    private async Task BuildImage()
    {
        var pick = ProjectPickRequested;
        if (pick is null)
        {
            StatusText = "No dockerfile picker available";
            return;
        }

        var request = new ProjectPickRequest();
        pick.Invoke(this, request);
        if (request.DockerfilePath is null || request.ImageTag is null) return;

        IsBusy = true;
        try
        {
            var context = Path.GetDirectoryName(request.DockerfilePath) ?? ".";
            var result = await _containerService.BuildImageAsync(
                request.DockerfilePath,
                request.ImageTag,
                context,
                line => _main.FindBuildOutputTool()?.AppendOutput(line, isError: false));
            StatusText = result.Message;
            _main.StatusMessage = result.Message;
            await RefreshListsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NewDockerfile()
    {
        var projects = _main.SolutionService.GetLoadedProjects();
        if (projects.Count == 0)
        {
            StatusText = "No solution loaded — open one to scaffold a Dockerfile";
            return;
        }

        var request = new ProjectPickRequest { ForScaffold = true };
        ProjectPickRequested?.Invoke(this, request);
        if (request.ScaffoldProject is null) return;

        try
        {
            var content = DockerfileScaffolder.GenerateDotNetDockerfile(request.ScaffoldProject);
            var path = Path.Combine(Path.GetDirectoryName(request.ScaffoldProject.FilePath)!, "Dockerfile");
            File.WriteAllText(path, content);
            StatusText = $"Dockerfile created: {path}";
            _main.StatusMessage = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to write Dockerfile: {ex.Message}";
        }
    }

    [RelayCommand]
    private void NewComposeFile()
    {
        var solution = _main.SolutionService.CurrentSolution;
        if (solution is null)
        {
            StatusText = "No solution loaded — open one to scaffold docker-compose.yml";
            return;
        }

        try
        {
            var solutionDir = Path.GetDirectoryName(solution.FilePath)!;
            var content = DockerfileScaffolder.GenerateCompose(solution.Name, solutionDir, _main.SolutionService.GetLoadedProjects());
            var path = Path.Combine(solutionDir, "docker-compose.yml");
            File.WriteAllText(path, content);
            StatusText = $"docker-compose.yml created: {path}";
            _main.StatusMessage = StatusText;
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to write compose file: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ComposeUp()
    {
        var composePath = FindComposeFile();
        if (composePath is null)
        {
            StatusText = "No docker-compose.yml in the solution root — create one first";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _containerService.ComposeUpAsync(
                composePath,
                line => _main.FindBuildOutputTool()?.AppendOutput(line, isError: false));
            StatusText = result.Message;
            await RefreshListsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ComposeDown()
    {
        var composePath = FindComposeFile();
        if (composePath is null)
        {
            StatusText = "No docker-compose.yml in the solution root";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _containerService.ComposeDownAsync(
                composePath,
                line => _main.FindBuildOutputTool()?.AppendOutput(line, isError: false));
            StatusText = result.Message;
            await RefreshListsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string? FindComposeFile()
    {
        var solution = _main.SolutionService.CurrentSolution;
        if (solution is null) return null;

        var path = Path.Combine(Path.GetDirectoryName(solution.FilePath)!, "docker-compose.yml");
        return File.Exists(path) ? path : null;
    }

    private async Task RunActionAsync(Func<Task<ContainerOperationResult>> action)
    {
        IsBusy = true;
        try
        {
            var result = await action();
            StatusText = result.Message;
            if (!result.Success)
            {
                _main.StatusMessage = result.Message;
            }
            await RefreshListsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }
}

/// <summary>Request object for the view's dockerfile/project picker dialog.</summary>
public sealed class ProjectPickRequest
{
    /// <summary>When true, the dialog picks a project to scaffold a Dockerfile for.</summary>
    public bool ForScaffold { get; init; }

    /// <summary>Set by the view: the project chosen for scaffolding.</summary>
    public ProjectModel? ScaffoldProject { get; set; }

    /// <summary>Set by the view: dockerfile to build from.</summary>
    public string? DockerfilePath { get; set; }

    /// <summary>Set by the view: image tag for the build.</summary>
    public string? ImageTag { get; set; }
}
