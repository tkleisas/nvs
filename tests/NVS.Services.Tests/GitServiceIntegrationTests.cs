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

    // ── Init Repository Tests ──────────────────────────────────────

    [Fact]
    public async Task InitRepositoryAsync_CreatesNewRepo()
    {
        var emptyDir = Path.Combine(Path.GetTempPath(), "nvs-git-init-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(emptyDir);
        try
        {
            var result = await _gitService.InitRepositoryAsync(emptyDir);

            result.Success.Should().BeTrue();
            _gitService.IsRepository.Should().BeTrue();
            Directory.Exists(Path.Combine(emptyDir, ".git")).Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(emptyDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetGitignoreTemplates_ReturnsTemplates()
    {
        var templates = _gitService.GetGitignoreTemplates();

        templates.Should().Contain("dotnet");
        templates.Should().Contain("node");
        templates.Should().Contain("python");
    }

    [Fact]
    public async Task CreateGitignoreAsync_WritesFile()
    {
        InitBareRepo();
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.CreateGitignoreAsync(_tempDir, "dotnet");

        result.Success.Should().BeTrue();
        var content = File.ReadAllText(Path.Combine(_tempDir, ".gitignore"));
        content.Should().Contain("bin/");
        content.Should().Contain("obj/");
    }

    [Fact]
    public async Task CreateGitignoreAsync_UnknownTemplate_Fails()
    {
        InitBareRepo();
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.CreateGitignoreAsync(_tempDir, "unknown-lang");

        result.Success.Should().BeFalse();
    }

    // ── Delete / Rename Branch Tests ───────────────────────────────

    [Fact]
    public async Task DeleteBranchAsync_RemovesBranch()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.CreateBranchAsync("to-delete");
        var result = await _gitService.DeleteBranchAsync("to-delete");

        result.Success.Should().BeTrue();
        var branches = await _gitService.GetBranchesAsync();
        branches.Should().NotContain(b => b.Name == "to-delete");
    }

    [Fact]
    public async Task DeleteBranchAsync_CurrentBranch_Fails()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        var currentBranch = _gitService.CurrentBranch!;

        var result = await _gitService.DeleteBranchAsync(currentBranch);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("current branch");
    }

    [Fact]
    public async Task RenameBranchAsync_RenamesBranch()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        await _gitService.CreateBranchAsync("old-name");
        var result = await _gitService.RenameBranchAsync("old-name", "new-name");

        result.Success.Should().BeTrue();
        var branches = await _gitService.GetBranchesAsync();
        branches.Should().Contain(b => b.Name == "new-name");
        branches.Should().NotContain(b => b.Name == "old-name");
    }

    // ── Merge Tests ────────────────────────────────────────────────

    [Fact]
    public async Task MergeBranchAsync_FastForward_Succeeds()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");

        await _gitService.InitializeAsync(_tempDir);
        var mainBranch = _gitService.CurrentBranch!;

        await _gitService.CreateBranchAsync("feature");
        await _gitService.CheckoutAsync("feature");
        // Create a commit on feature
        File.WriteAllText(Path.Combine(_tempDir, "feature.txt"), "feature work");
        await _gitService.StageAsync("feature.txt");
        await _gitService.CommitAsync("feature commit");

        await _gitService.CheckoutAsync(mainBranch);
        var result = await _gitService.MergeBranchAsync("feature");

        result.Success.Should().BeTrue();
        result.Status.Should().Be(Core.Interfaces.MergeStatus.FastForward);
    }

    [Fact]
    public async Task MergeBranchAsync_NonExistentBranch_Fails()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.MergeBranchAsync("nonexistent");

        result.Success.Should().BeFalse();
    }

    // ── Stash Tests ────────────────────────────────────────────────

    [Fact]
    public async Task StashSaveAsync_SavesChanges()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# modified");
        await _gitService.StageAsync("readme.md");

        var result = await _gitService.StashSaveAsync("test stash");

        result.Success.Should().BeTrue();
        _gitService.Status.HasStagedChanges.Should().BeFalse();
    }

    [Fact]
    public async Task GetStashListAsync_AfterStash_ReturnsList()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# stashed");
        await _gitService.StageAsync("readme.md");
        await _gitService.StashSaveAsync("my stash");

        var stashes = await _gitService.GetStashListAsync();

        stashes.Should().HaveCount(1);
        stashes[0].Index.Should().Be(0);
        stashes[0].Message.Should().Contain("my stash");
    }

    [Fact]
    public async Task StashPopAsync_RestoresChanges()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# stash-pop");
        await _gitService.StageAsync("readme.md");
        await _gitService.StashSaveAsync("pop test");

        var result = await _gitService.StashPopAsync();

        result.Success.Should().BeTrue();
        var stashes = await _gitService.GetStashListAsync();
        stashes.Should().BeEmpty();
    }

    [Fact]
    public async Task StashApplyAsync_KeepsStashEntry()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# apply");
        await _gitService.StageAsync("readme.md");
        await _gitService.StashSaveAsync("apply test");

        var result = await _gitService.StashApplyAsync();

        result.Success.Should().BeTrue();
        var stashes = await _gitService.GetStashListAsync();
        stashes.Should().HaveCount(1);
    }

    [Fact]
    public async Task StashDropAsync_RemovesEntry()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        File.WriteAllText(Path.Combine(_tempDir, "readme.md"), "# drop");
        await _gitService.StageAsync("readme.md");
        await _gitService.StashSaveAsync("drop test");

        var result = await _gitService.StashDropAsync();

        result.Success.Should().BeTrue();
        var stashes = await _gitService.GetStashListAsync();
        stashes.Should().BeEmpty();
    }

    // ── Tag Tests ──────────────────────────────────────────────────

    [Fact]
    public async Task CreateTagAsync_LightweightTag_Succeeds()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.CreateTagAsync("v1.0.0");

        result.Success.Should().BeTrue();
        var tags = await _gitService.GetTagsAsync();
        tags.Should().Contain(t => t.Name == "v1.0.0");
        tags.First(t => t.Name == "v1.0.0").IsAnnotated.Should().BeFalse();
    }

    [Fact]
    public async Task CreateTagAsync_AnnotatedTag_Succeeds()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.CreateTagAsync("v2.0.0", message: "Release 2.0");

        result.Success.Should().BeTrue();
        var tags = await _gitService.GetTagsAsync();
        var tag = tags.First(t => t.Name == "v2.0.0");
        tag.IsAnnotated.Should().BeTrue();
        tag.Message.Should().Contain("Release 2.0");
    }

    [Fact]
    public async Task DeleteTagAsync_RemovesTag()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);
        await _gitService.CreateTagAsync("to-delete");

        var result = await _gitService.DeleteTagAsync("to-delete");

        result.Success.Should().BeTrue();
        var tags = await _gitService.GetTagsAsync();
        tags.Should().NotContain(t => t.Name == "to-delete");
    }

    // ── Cherry-Pick Tests ──────────────────────────────────────────

    [Fact]
    public async Task CherryPickAsync_AppliesCommit()
    {
        InitBareRepo();
        CreateAndCommitFile("readme.md", "# test");
        await _gitService.InitializeAsync(_tempDir);

        var mainBranch = _gitService.CurrentBranch!;

        // Create feature branch with a commit
        await _gitService.CreateBranchAsync("cp-feature");
        await _gitService.CheckoutAsync("cp-feature");
        File.WriteAllText(Path.Combine(_tempDir, "cherry.txt"), "cherry-pick content");
        await _gitService.StageAsync("cherry.txt");
        await _gitService.CommitAsync("cherry commit");

        var log = await _gitService.GetLogAsync(1);
        var cherryCommitSha = log[0].Hash;

        // Go back to main and cherry-pick
        await _gitService.CheckoutAsync(mainBranch);
        var result = await _gitService.CherryPickAsync(cherryCommitSha);

        result.Success.Should().BeTrue();
        File.Exists(Path.Combine(_tempDir, "cherry.txt")).Should().BeTrue();
    }

    // ── Remote Tests ───────────────────────────────────────────────

    [Fact]
    public async Task AddRemoteAsync_AddsRemote()
    {
        InitBareRepo();
        await _gitService.InitializeAsync(_tempDir);

        var result = await _gitService.AddRemoteAsync("upstream", "https://example.com/repo.git");

        result.Success.Should().BeTrue();
        var remotes = await _gitService.GetRemotesAsync();
        remotes.Should().Contain(r => r.Name == "upstream" && r.Url == "https://example.com/repo.git");
    }

    [Fact]
    public async Task RemoveRemoteAsync_RemovesRemote()
    {
        InitBareRepo();
        await _gitService.InitializeAsync(_tempDir);
        await _gitService.AddRemoteAsync("to-remove", "https://example.com/repo.git");

        var result = await _gitService.RemoveRemoteAsync("to-remove");

        result.Success.Should().BeTrue();
        var remotes = await _gitService.GetRemotesAsync();
        remotes.Should().NotContain(r => r.Name == "to-remove");
    }

    [Fact]
    public async Task SetRemoteUrlAsync_UpdatesUrl()
    {
        InitBareRepo();
        await _gitService.InitializeAsync(_tempDir);
        await _gitService.AddRemoteAsync("origin", "https://old-url.com/repo.git");

        var result = await _gitService.SetRemoteUrlAsync("origin", "https://new-url.com/repo.git");

        result.Success.Should().BeTrue();
        var remotes = await _gitService.GetRemotesAsync();
        remotes.Should().Contain(r => r.Name == "origin" && r.Url == "https://new-url.com/repo.git");
    }

    // ── GetLog with Skip Tests ─────────────────────────────────────

    [Fact]
    public async Task GetLogAsync_WithSkip_ReturnsPaginated()
    {
        InitBareRepo();
        CreateAndCommitFile("a.txt", "a");
        CreateAndCommitFile("b.txt", "b");
        CreateAndCommitFile("c.txt", "c");

        await _gitService.InitializeAsync(_tempDir);
        var allCommits = await _gitService.GetLogAsync(limit: 100);
        var skippedCommits = await _gitService.GetLogAsync(limit: 100, skip: 1);

        skippedCommits.Should().HaveCount(allCommits.Count - 1);
    }
}
