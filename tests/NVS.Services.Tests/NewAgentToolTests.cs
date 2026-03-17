using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Services.LLM.Tools;

namespace NVS.Services.Tests;

public sealed class NewAgentToolTests : IDisposable
{
    private readonly string _tempDir;

    public NewAgentToolTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"nvs-tools-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    #region RunTerminalCommandTool

    [Fact]
    public async Task RunTerminal_EchoCommand_ReturnsOutput()
    {
        var tool = new RunTerminalCommandTool(() => _tempDir);
        var args = JsonSerializer.Serialize(new { command = OperatingSystem.IsWindows() ? "echo hello" : "echo hello" });

        var result = await tool.ExecuteAsync(args);
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("exit_code").GetInt32().Should().Be(0);
        json.GetProperty("stdout").GetString()!.Trim().Should().Be("hello");
    }

    [Fact]
    public async Task RunTerminal_InvalidCommand_ReturnsNonZeroExitCode()
    {
        var tool = new RunTerminalCommandTool(() => _tempDir);
        var cmd = OperatingSystem.IsWindows() ? "cmd /c exit 1" : "false";
        var args = JsonSerializer.Serialize(new { command = cmd });

        var result = await tool.ExecuteAsync(args);
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("exit_code").GetInt32().Should().NotBe(0);
    }

    [Fact]
    public void RunTerminal_HasCorrectName()
    {
        var tool = new RunTerminalCommandTool(() => _tempDir);
        tool.Name.Should().Be("run_terminal");
    }

    [Fact]
    public void RunTerminal_HasValidParameterSchema()
    {
        var tool = new RunTerminalCommandTool(() => _tempDir);
        tool.ParameterSchema.GetProperty("type").GetString().Should().Be("object");
        tool.ParameterSchema.GetProperty("required").GetArrayLength().Should().Be(1);
    }

    #endregion

    #region GitStatusTool

    [Fact]
    public async Task GitStatus_NotARepo_ReturnsError()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(false);

