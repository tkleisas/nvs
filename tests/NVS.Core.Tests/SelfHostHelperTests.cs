using NVS.Core.Models;

namespace NVS.Core.Tests;

public class SelfHostHelperTests
{
    [Fact]
    public void IsSelfHosted_ProcessUnderTargetTree_ReturnsTrue()
    {
        var target = Path.Combine(Path.GetTempPath(), "app", "App.slnx");
        var process = Path.Combine(Path.GetTempPath(), "app", "src", "App", "bin", "Debug", "net10.0", "App");

        SelfHostHelper.IsSelfHosted(target, process).Should().BeTrue();
    }

    [Fact]
    public void IsSelfHosted_ProcessOutsideTargetTree_ReturnsFalse()
    {
        var target = Path.Combine(Path.GetTempPath(), "app", "App.slnx");
        var process = Path.Combine(Path.GetTempPath(), "elsewhere", "ide", "nvs");

        SelfHostHelper.IsSelfHosted(target, process).Should().BeFalse();
    }

    [Fact]
    public void IsSelfHosted_NullProcessPath_ReturnsFalse()
    {
        SelfHostHelper.IsSelfHosted(Path.Combine(Path.GetTempPath(), "app", "App.slnx"), null)
            .Should().BeFalse();
    }

    [Fact]
    public void GetShadowDirectory_IsStableAndUnderTemp()
    {
        var solutionPath = Path.Combine("C:", "src", "My App", "MyApp.slnx");

        var first = SelfHostHelper.GetShadowDirectory(solutionPath);
        var second = SelfHostHelper.GetShadowDirectory(solutionPath);

        first.Should().Be(second, "debug/test flows rely on a stable location");
        first.Should().StartWith(Path.GetTempPath());
        first.Should().EndWith("MyApp");
    }

    [Fact]
    public void ShadowOutDirArgument_PointsAtShadowWithTrailingSeparator()
    {
        var arg = SelfHostHelper.ShadowOutDirArgument(Path.Combine("C:", "src", "App", "App.slnx"));

        arg.Should().StartWith("-p:OutDir=");
        arg.Should().EndWith(Path.DirectorySeparatorChar.ToString());
        arg.Should().Contain("nvs-shadow");
    }
}
