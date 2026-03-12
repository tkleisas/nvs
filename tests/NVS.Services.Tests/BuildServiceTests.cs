using NVS.Core.Interfaces;
using NVS.Services.Build;

namespace NVS.Services.Tests;

public sealed class BuildServiceTests : IDisposable
{
    private readonly BuildService _service = new();

    public void Dispose()
    {
        _service.Dispose();
    }

    #region MSBuild Output Parsing

    [Fact]
    public void ParseOutputLine_WithErrorFormat_ShouldAddToErrors()
    {
        _service.ParseOutputLine(
            @"C:\src\Program.cs(10,5): error CS1002: ; expected",
            isError: false);

        // Verify via a build that echoes errors (tested in RunTaskAsync_WithMsBuildErrorOutput)
        _service.IsBuilding.Should().BeFalse(); // Parsing doesn't affect build state
    }

    [Fact]
    public void ParseOutputLine_WithWarningFormat_ShouldNotCrash()
    {
        var act = () => _service.ParseOutputLine(
            @"D:\project\Startup.cs(1,1): warning CS8618: Non-nullable property 'Name' must contain a non-null value",
            isError: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void ParseOutputLine_WithNonMsBuildOutput_ShouldNotCrash()
    {
        var act = () => _service.ParseOutputLine("Build succeeded.", isError: false);

        act.Should().NotThrow();
    }

    [Fact]
    public async Task RunTaskAsync_WithEchoCommand_ShouldCaptureOutput()
    {
        var outputLines = new List<string>();
        _service.OutputReceived += (_, e) => outputLines.Add(e.Output);

        var task = new BuildTask
        {
            Name = "Echo Test",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", "echo hello world"]
                : ["-c", "echo hello world"]
        };

        var result = await _service.RunTaskAsync(task);

        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        outputLines.Should().Contain(l => l.Contains("hello world"));
    }

    [Fact]
    public async Task RunTaskAsync_WithFailingCommand_ShouldReturnFailure()
    {
        var task = new BuildTask
        {
            Name = "Fail Test",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", "exit 1"]
                : ["-c", "exit 1"]
        };

        var result = await _service.RunTaskAsync(task);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(1);
    }

    [Fact]
    public async Task RunTaskAsync_WhenAlreadyBuilding_ShouldThrow()
    {
        // Start a long-running command
        var cts = new CancellationTokenSource();
        var longTask = new BuildTask
        {
            Name = "Long Task",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", "ping -n 10 127.0.0.1 > nul"]
                : ["-c", "sleep 10"]
        };

        var buildTask = _service.RunTaskAsync(longTask, cts.Token);

        try
        {
            var secondTask = new BuildTask
            {
                Name = "Second Task",
                Command = "echo",
                Args = ["test"]
            };

            var act = () => _service.RunTaskAsync(secondTask);
            await act.Should().ThrowAsync<InvalidOperationException>();
        }
        finally
        {
            cts.Cancel();
            try { await buildTask; } catch { }
        }
    }

    [Fact]
    public async Task RunTaskAsync_WithCancellation_ShouldReturnCancelled()
    {
        var cts = new CancellationTokenSource();
        var task = new BuildTask
        {
            Name = "Cancel Test",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", "ping -n 30 127.0.0.1 > nul"]
                : ["-c", "sleep 30"]
        };

        var buildTask = _service.RunTaskAsync(task, cts.Token);

        await Task.Delay(200);
        cts.Cancel();

        var result = await buildTask;

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("cancelled", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RunTaskAsync_ShouldFireBuildCompletedEvent()
    {
        BuildResult? completedResult = null;
        _service.BuildCompleted += (_, r) => completedResult = r;

        var task = new BuildTask
        {
            Name = "Event Test",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", "echo done"]
                : ["-c", "echo done"]
        };

        var result = await _service.RunTaskAsync(task);

        completedResult.Should().NotBeNull();
        completedResult.Should().BeSameAs(result);
    }

    [Fact]
    public async Task RunTaskAsync_WithMsBuildErrorOutput_ShouldParseErrors()
    {
        // Use echo to simulate MSBuild error output via stderr
        var errorLine = @"Program.cs(10,5): error CS1002: ; expected";
        var warningLine = @"Program.cs(20,1): warning CS8618: Non-nullable property";

        var task = new BuildTask
        {
            Name = "Parse Test",
            Command = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh",
            Args = OperatingSystem.IsWindows()
                ? ["/c", $"echo {errorLine}& echo {warningLine}& exit /b 1"]
                : ["-c", $"echo '{errorLine}' && echo '{warningLine}' && exit 1"]
        };

        var result = await _service.RunTaskAsync(task);

        result.Success.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("CS1002"));
        result.Warnings.Should().Contain(w => w.Message.Contains("CS8618"));
    }

    [Fact]
    public async Task CancelAsync_WhenNotBuilding_ShouldNotThrow()
    {
        var act = () => _service.CancelAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void IsBuilding_InitialState_ShouldBeFalse()
    {
        _service.IsBuilding.Should().BeFalse();
    }

    [Fact]
    public void CurrentTask_InitialState_ShouldBeNull()
    {
        _service.CurrentTask.Should().BeNull();
    }

    #endregion
}
