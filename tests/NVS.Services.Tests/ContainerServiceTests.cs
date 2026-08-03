using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Services.Containers;

namespace NVS.Services.Tests;

public class ContainerServiceTests
{
    [Fact]
    public void ParseContainers_ParsesDockerPsJson()
    {
        var json = """
            {"ID":"abc123def456","Image":"myapp:latest","Command":"dotnet myapp.dll","CreatedAt":"2026-08-03 10:00:00 +0300 EEST","Labels":"","LocalVolumes":"0","Mounts":"","Names":"myapp","Networks":"bridge","Ports":"0.0.0.0:8080->8080/tcp","Size":"0B","State":"running","Status":"Up 2 hours"}
            {"ID":"def456abc789","Image":"redis:7","Command":"docker-entrypoint.s…","CreatedAt":"2026-08-01 09:00:00 +0300 EEST","Labels":"","LocalVolumes":"1","Mounts":"/data","Names":"redis","Networks":"bridge","Ports":"","Size":"0B","State":"exited","Status":"Exited (0) 2 days ago"}
            """;

        var containers = ContainerService.ParseContainers(json);

        containers.Should().HaveCount(2);
        containers[0].Name.Should().Be("myapp");
        containers[0].State.Should().Be("running");
        containers[0].IsRunning.Should().BeTrue();
        containers[0].Ports.Should().Contain("8080");
        containers[1].State.Should().Be("exited");
        containers[1].IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ParseImages_ParsesDockerImagesJson()
    {
        var json = """
            {"Containers":"1","CreatedAt":"2026-08-03 10:00:00 +0300 EEST","CreatedSince":"2 hours ago","Digest":"\u003cnone\u003e","ID":"sha256:abc123","Repository":"myapp","SharedSize":"0B","Size":"120MB","Tag":"latest","UniqueSize":"120MB","VirtualSize":"120MB"}
            """;

        var images = ContainerService.ParseImages(json);

        images.Should().HaveCount(1);
        images[0].Repository.Should().Be("myapp");
        images[0].DisplayName.Should().Be("myapp:latest");
    }

    [Fact]
    public void ParseContainers_SkipsMalformedLines()
    {
        var containers = ContainerService.ParseContainers("not json\n{\"ID\":\"x\",\"Names\":\"ok\",\"Image\":\"i\",\"Status\":\"Up\",\"State\":\"running\",\"Ports\":\"\"}");

        containers.Should().HaveCount(1);
        containers[0].Name.Should().Be("ok");
    }

    [Theory]
    [InlineData(true, false, ContainerEngine.Docker)]
    [InlineData(true, true, ContainerEngine.Docker)]
    [InlineData(false, true, ContainerEngine.Podman)]
    [InlineData(false, false, ContainerEngine.None)]
    public void ChooseEngine_PrefersDocker(bool docker, bool podman, ContainerEngine expected)
    {
        ContainerService.ChooseEngine(docker, podman).Should().Be(expected);
    }

    [Fact]
    public async Task ListContainers_NoEngine_ReturnsEmpty()
    {
        var service = new ContainerService();

        var containers = await service.ListContainersAsync();

        containers.Should().BeEmpty();
    }

    [Fact]
    public async Task BuildImage_NoEngine_FailsWithGuidance()
    {
        var service = new ContainerService();

        var result = await service.BuildImageAsync("Dockerfile", "x:latest", ".");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("No container engine");
    }

    [Fact]
    public async Task BuildImage_UsesDockerBuildArguments()
    {
        var service = new ContainerService();
        string? capturedExe = null, capturedArgs = null;
        service.RunnerOverride = (exe, args, _, _) =>
        {
            capturedExe = exe;
            capturedArgs = args;
            return Task.FromResult((0, "", ""));
        };
        await service.RefreshEngineAsync();

        var result = await service.BuildImageAsync("/src/Dockerfile", "myapp:latest", "/src");

        result.Success.Should().BeTrue();
        capturedExe.Should().Be("docker");
        capturedArgs.Should().Contain("-t \"myapp:latest\"");
        capturedArgs.Should().Contain("-f \"/src/Dockerfile\"");
        capturedArgs.Should().Contain("\"/src\"");
    }

    [Fact]
    public async Task RunContainer_MapsPortsAndName()
    {
        var service = new ContainerService();
        string? capturedArgs = null;
        service.RunnerOverride = (exe, args, _, _) =>
        {
            capturedArgs = args;
            return Task.FromResult((0, "", ""));
        };
        await service.RefreshEngineAsync();

        await service.RunContainerAsync("myapp:latest", name: "web", portMappings: ["8080:8080"]);

        capturedArgs.Should().Contain("run -d");
        capturedArgs.Should().Contain("--name \"web\"");
        capturedArgs.Should().Contain("-p \"8080:8080\"");
        capturedArgs.Should().Contain("\"myapp:latest\"");
    }
}

public class DockerfileScaffolderTests
{
    private static ProjectModel ExeProject(string name = "MyApp", string tfm = "net10.0") => new()
    {
        FilePath = $"/src/{name}/{name}.csproj",
        Name = name,
        Sdk = "Microsoft.NET.Sdk",
        TargetFramework = tfm,
        OutputType = "Exe",
    };

