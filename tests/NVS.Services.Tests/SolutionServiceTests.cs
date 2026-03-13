using NVS.Core.Models;
using NVS.Services.Solution;

namespace NVS.Services.Tests;

public sealed class SolutionServiceTests : IDisposable
{
    private readonly SolutionService _service = new();
    private readonly string _tempDir;

    public SolutionServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nvs_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { }
    }

    #region DetectSolutionFile

    [Fact]
    public async Task DetectSolutionFileAsync_WithSlnxFile_ShouldReturnSlnxPath()
    {
        var slnxPath = Path.Combine(_tempDir, "MyApp.slnx");
        await File.WriteAllTextAsync(slnxPath, "<Solution></Solution>");

        var result = await _service.DetectSolutionFileAsync(_tempDir);

        result.Should().NotBeNull();
        result.Should().EndWith(".slnx");
    }

    [Fact]
    public async Task DetectSolutionFileAsync_WithSlnFile_ShouldReturnSlnPath()
    {
        var slnPath = Path.Combine(_tempDir, "MyApp.sln");
        await File.WriteAllTextAsync(slnPath, "Microsoft Visual Studio Solution File");

        var result = await _service.DetectSolutionFileAsync(_tempDir);

        result.Should().NotBeNull();
        result.Should().EndWith(".sln");
    }

    [Fact]
    public async Task DetectSolutionFileAsync_WithBothFormats_ShouldPreferSlnx()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "MyApp.sln"), "");
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "MyApp.slnx"), "<Solution></Solution>");

        var result = await _service.DetectSolutionFileAsync(_tempDir);

        result.Should().EndWith(".slnx");
    }

    [Fact]
    public async Task DetectSolutionFileAsync_WithNoSolution_ShouldReturnNull()
    {
        var result = await _service.DetectSolutionFileAsync(_tempDir);

        result.Should().BeNull();
    }

    [Fact]
    public async Task DetectSolutionFileAsync_WithNonexistentDirectory_ShouldReturnNull()
    {
        var result = await _service.DetectSolutionFileAsync(Path.Combine(_tempDir, "nonexistent"));

        result.Should().BeNull();
    }

    #endregion

    #region ParseSlnx

    [Fact]
    public async Task ParseSlnxAsync_WithValidFile_ShouldReturnSolution()
    {
        var slnxContent = """
            <Solution>
              <Project Path="src\MyApp\MyApp.csproj" />
              <Project Path="tests\MyApp.Tests\MyApp.Tests.csproj" />
            </Solution>
            """;
        var slnxPath = Path.Combine(_tempDir, "Test.slnx");
        await File.WriteAllTextAsync(slnxPath, slnxContent);

        var result = await SolutionService.ParseSlnxAsync(slnxPath);

        result.Name.Should().Be("Test");
        result.Format.Should().Be(NVS.Core.Models.SolutionFormat.Slnx);
        result.Projects.Should().HaveCount(2);
        result.Projects[0].Name.Should().Be("MyApp");
        result.Projects[1].Name.Should().Be("MyApp.Tests");
    }

    [Fact]
    public async Task ParseSlnxAsync_WithEmptyProjects_ShouldReturnEmptyList()
    {
        var slnxPath = Path.Combine(_tempDir, "Empty.slnx");
        await File.WriteAllTextAsync(slnxPath, "<Solution></Solution>");

        var result = await SolutionService.ParseSlnxAsync(slnxPath);

        result.Projects.Should().BeEmpty();
    }

    #endregion

    #region ParseSln

    [Fact]
    public async Task ParseSlnAsync_WithValidFile_ShouldReturnSolution()
    {
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            # Visual Studio Version 17
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\MyApp\MyApp.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp.Tests", "tests\MyApp.Tests\MyApp.Tests.csproj", "{B2C3D4E5-F6A7-8901-BCDE-F12345678901}"
            EndProject
            """;
        var slnPath = Path.Combine(_tempDir, "Test.sln");
        await File.WriteAllTextAsync(slnPath, slnContent);

        var result = await SolutionService.ParseSlnAsync(slnPath);

        result.Name.Should().Be("Test");
        result.Format.Should().Be(NVS.Core.Models.SolutionFormat.Sln);
        result.Projects.Should().HaveCount(2);
        result.Projects[0].Name.Should().Be("MyApp");
        result.Projects[1].Name.Should().Be("MyApp.Tests");
    }

    [Fact]
    public async Task ParseSlnAsync_ShouldSkipSolutionFolders()
    {
        var slnContent = """
            Microsoft Visual Studio Solution File, Format Version 12.00
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "src", "src", "{D1E2F3A4-B5C6-7890-1234-567890ABCDEF}"
            EndProject
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "MyApp", "src\MyApp\MyApp.csproj", "{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}"
            EndProject
            """;
        var slnPath = Path.Combine(_tempDir, "Test.sln");
        await File.WriteAllTextAsync(slnPath, slnContent);

        var result = await SolutionService.ParseSlnAsync(slnPath);

        result.Projects.Should().HaveCount(1);
        result.Projects[0].Name.Should().Be("MyApp");
    }

    #endregion

    #region ParseCsproj

    [Fact]
    public void ParseCsproj_WithExeProject_ShouldDetectOutputType()
    {
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
                <RootNamespace>MyApp</RootNamespace>
                <AssemblyName>MyApp</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Serilog" Version="4.0.0" />
                <PackageReference Include="Newtonsoft.Json" Version="13.0.0" />
              </ItemGroup>
              <ItemGroup>
                <ProjectReference Include="..\MyLib\MyLib.csproj" />
              </ItemGroup>
            </Project>
            """;
        var csprojPath = Path.Combine(_tempDir, "MyApp.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var result = SolutionService.ParseCsproj(csprojPath);

        result.Name.Should().Be("MyApp");
        result.Sdk.Should().Be("Microsoft.NET.Sdk");
        result.TargetFramework.Should().Be("net10.0");
        result.OutputType.Should().Be("Exe");
        result.IsExecutable.Should().BeTrue();
        result.RootNamespace.Should().Be("MyApp");
        result.AssemblyName.Should().Be("MyApp");
        result.PackageReferences.Should().HaveCount(2);
        result.ProjectReferences.Should().HaveCount(1);
    }

    [Fact]
    public void ParseCsproj_WithLibraryProject_ShouldNotBeExecutable()
    {
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """;
        var csprojPath = Path.Combine(_tempDir, "MyLib.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var result = SolutionService.ParseCsproj(csprojPath);

        result.OutputType.Should().BeNull();
        result.IsExecutable.Should().BeFalse();
    }

    [Fact]
    public void ParseCsproj_WithMultiTargetFramework_ShouldTakeFirst()
    {
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
              </PropertyGroup>
            </Project>
            """;
        var csprojPath = Path.Combine(_tempDir, "MyLib.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var result = SolutionService.ParseCsproj(csprojPath);

        result.TargetFramework.Should().Be("net8.0");
    }

    #endregion

    #region LoadSolution end-to-end

    [Fact]
    public async Task LoadSolutionAsync_WithSlnx_ShouldLoadAndDetectStartupProject()
    {
        // Create project files
        var srcDir = Path.Combine(_tempDir, "src", "MyApp");
        var testDir = Path.Combine(_tempDir, "tests", "MyApp.Tests");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(testDir);

        File.WriteAllText(Path.Combine(srcDir, "MyApp.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(testDir, "MyApp.Tests.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var slnxContent = """
            <Solution>
              <Project Path="src\MyApp\MyApp.csproj" />
              <Project Path="tests\MyApp.Tests\MyApp.Tests.csproj" />
            </Solution>
            """;
        var slnxPath = Path.Combine(_tempDir, "MyApp.slnx");
        await File.WriteAllTextAsync(slnxPath, slnxContent);

        SolutionModel? loadedSolution = null;
        _service.SolutionLoaded += (_, s) => { loadedSolution = s; };

        var result = await _service.LoadSolutionAsync(slnxPath);

        result.Should().NotBeNull();
        result.Projects.Should().HaveCount(2);
        result.StartupProjectPath.Should().Contain("MyApp.csproj");
        result.StartupProjectPath.Should().NotContain("Tests");
        _service.IsSolutionLoaded.Should().BeTrue();
        _service.CurrentSolution.Should().Be(result);
        loadedSolution.Should().Be(result);
    }

    [Fact]
    public async Task LoadSolutionAsync_ShouldSkipMissingProjects()
    {
        var slnxContent = """
            <Solution>
              <Project Path="nonexistent\Missing.csproj" />
            </Solution>
            """;
        var slnxPath = Path.Combine(_tempDir, "Test.slnx");
        await File.WriteAllTextAsync(slnxPath, slnxContent);

        var result = await _service.LoadSolutionAsync(slnxPath);

        result.Projects.Should().HaveCount(1); // reference is in solution
        _service.GetStartupProject().Should().BeNull(); // but couldn't be loaded
    }

    #endregion

    #region CloseSolution

    [Fact]
    public async Task CloseSolutionAsync_ShouldClearState()
    {
        var slnxPath = Path.Combine(_tempDir, "Test.slnx");
        await File.WriteAllTextAsync(slnxPath, "<Solution></Solution>");
        await _service.LoadSolutionAsync(slnxPath);

        bool closedFired = false;
        _service.SolutionClosed += (_, _) => closedFired = true;

        await _service.CloseSolutionAsync();

        _service.IsSolutionLoaded.Should().BeFalse();
        _service.CurrentSolution.Should().BeNull();
        closedFired.Should().BeTrue();
    }

    #endregion

    #region GetStartupProject

    [Fact]
    public async Task GetStartupProject_WithExecutableProject_ShouldReturnIt()
    {
        var projDir = Path.Combine(_tempDir, "src", "App");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "App.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>Exe</OutputType>
              </PropertyGroup>
            </Project>
            """);

        var slnxPath = Path.Combine(_tempDir, "Test.slnx");
        await File.WriteAllTextAsync(slnxPath, """
            <Solution>
              <Project Path="src\App\App.csproj" />
            </Solution>
            """);

        await _service.LoadSolutionAsync(slnxPath);

        var startup = _service.GetStartupProject();
        startup.Should().NotBeNull();
        startup!.Name.Should().Be("App");
        startup.IsExecutable.Should().BeTrue();
    }

    [Fact]
    public async Task GetStartupProject_WithNoExecutable_ShouldReturnNull()
    {
        var projDir = Path.Combine(_tempDir, "src", "Lib");
        Directory.CreateDirectory(projDir);
        File.WriteAllText(Path.Combine(projDir, "Lib.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        var slnxPath = Path.Combine(_tempDir, "Test.slnx");
        await File.WriteAllTextAsync(slnxPath, """
            <Solution>
              <Project Path="src\Lib\Lib.csproj" />
            </Solution>
            """);

        await _service.LoadSolutionAsync(slnxPath);

        _service.GetStartupProject().Should().BeNull();
    }

    #endregion

    #region Edge cases

    [Fact]
    public async Task LoadSolutionAsync_WithUnsupportedFormat_ShouldThrow()
    {
        var badPath = Path.Combine(_tempDir, "test.txt");
        await File.WriteAllTextAsync(badPath, "");

        var act = () => _service.LoadSolutionAsync(badPath);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task LoadSolutionAsync_WithMissingFile_ShouldThrow()
    {
        var act = () => _service.LoadSolutionAsync(Path.Combine(_tempDir, "missing.sln"));

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public void ParseCsproj_WithWinExeOutputType_ShouldBeExecutable()
    {
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <OutputType>WinExe</OutputType>
              </PropertyGroup>
            </Project>
            """;
        var csprojPath = Path.Combine(_tempDir, "WinApp.csproj");
        File.WriteAllText(csprojPath, csprojContent);

        var result = SolutionService.ParseCsproj(csprojPath);

        result.IsExecutable.Should().BeTrue();
    }

    #endregion

    #region CreateSolutionAsync

    [Fact]
    public async Task CreateSolutionAsync_ShouldCreateSlnFile()
    {
        var slnPath = await _service.CreateSolutionAsync("TestSolution", _tempDir);

        slnPath.Should().Contain("TestSolution");
        File.Exists(slnPath).Should().BeTrue();
    }

    [Fact]
    public async Task CreateSolutionAsync_WithEmptyName_ShouldThrow()
    {
        var act = () => _service.CreateSolutionAsync("", _tempDir);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateSolutionAsync_WithEmptyDirectory_ShouldThrow()
    {
        var act = () => _service.CreateSolutionAsync("Test", "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateSolutionAsync_CreatedSlnShouldBeParseable()
    {
        var slnPath = await _service.CreateSolutionAsync("TestSolution", _tempDir);

        var solution = await _service.LoadSolutionAsync(slnPath);

        solution.Name.Should().Be("TestSolution");
    }

    #endregion

    #region AddProjectToSolutionAsync

    [Fact]
    public async Task AddProjectToSolutionAsync_ShouldAddProject()
    {
        // Create a solution
        var slnPath = await _service.CreateSolutionAsync("TestSolution", _tempDir);

        // Create a minimal .csproj
        var projDir = Path.Combine(_tempDir, "MyApp");
        Directory.CreateDirectory(projDir);
        var csprojPath = Path.Combine(projDir, "MyApp.csproj");
        await File.WriteAllTextAsync(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        await _service.AddProjectToSolutionAsync(slnPath, csprojPath);

        // Verify the solution now contains the project
        var solution = await _service.LoadSolutionAsync(slnPath);
        solution.Projects.Should().ContainSingle(p => p.Name == "MyApp");
    }

    [Fact]
    public async Task AddProjectToSolutionAsync_WithMissingSln_ShouldThrow()
    {
        var missingSlnPath = Path.Combine(_tempDir, "missing.sln");
        var csprojPath = Path.Combine(_tempDir, "MyApp.csproj");
        await File.WriteAllTextAsync(csprojPath, "<Project></Project>");

        var act = () => _service.AddProjectToSolutionAsync(missingSlnPath, csprojPath);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task AddProjectToSolutionAsync_WithMissingProject_ShouldThrow()
    {
        var slnPath = await _service.CreateSolutionAsync("TestSolution", _tempDir);
        var missingCsproj = Path.Combine(_tempDir, "Missing.csproj");

        var act = () => _service.AddProjectToSolutionAsync(slnPath, missingCsproj);
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    #endregion
}