        var tool = new GitStatusTool(() => mockGit);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("error").GetString().Should().Contain("Not a git repository");
    }

    [Fact]
    public async Task GitStatus_WithRepo_ReturnsBranchAndFiles()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(true);
        mockGit.CurrentBranch.Returns("main");
        mockGit.Status.Returns(new RepositoryStatus
        {
            HasUnstagedChanges = true,
            HasStagedChanges = false,
            AheadCount = 1,
            BehindCount = 0,
            Files =
            [
                new GitFileStatus { Path = "file.cs", Status = FileStatus.Modified, IsStaged = false }
            ]
        });

        var tool = new GitStatusTool(() => mockGit);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("branch").GetString().Should().Be("main");
        json.GetProperty("has_unstaged_changes").GetBoolean().Should().BeTrue();
        json.GetProperty("changed_count").GetInt32().Should().Be(1);
        json.GetProperty("staged_count").GetInt32().Should().Be(0);
        json.GetProperty("ahead").GetInt32().Should().Be(1);
    }

    [Fact]
    public void GitStatus_HasCorrectName()
    {
        var mockGit = Substitute.For<IGitService>();
        var tool = new GitStatusTool(() => mockGit);
        tool.Name.Should().Be("git_status");
    }

    #endregion

    #region GitDiffTool

    [Fact]
    public async Task GitDiff_NotARepo_ReturnsError()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(false);

        var tool = new GitDiffTool(() => mockGit);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("error").GetString().Should().Contain("Not a git repository");
    }

    [Fact]
    public async Task GitDiff_NoChanges_ReturnsEmpty()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(true);
        mockGit.GetDiffAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DiffHunk>());

        var tool = new GitDiffTool(() => mockGit);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("hunk_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task GitDiff_WithChanges_ReturnsDiffText()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(true);
        mockGit.GetDiffAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DiffHunk>
            {
                new()
                {
                    OldStart = 1, OldCount = 1, NewStart = 1, NewCount = 1,
                    Lines =
                    [
                        new DiffLine { Type = DiffLineType.Deletion, Content = "old" },
                        new DiffLine { Type = DiffLineType.Addition, Content = "new" }
                    ]
                }
            });

        var tool = new GitDiffTool(() => mockGit);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("hunk_count").GetInt32().Should().Be(1);
        json.GetProperty("diff").GetString().Should().Contain("-old");
        json.GetProperty("diff").GetString().Should().Contain("+new");
    }

    [Fact]
    public async Task GitDiff_Staged_CallsStagedDiff()
    {
        var mockGit = Substitute.For<IGitService>();
        mockGit.IsRepository.Returns(true);
        mockGit.GetStagedDiffAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DiffHunk>());

        var tool = new GitDiffTool(() => mockGit);
        var args = JsonSerializer.Serialize(new { staged = true });
        await tool.ExecuteAsync(args);

        await mockGit.Received(1).GetStagedDiffAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await mockGit.DidNotReceive().GetDiffAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void GitDiff_HasCorrectName()
    {
        var mockGit = Substitute.For<IGitService>();
        var tool = new GitDiffTool(() => mockGit);
        tool.Name.Should().Be("git_diff");
    }

    #endregion

    #region GetDiagnosticsTool

    [Fact]
    public async Task Diagnostics_Empty_ReturnsZeroTotal()
    {
        var tool = new GetDiagnosticsTool(() => []);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Diagnostics_WithItems_ReturnsAll()
    {
        var items = new List<GetDiagnosticsTool.DiagnosticInfo>
        {
            new() { FilePath = "a.cs", Line = 10, Column = 5, Severity = "error", Message = "CS0001" },
            new() { FilePath = "b.cs", Line = 20, Column = 1, Severity = "warning", Message = "CS0168" }
        };

        var tool = new GetDiagnosticsTool(() => items);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("total").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task Diagnostics_WithFilter_FiltersCorrectly()
    {
        var items = new List<GetDiagnosticsTool.DiagnosticInfo>
        {
            new() { FilePath = "a.cs", Line = 10, Column = 5, Severity = "error", Message = "CS0001" },
            new() { FilePath = "b.cs", Line = 20, Column = 1, Severity = "warning", Message = "CS0168" }
        };

        var tool = new GetDiagnosticsTool(() => items);
        var args = JsonSerializer.Serialize(new { severity = "error" });
        var result = await tool.ExecuteAsync(args);
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("total").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Diagnostics_HasCorrectName()
    {
        var tool = new GetDiagnosticsTool(() => []);
        tool.Name.Should().Be("get_diagnostics");
    }

    #endregion

    #region RunBuildTool

    [Fact]
    public async Task RunBuild_Success_ReturnsSuccessResult()
    {
        var mockBuild = Substitute.For<IBuildService>();
        mockBuild.RunTaskAsync(Arg.Any<BuildTask>(), Arg.Any<CancellationToken>())
            .Returns(new BuildResult
            {
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromSeconds(2),
                Errors = [],
                Warnings = []
            });

        var tool = new RunBuildTool(() => mockBuild, () => _tempDir);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("exit_code").GetInt32().Should().Be(0);
        json.GetProperty("error_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RunBuild_WithErrors_ReturnsErrorDetails()
    {
        var mockBuild = Substitute.For<IBuildService>();
        mockBuild.RunTaskAsync(Arg.Any<BuildTask>(), Arg.Any<CancellationToken>())
            .Returns(new BuildResult
            {
                Success = false,
                ExitCode = 1,
                Duration = TimeSpan.FromSeconds(1),
                Errors = [new BuildError { Message = "CS0001: bad code", FilePath = "a.cs", Line = 10, Column = 5 }],
                Warnings = []
            });

        var tool = new RunBuildTool(() => mockBuild, () => _tempDir);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("success").GetBoolean().Should().BeFalse();
        json.GetProperty("error_count").GetInt32().Should().Be(1);
    }

    [Fact]
    public void RunBuild_HasCorrectName()
    {
        var mockBuild = Substitute.For<IBuildService>();
        var tool = new RunBuildTool(() => mockBuild, () => _tempDir);
        tool.Name.Should().Be("run_build");
    }

    #endregion

    #region RunTestsTool

    [Fact]
    public async Task RunTests_Success_ReturnsSuccessResult()
    {
        var mockBuild = Substitute.For<IBuildService>();
        mockBuild.RunTaskAsync(Arg.Any<BuildTask>(), Arg.Any<CancellationToken>())
            .Returns(new BuildResult
            {
                Success = true,
                ExitCode = 0,
                Duration = TimeSpan.FromSeconds(5),
                Errors = [],
                Warnings = []
            });

        var tool = new RunTestsTool(() => mockBuild, () => _tempDir);
        var result = await tool.ExecuteAsync("{}");
        var json = JsonSerializer.Deserialize<JsonElement>(result);

        json.GetProperty("success").GetBoolean().Should().BeTrue();
        json.GetProperty("error_count").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task RunTests_WithFilter_PassesFilterArg()
    {
        var mockBuild = Substitute.For<IBuildService>();
        mockBuild.RunTaskAsync(Arg.Any<BuildTask>(), Arg.Any<CancellationToken>())
            .Returns(new BuildResult { Success = true, ExitCode = 0, Duration = TimeSpan.Zero });

        var tool = new RunTestsTool(() => mockBuild, () => _tempDir);
        var args = JsonSerializer.Serialize(new { filter = "FullyQualifiedName~MyTest" });
        await tool.ExecuteAsync(args);

        await mockBuild.Received(1).RunTaskAsync(
            Arg.Is<BuildTask>(t => t.Args.Contains("--filter") && t.Args.Contains("FullyQualifiedName~MyTest")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RunTests_HasCorrectName()
    {
        var mockBuild = Substitute.For<IBuildService>();
        var tool = new RunTestsTool(() => mockBuild, () => _tempDir);
        tool.Name.Should().Be("run_tests");
    }

    #endregion
}
