using FluentAssertions;
using LibGit2Sharp;
using NVS.Core.Interfaces;
using NVS.Services.Git;
using RepositoryStatus = NVS.Core.Interfaces.RepositoryStatus;

namespace NVS.Services.Tests;

public sealed class GitServiceIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly GitService _gitService;

    public GitServiceIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "nvs-git-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _gitService = new GitService();
    }

    private void InitBareRepo()
    {
        Repository.Init(_tempDir);
        using var repo = new Repository(_tempDir);
        repo.Config.Set("user.name", "Test User");
        repo.Config.Set("user.email", "test@example.com");
    }

    private void CreateAndCommitFile(string name, string content)
    {
        File.WriteAllText(Path.Combine(_tempDir, name), content);
        using var repo = new Repository(_tempDir);
        Commands.Stage(repo, name);
        var sig = new Signature("Test User", "test@example.com", DateTimeOffset.Now);
        repo.Commit($"Add {name}", sig, sig);
    }

    [Fact]
    public async Task InitializeAsync_WithGitRepo_SetsIsRepository()
    {
        InitBareRepo();

        await _gitService.InitializeAsync(_tempDir);

        _gitService.IsRepository.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WithNonGitDir_IsRepositoryFalse()
    {
        await _gitService.InitializeAsync(_tempDir);

        _gitService.IsRepository.Should().BeFalse();
    }

    [Fact]
    public async Task CurrentBranch_AfterInit_ReturnsBranchName()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# Hello");

        await _gitService.InitializeAsync(_tempDir);

        _gitService.CurrentBranch.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task StageAsync_NewFile_ShowsInStagedFiles()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "new.txt"), "new content");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.StageAsync("new.txt");

        _gitService.Status.HasStagedChanges.Should().BeTrue();
        _gitService.Status.Files.Should().Contain(f => f.Path == "new.txt" && f.IsStaged);
    }

    [Fact]
    public async Task StageAllAsync_MultiplePendingFiles_StagesAll()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "a.txt"), "aaa");
        File.WriteAllText(Path.Combine(_tempDir, "b.txt"), "bbb");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.StageAllAsync();

        _gitService.Status.Files.Where(f => f.IsStaged).Should().HaveCountGreaterOrEqualTo(2);
    }

    [Fact]
    public async Task UnstageAsync_StagedFile_MovesToUnstaged()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "new.txt"), "new content");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.StageAsync("new.txt");
        await _gitService.UnstageAsync("new.txt");

        _gitService.Status.Files.Should().Contain(f => f.Path == "new.txt" && !f.IsStaged);
    }

    [Fact]
    public async Task CommitAsync_WithStagedChanges_Succeeds()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "new.txt"), "content");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.StageAsync("new.txt");
        var result = await _gitService.CommitAsync("test commit");

        result.Success.Should().BeTrue();
        result.CommitHash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task CommitAsync_WithNoRepo_FailsGracefully()
    {
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.CommitAsync("should fail");

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetLogAsync_AfterCommits_ReturnsHistory()
    {
        InitBareRepo();
        CreateAndCommitFile("a.txt", "aaa");
        CreateAndCommitFile("b.txt", "bbb");

        await _gitService.InitializeAsync(_tempDir);
        var commits = await _gitService.GetLogAsync();

        commits.Should().HaveCountGreaterOrEqualTo(2);
        commits[0].Author.Should().Be("Test User");
        commits[0].Hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetBranchesAsync_DefaultBranch_ReturnsAtLeastOne()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        var branches = await _gitService.GetBranchesAsync();

        branches.Should().NotBeEmpty();
        branches.Should().Contain(b => b.IsCurrent);
    }

    [Fact]
    public async Task CreateBranchAsync_CreatesBranch()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.CreateBranchAsync("feature-test");
        var branches = await _gitService.GetBranchesAsync();

        branches.Should().Contain(b => b.Name == "feature-test");
    }

    [Fact]
    public async Task CheckoutAsync_SwitchesBranch()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.CreateBranchAsync("feature-checkout");
        await _gitService.CheckoutAsync("feature-checkout");

        _gitService.CurrentBranch.Should().Be("feature-checkout");
    }

    [Fact]
    public async Task GetDiffAsync_ModifiedFile_ReturnsHunks()
    {
        InitBareRepo();
        CreateAndCommitFile("test.txt", "line 1\nline 2\nline 3\n");
        File.WriteAllText(Path.Combine(_tempDir, "test.txt"), "line 1\nmodified line 2\nline 3\nnew line 4\n");

        await _gitService.InitializeAsync(_tempDir);
        var hunks = await _gitService.GetDiffAsync();

        hunks.Should().NotBeEmpty();
        hunks.SelectMany(h => h.Lines).Should().Contain(l => l.Type == DiffLineType.Addition);
    }

    [Fact]
    public async Task StatusChanged_OnStage_FiresEvent()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "new.txt"), "new");

        await _gitService.InitializeAsync(_tempDir);

        RepositoryStatus? receivedStatus = null;
        _gitService.StatusChanged += (_, s) => receivedStatus = s;

        await _gitService.StageAsync("new.txt");

        receivedStatus.Should().NotBeNull();
        receivedStatus!.HasStagedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Status_UntrackedFile_ShowsAsUntracked()
    {
        InitBareRepo();
        CreateAndCommitFile("initial.txt", "initial");
        File.WriteAllText(Path.Combine(_tempDir, "untracked.txt"), "data");

        await _gitService.InitializeAsync(_tempDir);

        _gitService.Status.Files.Should().Contain(f =>
            f.Path == "untracked.txt" &&
            f.Status == Core.Interfaces.FileStatus.Untracked &&
            !f.IsStaged);
    }

    public void Dispose()
    {
        _gitService.Dispose();
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* temp dir cleanup best-effort */ }
    }
}
