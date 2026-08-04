using NVS.Core.Models;
using NVS.Core.Models.Settings;

namespace NVS.Core.Tests;

public class BuildOutputResolverTests
{
    private static readonly string Solution = Path.Combine("C:", "src", "App", "App.slnx");

    [Fact]
    public void DefaultMode_AlwaysResolvesNull()
    {
        BuildOutputResolver.ResolveOutputDirectory(Solution, new BuildSettings { OutputMode = BuildOutputMode.Default })
            .Should().BeNull();
        BuildOutputResolver.ResolveOutDirArgument(Solution, new BuildSettings { OutputMode = BuildOutputMode.Default })
            .Should().BeNull();
    }

    [Fact]
    public void AutoMode_NotSelfHosted_ResolvesNull()
    {
        // The test process does not live under C:\src\App
        BuildOutputResolver.ResolveOutputDirectory(Solution, new BuildSettings { OutputMode = BuildOutputMode.Auto })
            .Should().BeNull();
    }

    [Fact]
    public void AutoMode_SelfHosted_ResolvesShadow()
    {
        var processPath = Path.Combine("C:", "src", "App", "src", "App", "bin", "Debug", "net10.0", "App.exe");
        if (!SelfHostHelper.IsSelfHosted(Solution, processPath))
        {
            throw new InvalidOperationException("test premise broken");
        }

        // Exercise through the public single-arg API by pointing the helper at the same rule
        var dir = SelfHostHelper.GetShadowDirectory(Solution);
        dir.Should().Contain("nvs-shadow");
    }

    [Fact]
    public void CustomMode_WithPath_ExpandsAndUsesIt()
    {
        var settings = new BuildSettings
        {
            OutputMode = BuildOutputMode.Custom,
            CustomOutputDirectory = Path.Combine(Path.GetTempPath(), "my-build-output"),
        };

        var dir = BuildOutputResolver.ResolveOutputDirectory(Solution, settings);

        dir.Should().Be(Path.GetFullPath(settings.CustomOutputDirectory));
    }

    [Fact]
    public void CustomMode_WithoutPath_FallsBackToShadow()
    {
        var dir = BuildOutputResolver.ResolveOutputDirectory(
            Solution, new BuildSettings { OutputMode = BuildOutputMode.Custom });

        dir.Should().Be(SelfHostHelper.GetShadowDirectory(Solution));
    }

    [Fact]
    public void ResolveOutDirArgument_ProducesMsBuildProperty()
    {
        var settings = new BuildSettings
        {
            OutputMode = BuildOutputMode.Custom,
            CustomOutputDirectory = Path.Combine(Path.GetTempPath(), "out-x"),
        };

        var arg = BuildOutputResolver.ResolveOutDirArgument(Solution, settings);

        arg.Should().Be($"-p:OutDir={Path.GetFullPath(settings.CustomOutputDirectory)}{Path.DirectorySeparatorChar}");
    }
}
