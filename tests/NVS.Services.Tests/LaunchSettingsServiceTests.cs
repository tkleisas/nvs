using NVS.Core.Models;
using NVS.Services.Launch;

namespace NVS.Services.Tests;

public sealed class LaunchSettingsServiceTests : IDisposable
{
    private readonly LaunchSettingsService _service = new();
    private readonly string _tempDir;

    public LaunchSettingsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nvs_launch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    private ProjectModel ProjectAt(string dir, string sdk = "Microsoft.NET.Sdk.Web") => new()
    {
        FilePath = Path.Combine(dir, "App.csproj"),
        Name = "App",
        Sdk = sdk,
        TargetFramework = "net10.0",
        OutputType = "Exe",
    };

    private void WriteLaunchSettings(string json)
    {
        var propsDir = Path.Combine(_tempDir, "Properties");
        Directory.CreateDirectory(propsDir);
        File.WriteAllText(Path.Combine(propsDir, "launchSettings.json"), json);
    }

    [Fact]
    public void IsWebProject_WebSdk_DetectsWeb()
    {
        var model = ProjectAt(_tempDir, "Microsoft.NET.Sdk.Web");
        model.IsWebProject.Should().BeTrue();
    }

    [Fact]
    public void IsWebProject_ClassicSdk_NotWeb()
    {
        var model = new ProjectModel
        {
            FilePath = Path.Combine(_tempDir, "Lib.csproj"),
            Name = "Lib",
            Sdk = "Microsoft.NET.Sdk",
            TargetFramework = "net10.0",
            OutputType = "Library",
        };
        model.IsWebProject.Should().BeFalse();
    }

    [Fact]
    public void GetLaunchProfiles_NoFile_ReturnsEmpty()
    {
        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));
        profiles.Should().BeEmpty();
    }

    [Fact]
    public void GetLaunchProfiles_ParsesProjectProfile()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "http": {
              "commandName": "Project",
              "launchBrowser": false,
              "applicationUrl": "http://localhost:5062",
              "environmentVariables": {
                "ASPNETCORE_ENVIRONMENT": "Development"
              },
              "commandLineArgs": "--foo"
            }
          }
        }
        """);

        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));

        profiles.Should().ContainSingle();
        var p = profiles[0];
        p.Name.Should().Be("http");
        p.CommandName.Should().Be("Project");
        p.ApplicationUrl.Should().Be("http://localhost:5062");
        p.LaunchBrowser.Should().BeFalse();
        p.CommandLineArgs.Should().Be("--foo");
        p.IsProjectLaunch.Should().BeTrue();
        p.EnvironmentVariables["ASPNETCORE_ENVIRONMENT"].Should().Be("Development");
        p.FirstApplicationUrl.Should().Be("http://localhost:5062");
    }

    [Fact]
    public void GetLaunchProfiles_TwoApplicationUrls_ParsesFirstUrl()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "https": {
              "commandName": "Project",
              "applicationUrl": "https://localhost:5001;http://localhost:5000"
            }
          }
        }
        """);

        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));
        profiles[0].FirstApplicationUrl.Should().Be("https://localhost:5001");
    }

    [Fact]
    public void GetLaunchProfiles_LaunchBrowserAbsent_DefaultsTrue()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "http": { "commandName": "Project" }
          }
        }
        """);

        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));
        profiles[0].LaunchBrowser.Should().BeTrue();
    }

    [Fact]
    public void GetLaunchProfiles_LegacyNullCommandName_IsProjectLaunch()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "MyApp": {
              "commandName": null,
              "launchBrowser": true,
              "applicationUrl": "http://localhost:5000"
            }
          }
        }
        """);

        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));
        var p = profiles[0];
        p.CommandName.Should().BeNull();
        p.IsProjectLaunch.Should().BeTrue();
    }

    [Fact]
    public void GetLaunchProfiles_IisExpressProfile_IsNotProjectLaunch()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "IIS Express": { "commandName": "IISExpress" },
            "http": { "commandName": "Project", "applicationUrl": "http://localhost:5062" }
          }
        }
        """);

        var profiles = _service.GetLaunchProfiles(ProjectAt(_tempDir));
        profiles.Should().HaveCount(2);
        profiles.Single(p => p.Name == "IIS Express").IsProjectLaunch.Should().BeFalse();
        profiles.Single(p => p.Name == "http").IsProjectLaunch.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultLaunchProfile_PrefersProjectLaunch()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "IIS Express": { "commandName": "IISExpress" },
            "https": { "commandName": "Project", "applicationUrl": "https://localhost:5001;http://localhost:5000" },
            "http": { "commandName": "Project", "applicationUrl": "http://localhost:5062" }
          }
        }
        """);

        var def = _service.GetDefaultLaunchProfile(ProjectAt(_tempDir));
        def.Should().NotBeNull();
        def!.Name.Should().Be("https");
    }

    [Fact]
    public void GetDefaultLaunchProfile_NoProjectLaunch_FallsBackToFirst()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "IIS Express": { "commandName": "IISExpress" }
          }
        }
        """);

        var def = _service.GetDefaultLaunchProfile(ProjectAt(_tempDir));
        def.Should().NotBeNull();
        def!.Name.Should().Be("IIS Express");
    }

    [Fact]
    public void GetLaunchProfile_ByName_Resolves()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "http": { "commandName": "Project" },
            "https": { "commandName": "Project", "applicationUrl": "https://localhost:5001" }
          }
        }
        """);

        var p = _service.GetLaunchProfile(ProjectAt(_tempDir), "https");
        p.Should().NotBeNull();
        p!.ApplicationUrl.Should().Be("https://localhost:5001");
    }

    [Fact]
    public void GetLaunchProfile_UnknownName_ReturnsNull()
    {
        WriteLaunchSettings("""
        {
          "profiles": {
            "http": { "commandName": "Project" }
          }
        }
        """);

        _service.GetLaunchProfile(ProjectAt(_tempDir), "nope").Should().BeNull();
    }

    [Fact]
    public void GetLaunchProfiles_MalformedJson_ReturnsEmpty()
    {
        WriteLaunchSettings("{ totally broken");
        _service.GetLaunchProfiles(ProjectAt(_tempDir)).Should().BeEmpty();
    }
}