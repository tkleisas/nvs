using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class ContainersToolViewModelTests : IDisposable
{
    private readonly string _dir;

    public ContainersToolViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NvsContainerTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private sealed class FakeContainerService : IContainerService
    {
        public ContainerEngine Engine { get; set; } = ContainerEngine.Docker;
        public (string Dockerfile, string Tag, string Context)? LastBuild;

        public Task<ContainerEngine> RefreshEngineAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Engine);
        public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(bool includeStopped = true, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerInfo>>([]);
        public Task<IReadOnlyList<ContainerImageInfo>> ListImagesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ContainerImageInfo>>([]);
        public Task<ContainerOperationResult> BuildImageAsync(string dockerfilePath, string imageTag, string contextDirectory, Action<string>? onOutput = null, CancellationToken cancellationToken = default)
        {
            LastBuild = (dockerfilePath, imageTag, contextDirectory);
            return Task.FromResult(ContainerOperationResult.Ok("built"));
        }
        public Task<ContainerOperationResult> RunContainerAsync(string image, string? name = null, IReadOnlyList<string>? portMappings = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
        public Task<ContainerOperationResult> StartContainerAsync(string containerId, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
        public Task<ContainerOperationResult> StopContainerAsync(string containerId, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
        public Task<ContainerOperationResult> RemoveContainerAsync(string containerId, bool force = false, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
        public Task<string> GetContainerLogsAsync(string containerId, int tailLines = 200, CancellationToken cancellationToken = default)
            => Task.FromResult("");
        public Task<ContainerOperationResult> ComposeUpAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
        public Task<ContainerOperationResult> ComposeDownAsync(string composeFilePath, Action<string>? onOutput = null, CancellationToken cancellationToken = default)
            => Task.FromResult(ContainerOperationResult.Ok());
    }

    private ProjectModel CreateExeProject(string name)
    {
        var projectDir = Path.Combine(_dir, name);
        Directory.CreateDirectory(projectDir);
        var projectPath = Path.Combine(projectDir, $"{name}.csproj");
        File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        return new ProjectModel
        {
            FilePath = projectPath,
            Name = name,
            Sdk = "Microsoft.NET.Sdk",
            TargetFramework = "net10.0",
            OutputType = "Exe",
        };
    }

    private MainViewModel CreateMain(IReadOnlyList<ProjectModel> projects)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        var solutionService = Substitute.For<ISolutionService>();
        solutionService.GetLoadedProjects().Returns(projects);
        return new MainViewModel(
            Substitute.For<IWorkspaceService>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IFileSystemService>(),
            new EditorViewModel(Substitute.For<IEditorService>(), Substitute.For<IFileSystemService>()),
            Substitute.For<IGitService>(),
            Substitute.For<ITerminalService>(),
            settings,
            solutionService,
            Substitute.For<IBuildService>());
    }

    [Fact]
    public void GenerateDockerfilesForAllProjects_CreatesForExecutablesOnly_AndSkipsExisting()
    {
        var api = CreateExeProject("Api");
        var worker = CreateExeProject("Worker");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(api.FilePath)!, "Dockerfile"), "# existing");
        var lib = new ProjectModel
        {
            FilePath = Path.Combine(_dir, "Lib.csproj"),
            Name = "Lib",
            Sdk = "Microsoft.NET.Sdk",
            TargetFramework = "net10.0",
        };

        var vm = new ContainersToolViewModel(CreateMain([api, worker, lib]), new FakeContainerService());

        var created = vm.GenerateDockerfilesForAllProjects();

        created.Should().Be(1);
        File.Exists(Path.Combine(Path.GetDirectoryName(worker.FilePath)!, "Dockerfile")).Should().BeTrue();
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(worker.FilePath)!, "Dockerfile"))
            .Should().Contain("ENTRYPOINT [\"dotnet\", \"Worker.dll\"]");
        File.ReadAllText(Path.Combine(Path.GetDirectoryName(api.FilePath)!, "Dockerfile"))
            .Should().Be("# existing");
    }

    [Fact]
    public async Task BuildDockerfileAsync_TagsImageWithProjectName()
    {
        var api = CreateExeProject("Api");
        var dockerfilePath = Path.Combine(Path.GetDirectoryName(api.FilePath)!, "Dockerfile");
        File.WriteAllText(dockerfilePath, "FROM scratch");

        var service = new FakeContainerService();
        var vm = new ContainersToolViewModel(CreateMain([api]), service);

        var success = await vm.BuildDockerfileAsync(api);

        success.Should().BeTrue();
        service.LastBuild.Should().NotBeNull();
        service.LastBuild!.Value.Tag.Should().Be("api:latest");
        service.LastBuild.Value.Dockerfile.Should().Be(dockerfilePath);
        service.LastBuild.Value.Context.Should().Be(Path.GetDirectoryName(api.FilePath));
    }

    [Fact]
    public async Task BuildDockerfileAsync_MissingDockerfile_Fails()
    {
        var api = CreateExeProject("Api");
        var vm = new ContainersToolViewModel(CreateMain([api]), new FakeContainerService());

        var success = await vm.BuildDockerfileAsync(api);

        success.Should().BeFalse();
    }

    [Fact]
    public async Task BuildDockerfileAsync_NoEngine_Fails()
    {
        var api = CreateExeProject("Api");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(api.FilePath)!, "Dockerfile"), "FROM scratch");

        var service = new FakeContainerService { Engine = ContainerEngine.None };
        var vm = new ContainersToolViewModel(CreateMain([api]), service);

        var success = await vm.BuildDockerfileAsync(api);

        success.Should().BeFalse();
    }
}
