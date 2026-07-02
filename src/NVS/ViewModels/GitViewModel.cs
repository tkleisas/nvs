using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.ViewModels;

/// <summary>
/// Source-control state and commands, owned by <see cref="MainViewModel"/> and
/// exposed to views as <c>Main.Git</c>.
/// </summary>
public sealed partial class GitViewModel : ObservableObject
{
    private readonly IGitService _gitService;
    private readonly MainViewModel _main;

    [ObservableProperty]
    private string _currentBranch = "";

    [ObservableProperty]
    private string _commitMessage = "";

    private Branch? _selectedBranch;
    private bool _isRefreshingBranches;

    public GitViewModel(IGitService gitService, MainViewModel main)
    {
        _gitService = gitService;
        _main = main;
        _gitService.StatusChanged += (_, _) => RefreshFiles();
    }

    public bool IsRepository => _gitService.IsRepository;
    public bool IsNotRepository => !_gitService.IsRepository;
    public bool HasStashes => Stashes.Count > 0;
    public bool HasTags => Tags.Count > 0;

    public ObservableCollection<GitFileStatus> ChangedFiles { get; } = [];
    public ObservableCollection<GitFileStatus> StagedFiles { get; } = [];
    public ObservableCollection<Branch> Branches { get; } = [];
    public ObservableCollection<StashEntry> Stashes { get; } = [];
    public ObservableCollection<Tag> Tags { get; } = [];
    public ObservableCollection<Commit> CommitLog { get; } = [];
    public ObservableCollection<Remote> Remotes { get; } = [];

    public Branch? SelectedBranch
    {
        get => _selectedBranch;
        set
        {
            if (_isRefreshingBranches) return;
            if (value is not null && value.Name != (_selectedBranch?.Name))
            {
                SetProperty(ref _selectedBranch, value);
                _ = CheckoutBranch(value);
                return;
            }
            SetProperty(ref _selectedBranch, value);
        }
    }

    public async Task InitializeAsync(string folderPath)
    {
        await _gitService.InitializeAsync(folderPath);
        CurrentBranch = _gitService.CurrentBranch ?? "";
        RefreshFiles();
        if (_gitService.IsRepository)
        {
            await RefreshBranchesAsync();
            await RefreshExtrasAsync();
        }
    }

    public void RefreshFiles()
    {
        ChangedFiles.Clear();
        StagedFiles.Clear();

        foreach (var file in _gitService.Status.Files)
        {
            if (file.IsStaged)
                StagedFiles.Add(file);
            else
                ChangedFiles.Add(file);
        }

        OnPropertyChanged(nameof(IsRepository));
        OnPropertyChanged(nameof(IsNotRepository));
        OnPropertyChanged(nameof(HasStashes));
        OnPropertyChanged(nameof(HasTags));
    }

    [RelayCommand]
    private async Task StageFile(GitFileStatus? file)
    {
        if (file is null) return;
        await _gitService.StageAsync(file.Path);
        _main.StatusMessage = $"Staged: {file.Path}";
    }

    [RelayCommand]
    private async Task UnstageFile(GitFileStatus? file)
    {
        if (file is null) return;
        await _gitService.UnstageAsync(file.Path);
        _main.StatusMessage = $"Unstaged: {file.Path}";
    }

    [RelayCommand]
    private async Task StageAll()
    {
        await _gitService.StageAllAsync();
        _main.StatusMessage = "Staged all changes";
    }