    private static ProjectModel WebProject(string name = "MyApi") => new()
    {
        FilePath = $"/src/{name}/{name}.csproj",
        Name = name,
        Sdk = "Microsoft.NET.Sdk.Web",
        TargetFramework = "net10.0",
    };

    [Fact]
    public void FrameworkVersion_MapsMonikers()
    {
        DockerfileScaffolder.FrameworkVersion("net10.0").Should().Be("10.0");
        DockerfileScaffolder.FrameworkVersion("net8.0").Should().Be("8.0");
    }

    [Fact]
    public void GenerateDotNetDockerfile_Exe_HasEntrypointAndMultistage()
    {
        var dockerfile = DockerfileScaffolder.GenerateDotNetDockerfile(ExeProject());

        dockerfile.Should().Contain("FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build");
        dockerfile.Should().Contain("FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final");
        dockerfile.Should().Contain("dotnet restore \"MyApp.csproj\"");
        dockerfile.Should().Contain("dotnet publish \"MyApp.csproj\" -c Release");
        dockerfile.Should().Contain("ENTRYPOINT [\"dotnet\", \"MyApp.dll\"]");
    }

    [Fact]
    public void GenerateDotNetDockerfile_Web_UsesAspnetAndExposesPort()
    {
        var dockerfile = DockerfileScaffolder.GenerateDotNetDockerfile(WebProject());

        dockerfile.Should().Contain("FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final");
        dockerfile.Should().Contain("EXPOSE 8080");
        dockerfile.Should().Contain("ENTRYPOINT");
    }

    [Fact]
    public void GenerateDotNetDockerfile_Library_HasNoEntrypoint()
    {
        var dockerfile = DockerfileScaffolder.GenerateDotNetDockerfile(new ProjectModel
        {
            FilePath = "/src/Lib/Lib.csproj",
            Name = "Lib",
            Sdk = "Microsoft.NET.Sdk",
            TargetFramework = "net10.0",
        });

        dockerfile.Should().Contain("ENTRYPOINT intentionally omitted");
    }

    [Fact]
    public void GenerateCompose_CreatesServicePerExecutableProject()
    {
        var compose = DockerfileScaffolder.GenerateCompose("MySolution", "/src",
            [ExeProject("Api"), ExeProject("Worker"), new ProjectModel
            {
                FilePath = "/src/Lib/Lib.csproj",
                Name = "Lib",
                Sdk = "Microsoft.NET.Sdk",
                TargetFramework = "net10.0",
            }]);

        compose.Should().Contain("services:");
        compose.Should().Contain("  api:");
        compose.Should().Contain("  worker:");
        compose.Should().NotContain("  lib:");
        compose.Should().Contain("image: api:latest");
    }

    [Fact]
    public void GenerateCompose_WebProject_MapsPort()
    {
        var compose = DockerfileScaffolder.GenerateCompose("MySolution", "/src", [WebProject("Frontend")]);

        compose.Should().Contain("  frontend:");
        compose.Should().Contain("ports:");
        compose.Should().Contain("\"8080:8080\"");
    }

    [Fact]
    public void GenerateCompose_NoExecutables_LeavesGuidanceComment()
    {
        var compose = DockerfileScaffolder.GenerateCompose("MySolution", "/src", []);

        compose.Should().Contain("No executable projects");
    }
}
