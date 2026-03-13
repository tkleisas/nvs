using System.Text.Json;
using NVS.Core.Models.NuGet;
using NVS.Services.NuGet;

namespace NVS.Services.Tests;

public sealed class NuGetPackageServiceTests : IDisposable
{
    private readonly string _testDir;

    public NuGetPackageServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"nvs_nuget_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    // --- Search Response Parsing ---

    [Fact]
    public void ParseSearchResponse_ShouldParseValidJson()
    {
        var json = """
        {
            "data": [
                {
                    "id": "Newtonsoft.Json",
                    "version": "13.0.3",
                    "description": "JSON framework for .NET",
                    "authors": ["James Newton-King"],
                    "totalDownloads": 1000000,
                    "verified": true,
                    "tags": ["json", "serialization"],
                    "versions": [
                        { "version": "13.0.3" },
                        { "version": "13.0.2" }
                    ]
                }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseSearchResponse(json);

        packages.Should().HaveCount(1);
        packages[0].Id.Should().Be("Newtonsoft.Json");
        packages[0].Version.Should().Be("13.0.3");
        packages[0].Description.Should().Contain("JSON");
        packages[0].TotalDownloads.Should().Be(1000000);
        packages[0].IsVerified.Should().BeTrue();
        packages[0].Tags.Should().Contain("json");
        packages[0].Versions.Should().HaveCount(2);
    }

    [Fact]
    public void ParseSearchResponse_MultiplePackages_ShouldParseAll()
    {
        var json = """
        {
            "data": [
                { "id": "PackageA", "version": "1.0.0" },
                { "id": "PackageB", "version": "2.0.0" },
                { "id": "PackageC", "version": "3.0.0" }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseSearchResponse(json);

        packages.Should().HaveCount(3);
        packages[0].Id.Should().Be("PackageA");
        packages[2].Id.Should().Be("PackageC");
    }

    [Fact]
    public void ParseSearchResponse_EmptyData_ShouldReturnEmpty()
    {
        var packages = NuGetPackageService.ParseSearchResponse("""{ "data": [] }""");
        packages.Should().BeEmpty();
    }

    [Fact]
    public void ParseSearchResponse_InvalidJson_ShouldReturnEmpty()
    {
        var packages = NuGetPackageService.ParseSearchResponse("not json");
        packages.Should().BeEmpty();
    }

    [Fact]
    public void ParseSearchResponse_MissingOptionalFields_ShouldUseDefaults()
    {
        var json = """
        {
            "data": [
                { "id": "Minimal", "version": "1.0.0" }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseSearchResponse(json);

        packages[0].Description.Should().BeEmpty();
        packages[0].Authors.Should().BeEmpty();
        packages[0].TotalDownloads.Should().Be(0);
        packages[0].IsVerified.Should().BeFalse();
        packages[0].Tags.Should().BeEmpty();
        packages[0].Versions.Should().BeEmpty();
    }

    // --- List Package JSON Parsing ---

    [Fact]
    public void ParseListPackageJson_ShouldParseInstalledPackages()
    {
        var json = """
        {
            "projects": [
                {
                    "path": "MyApp.csproj",
                    "frameworks": [
                        {
                            "framework": "net10.0",
                            "topLevelPackages": [
                                { "id": "Serilog", "requestedVersion": "4.0.0", "resolvedVersion": "4.0.0" },
                                { "id": "xunit", "requestedVersion": "2.9.0", "resolvedVersion": "2.9.0" }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseListPackageJson(json);

        packages.Should().HaveCount(2);
        packages[0].Id.Should().Be("Serilog");
        packages[0].RequestedVersion.Should().Be("4.0.0");
        packages[0].ResolvedVersion.Should().Be("4.0.0");
    }

    [Fact]
    public void ParseListPackageJson_Outdated_ShouldIncludeLatestVersion()
    {
        var json = """
        {
            "projects": [
                {
                    "path": "MyApp.csproj",
                    "frameworks": [
                        {
                            "framework": "net10.0",
                            "topLevelPackages": [
                                {
                                    "id": "Serilog",
                                    "requestedVersion": "3.0.0",
                                    "resolvedVersion": "3.0.0",
                                    "latestVersion": "4.0.0"
                                }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseListPackageJson(json, outdated: true);

        packages.Should().HaveCount(1);
        packages[0].LatestVersion.Should().Be("4.0.0");
        packages[0].HasUpdate.Should().BeTrue();
    }

    [Fact]
    public void ParseListPackageJson_DuplicateAcrossFrameworks_ShouldDeduplicate()
    {
        var json = """
        {
            "projects": [
                {
                    "path": "MyApp.csproj",
                    "frameworks": [
                        {
                            "framework": "net10.0",
                            "topLevelPackages": [
                                { "id": "Serilog", "requestedVersion": "4.0.0" }
                            ]
                        },
                        {
                            "framework": "net9.0",
                            "topLevelPackages": [
                                { "id": "Serilog", "requestedVersion": "4.0.0" }
                            ]
                        }
                    ]
                }
            ]
        }
        """;

        var packages = NuGetPackageService.ParseListPackageJson(json);
        packages.Should().HaveCount(1);
    }

    [Fact]
    public void ParseListPackageJson_InvalidJson_ShouldReturnEmpty()
    {
        var packages = NuGetPackageService.ParseListPackageJson("invalid");
        packages.Should().BeEmpty();
    }

    [Fact]
    public void ParseListPackageJson_EmptyProjects_ShouldReturnEmpty()
    {
        var packages = NuGetPackageService.ParseListPackageJson("""{ "projects": [] }""");
        packages.Should().BeEmpty();
    }

    // --- Csproj Fallback Parsing ---

    [Fact]
    public void ParseCsprojPackages_ShouldExtractPackageReferences()
    {
        var csproj = Path.Combine(_testDir, "Test.csproj");
        File.WriteAllText(csproj, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
            <PackageReference Include="Serilog" Version="4.0.0" />
          </ItemGroup>
        </Project>
        """);

        var packages = NuGetPackageService.ParseCsprojPackages(csproj);

        packages.Should().HaveCount(2);
        packages[0].Id.Should().Be("Newtonsoft.Json");
        packages[0].RequestedVersion.Should().Be("13.0.3");
        packages[1].Id.Should().Be("Serilog");
        packages[1].RequestedVersion.Should().Be("4.0.0");
    }

    [Fact]
    public void ParseCsprojPackages_NoPackages_ShouldReturnEmpty()
    {
        var csproj = Path.Combine(_testDir, "Empty.csproj");
        File.WriteAllText(csproj, """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """);

        var packages = NuGetPackageService.ParseCsprojPackages(csproj);
        packages.Should().BeEmpty();
    }

    [Fact]
    public void ParseCsprojPackages_FileNotFound_ShouldReturnEmpty()
    {
        var packages = NuGetPackageService.ParseCsprojPackages(Path.Combine(_testDir, "missing.csproj"));
        packages.Should().BeEmpty();
    }

    // --- Model Tests ---

    [Fact]
    public void InstalledPackage_HasUpdate_WhenLatestDiffers()
    {
        var pkg = new InstalledPackage
        {
            Id = "Test",
            RequestedVersion = "1.0.0",
            ResolvedVersion = "1.0.0",
            LatestVersion = "2.0.0"
        };

        pkg.HasUpdate.Should().BeTrue();
    }

    [Fact]
    public void InstalledPackage_NoUpdate_WhenVersionsMatch()
    {
        var pkg = new InstalledPackage
        {
            Id = "Test",
            RequestedVersion = "1.0.0",
            ResolvedVersion = "1.0.0",
            LatestVersion = "1.0.0"
        };

        pkg.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void InstalledPackage_NoUpdate_WhenLatestIsNull()
    {
        var pkg = new InstalledPackage
        {
            Id = "Test",
            RequestedVersion = "1.0.0"
        };

        pkg.HasUpdate.Should().BeFalse();
    }

    [Fact]
    public void PackageOperationResult_ShouldHoldAllFields()
    {
        var result = new PackageOperationResult
        {
            Success = true,
            PackageId = "MyPkg",
            Version = "1.0.0",
            Message = "Added",
            ErrorOutput = null
        };

        result.Success.Should().BeTrue();
        result.PackageId.Should().Be("MyPkg");
        result.Version.Should().Be("1.0.0");
    }
}