    [RelayCommand]
    private async Task Commit()
    {
        if (string.IsNullOrWhiteSpace(CommitMessage)) return;

        // Auto-stage all changes if nothing is staged (like VS Code)
        if (StagedFiles.Count == 0 && ChangedFiles.Count > 0)
        {
            await _gitService.StageAllAsync();
        }

        var result = await _gitService.CommitAsync(CommitMessage);
        if (result.Success)
        {
            _main.StatusMessage = $"Committed: {result.CommitHash?[..7]}";
            CommitMessage = "";
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            await RefreshExtrasAsync();
        }
        else
        {
            _main.StatusMessage = $"Commit failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task Refresh()
    {
        if (_main.WorkspacePath is not null)
        {
            await _gitService.InitializeAsync(_main.WorkspacePath);
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            await RefreshBranchesAsync();
            await RefreshExtrasAsync();
        }
    }

    [RelayCommand]
    private async Task InitRepository()
    {
        if (_main.WorkspacePath is null) return;

        // Auto-detect project type and create .gitignore before init
        var template = DetectGitignoreTemplate(_main.WorkspacePath);
        if (template is not null)
        {
            await _gitService.CreateGitignoreAsync(_main.WorkspacePath, template);
        }

        var result = await _gitService.InitRepositoryAsync(_main.WorkspacePath);
        if (result.Success)
        {
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            await RefreshBranchesAsync();
            _main.StatusMessage = template is not null
                ? $"Repository initialized with .gitignore ({template})"
                : "Repository initialized";
        }
        else
        {
            _main.StatusMessage = $"Init failed: {result.ErrorMessage}";
        }
    }

    private static string? DetectGitignoreTemplate(string path)
    {
        if (Directory.EnumerateFiles(path, "*.csproj", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.sln", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.slnx", SearchOption.AllDirectories).Any()
            || Directory.EnumerateFiles(path, "*.fsproj", SearchOption.AllDirectories).Any())
            return "dotnet";

        if (File.Exists(Path.Combine(path, "package.json")))
            return "node";

        if (File.Exists(Path.Combine(path, "requirements.txt"))
            || File.Exists(Path.Combine(path, "pyproject.toml"))
            || Directory.EnumerateFiles(path, "*.py", SearchOption.TopDirectoryOnly).Any())
            return "python";

        if (File.Exists(Path.Combine(path, "go.mod")))
            return "go";

        if (File.Exists(Path.Combine(path, "Cargo.toml")))
            return "rust";

        if (File.Exists(Path.Combine(path, "pom.xml"))
            || File.Exists(Path.Combine(path, "build.gradle")))
            return "java";

        return null;
    }

    [RelayCommand]
    private async Task CreateGitignore(string? template)
    {
        if (_main.WorkspacePath is null || template is null) return;

        var result = await _gitService.CreateGitignoreAsync(_main.WorkspacePath, template);
        _main.StatusMessage = result.Success ? ".gitignore created" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task CheckoutBranch(Branch? branch)
    {
        if (branch is null) return;
        try
        {
            await _gitService.CheckoutAsync(branch.Name);
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            await RefreshBranchesAsync();

            // Reload file tree and open documents to reflect new branch
            await _main.Explorer.ReloadAsync();
            await _main.ReloadOpenDocumentsFromDiskAsync();

            _main.StatusMessage = $"Switched to branch: {branch.Name}";
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Checkout failed: {ex.Message}";
        }
    }

    public async Task CreateBranchAsync(string branchName, bool includeChanges = true)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return;
        var name = branchName.Trim();

        if (!includeChanges)
        {
            // Stash changes before switching, then drop the stash
            var hasChanges = _gitService.Status.Files.Count > 0;
            if (hasChanges)
                await _gitService.StashSaveAsync("temp-branch-create");

            await _gitService.CreateBranchAsync(name);
            await _gitService.CheckoutAsync(name);

            if (hasChanges)
                await _gitService.StashDropAsync(0);
        }
        else
        {
            await _gitService.CreateBranchAsync(name);
            await _gitService.CheckoutAsync(name);
        }

        CurrentBranch = _gitService.CurrentBranch ?? "";
        RefreshFiles();
        await RefreshBranchesAsync();
        await RefreshExtrasAsync();
        _main.StatusMessage = $"Created and switched to: {name}";
    }

    public async Task DeleteBranchAsync(string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName)) return;
        var result = await _gitService.DeleteBranchAsync(branchName.Trim());
        if (result.Success)
        {
            await RefreshBranchesAsync();
            _main.StatusMessage = $"Deleted branch: {branchName}";
        }
        else
        {
            _main.StatusMessage = $"Delete failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task MergeBranch(Branch? branch)
    {
        if (branch is null) return;
        var result = await _gitService.MergeBranchAsync(branch.Name);
        if (result.Success)
        {
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            _main.StatusMessage = $"Merged {branch.Name} ({result.Status})";
        }
        else if (result.Status == Core.Interfaces.MergeStatus.Conflicts)
        {
            RefreshFiles();
            _main.StatusMessage = $"Merge conflicts in {result.ConflictedFiles.Count} file(s)";
        }
        else
        {
            _main.StatusMessage = $"Merge failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task StashSave()
    {
        var result = await _gitService.StashSaveAsync(includeUntracked: true);
        if (result.Success)
        {
            await RefreshExtrasAsync();
            _main.StatusMessage = "Changes stashed";
        }
        else
        {
            _main.StatusMessage = $"Stash failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task StashPop()
    {
        var result = await _gitService.StashPopAsync();
        if (result.Success)
        {
            await RefreshExtrasAsync();
            _main.StatusMessage = "Stash popped";
        }
        else
        {
            _main.StatusMessage = $"Pop failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task StashApply(StashEntry? entry)
    {
        var index = entry?.Index ?? 0;
        var result = await _gitService.StashApplyAsync(index);
        _main.StatusMessage = result.Success ? $"Stash @{{{index}}} applied" : $"Apply failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task StashDrop(StashEntry? entry)
    {
        var index = entry?.Index ?? 0;
        var result = await _gitService.StashDropAsync(index);
        if (result.Success) await RefreshExtrasAsync();
        _main.StatusMessage = result.Success ? $"Stash @{{{index}}} dropped" : $"Drop failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task CreateTag(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName)) return;
        var result = await _gitService.CreateTagAsync(tagName);
        if (result.Success)
        {
            await RefreshExtrasAsync();
            _main.StatusMessage = $"Created tag: {tagName}";
        }
        else
        {
            _main.StatusMessage = $"Tag failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task DeleteTag(Tag? tag)
    {
        if (tag is null) return;
        var result = await _gitService.DeleteTagAsync(tag.Name);
        if (result.Success)
        {
            await RefreshExtrasAsync();
            _main.StatusMessage = $"Deleted tag: {tag.Name}";
        }
        else
        {
            _main.StatusMessage = $"Delete tag failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task Push()
    {
        try
        {
            _main.StatusMessage = "Pushing...";
            await _gitService.PushAsync();
            _main.StatusMessage = "Push completed";
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Push failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Pull()
    {
        try
        {
            _main.StatusMessage = "Pulling...";
            await _gitService.PullAsync();
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            _main.StatusMessage = "Pull completed";
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Pull failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task Fetch()
    {
        try
        {
            _main.StatusMessage = "Fetching...";
            await _gitService.FetchAsync();
            _main.StatusMessage = "Fetch completed";
        }
        catch (Exception ex)
        {
            _main.StatusMessage = $"Fetch failed: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task CherryPick(Commit? commit)
    {
        if (commit is null) return;
        var result = await _gitService.CherryPickAsync(commit.Hash);
        if (result.Success)
        {
            RefreshFiles();
            _main.StatusMessage = $"Cherry-picked: {commit.Hash[..7]}";
        }
        else
        {
            _main.StatusMessage = $"Cherry-pick failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task AddRemote(string? args)
    {
        if (string.IsNullOrWhiteSpace(args)) return;
        var parts = args.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        var result = await _gitService.AddRemoteAsync(parts[0], parts[1]);
        if (result.Success) await RefreshExtrasAsync();
        _main.StatusMessage = result.Success ? $"Added remote: {parts[0]}" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task RemoveRemote(Remote? remote)
    {
        if (remote is null) return;
        var result = await _gitService.RemoveRemoteAsync(remote.Name);
        if (result.Success) await RefreshExtrasAsync();
        _main.StatusMessage = result.Success ? $"Removed remote: {remote.Name}" : $"Failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task LoadMoreHistory()
    {
        var moreCommits = await _gitService.GetLogAsync(limit: 50, skip: CommitLog.Count);
        foreach (var c in moreCommits)
            CommitLog.Add(c);
    }

    [RelayCommand]
    private async Task ViewDiff(GitFileStatus? file)
    {
        if (file is null) return;

        var hunks = file.IsStaged
            ? await _gitService.GetStagedDiffAsync(file.Path)
            : await _gitService.GetDiffAsync(file.Path);

        // Get old/new file content for full-file diff view
        string? oldContent;
        string? newContent;

        if (file.IsStaged)
        {
            // Staged: old = HEAD, new = index
            oldContent = await _gitService.GetFileContentFromHeadAsync(file.Path);
            newContent = await _gitService.GetFileContentFromIndexAsync(file.Path);
        }
        else
        {
            // Unstaged: old = index (or HEAD if not staged), new = working tree
            newContent = null;
            oldContent = await _gitService.GetFileContentFromIndexAsync(file.Path)
                         ?? await _gitService.GetFileContentFromHeadAsync(file.Path);

            var fullPath = Path.Combine(_main.WorkspacePath ?? "", file.Path);
            if (File.Exists(fullPath))
                newContent = await File.ReadAllTextAsync(fullPath);
        }

        var diffViewer = _main.OpenDiffDocument();
        if (diffViewer is null) return;
        diffViewer.LoadDiff(file.Path, hunks, file.IsStaged, oldContent, newContent);
        _main.DiffViewer = diffViewer;
        _main.StatusMessage = $"Diff: {file.Path} ({hunks.Count} hunks)";
    }

    [RelayCommand]
    private async Task Reset(string? modeStr)
    {
        var mode = modeStr?.ToLowerInvariant() switch
        {
            "soft" => ResetMode.Soft,
            "hard" => ResetMode.Hard,
            _ => ResetMode.Mixed,
        };
        var result = await _gitService.ResetAsync(mode);
        if (result.Success)
        {
            RefreshFiles();
            await RefreshExtrasAsync();
            _main.StatusMessage = $"Reset ({mode}) successful";
        }
        else
        {
            _main.StatusMessage = $"Reset failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task AmendCommit()
    {
        var message = string.IsNullOrWhiteSpace(CommitMessage) ? null : CommitMessage;
        var result = await _gitService.AmendCommitAsync(message);
        if (result.Success)
        {
            _main.StatusMessage = $"Amended: {result.CommitHash?[..7]}";
            CommitMessage = "";
            RefreshFiles();
            await RefreshExtrasAsync();
        }
        else
        {
            _main.StatusMessage = $"Amend failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task Rebase(string? ontoBranch)
    {
        if (string.IsNullOrWhiteSpace(ontoBranch)) return;
        var result = await _gitService.RebaseAsync(ontoBranch);
        if (result.Success)
        {
            CurrentBranch = _gitService.CurrentBranch ?? "";
            RefreshFiles();
            await RefreshExtrasAsync();
            _main.StatusMessage = $"Rebased onto {ontoBranch}";
        }
        else
        {
            _main.StatusMessage = $"Rebase failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task MarkResolved(GitFileStatus? file)
    {
        if (file is null) return;
        var result = await _gitService.MarkResolvedAsync(file.Path);
        _main.StatusMessage = result.Success
            ? $"Resolved: {file.Path}"
            : $"Mark resolved failed: {result.ErrorMessage}";
    }

    [RelayCommand]
    private async Task StageHunk((string path, int hunkIndex) args)
    {
        var result = await _gitService.StageHunkAsync(args.path, args.hunkIndex);
        if (result.Success)
        {
            RefreshFiles();
            _main.StatusMessage = $"Staged hunk {args.hunkIndex} of {args.path}";
        }
        else
        {
            _main.StatusMessage = $"Stage hunk failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task UnstageHunk((string path, int hunkIndex) args)
    {
        var result = await _gitService.UnstageHunkAsync(args.path, args.hunkIndex);
        if (result.Success)
        {
            RefreshFiles();
            _main.StatusMessage = $"Unstaged hunk {args.hunkIndex} of {args.path}";
        }
        else
        {
            _main.StatusMessage = $"Unstage hunk failed: {result.ErrorMessage}";
        }
    }

    [RelayCommand]
    private async Task OpenConflictResolver(GitFileStatus? file)
    {
        if (file is null || _main.WorkspacePath is null) return;

        var fullPath = Path.Combine(_main.WorkspacePath, file.Path);
        if (!File.Exists(fullPath)) return;

        var content = await File.ReadAllTextAsync(fullPath);
        _main.ConflictResolver?.LoadFile(fullPath, content);
        _main.StatusMessage = $"Conflict resolver: {file.Path}";
    }

    private async Task RefreshBranchesAsync()
    {
        _isRefreshingBranches = true;
        try
        {
            Branches.Clear();
            var branches = await _gitService.GetBranchesAsync();
            foreach (var b in branches)
                Branches.Add(b);

            _selectedBranch = Branches.FirstOrDefault(b => b.IsCurrent);
            OnPropertyChanged(nameof(SelectedBranch));
        }
        finally
        {
            _isRefreshingBranches = false;
        }
    }

    private async Task RefreshExtrasAsync()
    {
        Stashes.Clear();
        Tags.Clear();
        Remotes.Clear();
        CommitLog.Clear();

        var stashes = await _gitService.GetStashListAsync();
        foreach (var s in stashes) Stashes.Add(s);

        var tags = await _gitService.GetTagsAsync();
        foreach (var t in tags) Tags.Add(t);

        var remotes = await _gitService.GetRemotesAsync();
        foreach (var r in remotes) Remotes.Add(r);

        var commits = await _gitService.GetLogAsync(limit: 50);
        foreach (var c in commits) CommitLog.Add(c);

        OnPropertyChanged(nameof(HasStashes));
        OnPropertyChanged(nameof(HasTags));
    }
}
