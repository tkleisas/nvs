using LibGit2Sharp;
using NVS.Core.Interfaces;
using Commit = NVS.Core.Interfaces.Commit;
using Branch = NVS.Core.Interfaces.Branch;
using RepositoryStatus = NVS.Core.Interfaces.RepositoryStatus;

namespace NVS.Services.Git;

public sealed class GitService : IGitService, IDisposable
{
    private Repository? _repo;
    private RepositoryStatus _cachedStatus = new();

    public bool IsRepository => _repo is not null;

    public string? CurrentBranch => _repo?.Head?.FriendlyName;

    public RepositoryStatus Status => _cachedStatus;

    public event EventHandler<RepositoryStatus>? StatusChanged;

    public Task InitializeAsync(string path, CancellationToken cancellationToken = default)
    {
        Dispose();

        var repoPath = Repository.Discover(path);
        if (repoPath is not null)
        {
            _repo = new Repository(repoPath);
            RefreshStatus();
        }

        return Task.CompletedTask;
    }

    public Task<CommitResult> CommitAsync(string message, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(new CommitResult { Success = false, ErrorMessage = "No repository" });

        try
        {
            var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
            if (signature is null)
            {
                return Task.FromResult(new CommitResult
                {
                    Success = false,
                    ErrorMessage = "Git user.name and user.email are not configured",
                });
            }

            var commit = _repo.Commit(message, signature, signature);
            RefreshStatus();
            return Task.FromResult(new CommitResult { Success = true, CommitHash = commit.Sha });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new CommitResult { Success = false, ErrorMessage = ex.Message });
        }
    }

    public Task StageAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        Commands.Stage(_repo, path);
        RefreshStatus();
        return Task.CompletedTask;
    }

    public Task StageAllAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        Commands.Stage(_repo, "*");
        RefreshStatus();
        return Task.CompletedTask;
    }

    public Task UnstageAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        Commands.Unstage(_repo, path);
        RefreshStatus();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Branch>> GetBranchesAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<Branch>>([]);

        var branches = _repo.Branches.Select(b =>
        {
            var ahead = 0;
            var behind = 0;
            if (b.IsTracking && b.TrackingDetails is not null)
            {
                ahead = b.TrackingDetails.AheadBy ?? 0;
                behind = b.TrackingDetails.BehindBy ?? 0;
            }

            return new Branch
            {
                Name = b.FriendlyName,
                IsCurrent = b.IsCurrentRepositoryHead,
                IsRemote = b.IsRemote,
                Upstream = b.TrackedBranch?.FriendlyName,
                AheadCount = ahead,
                BehindCount = behind,
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<Branch>>(branches);
    }

    public Task CheckoutAsync(string branchName, CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        var branch = _repo.Branches[branchName]
            ?? throw new InvalidOperationException($"Branch '{branchName}' not found");
        Commands.Checkout(_repo, branch);
        RefreshStatus();
        return Task.CompletedTask;
    }

    public Task CreateBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        _repo.CreateBranch(branchName);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Commit>> GetLogAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<Commit>>([]);

        var commits = _repo.Commits
            .Take(limit)
            .Select(c => new Commit
            {
                Hash = c.Sha,
                Message = c.MessageShort,
                Author = c.Author.Name,
                AuthorEmail = c.Author.Email,
                Date = c.Author.When,
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<Commit>>(commits);
    }

    public Task<IReadOnlyList<DiffHunk>> GetDiffAsync(string? path = null, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<DiffHunk>>([]);

        var diff = path is not null
            ? _repo.Diff.Compare<Patch>([path])
            : _repo.Diff.Compare<Patch>();

        var hunks = new List<DiffHunk>();
        foreach (var entry in diff)
        {
            hunks.AddRange(ParsePatch(entry.Patch));
        }

        return Task.FromResult<IReadOnlyList<DiffHunk>>(hunks);
    }

    internal static List<DiffHunk> ParsePatch(string patchText)
    {
        var hunks = new List<DiffHunk>();
        if (string.IsNullOrEmpty(patchText))
            return hunks;

        var patchLines = patchText.Split('\n');
        List<DiffLine>? currentLines = null;
        int oldStart = 0, oldCount = 0, newStart = 0, newCount = 0;
        int oldLine = 0, newLine = 0;

        foreach (var rawLine in patchLines)
        {
            if (rawLine.StartsWith("@@"))
            {
                // Flush previous hunk
                if (currentLines is not null)
                {
                    hunks.Add(new DiffHunk
                    {
                        OldStart = oldStart, OldCount = oldCount,
                        NewStart = newStart, NewCount = newCount,
                        Lines = currentLines,
                    });
                }

                // Parse @@ -oldStart,oldCount +newStart,newCount @@
                currentLines = [];
                var header = rawLine;
                var minus = header.IndexOf('-');
                var plus = header.IndexOf('+');
                var endAt = header.IndexOf("@@", 2, StringComparison.Ordinal);
                if (minus >= 0 && plus > minus && endAt > plus)
                {
                    var oldPart = header[(minus + 1)..plus].Trim().TrimEnd(',');
                    var newPart = header[(plus + 1)..endAt].Trim();

                    var oldParts = oldPart.Split(',');
                    var newParts = newPart.Split(',');

                    int.TryParse(oldParts[0], out oldStart);
                    oldCount = oldParts.Length > 1 && int.TryParse(oldParts[1], out var oc) ? oc : 1;
                    int.TryParse(newParts[0], out newStart);
                    newCount = newParts.Length > 1 && int.TryParse(newParts[1], out var nc) ? nc : 1;
                }

                oldLine = oldStart;
                newLine = newStart;
                continue;
            }

            if (currentLines is null)
                continue;

            if (rawLine.StartsWith('+'))
            {
                currentLines.Add(new DiffLine
                {
                    Type = DiffLineType.Addition,
                    Content = rawLine[1..],
                    OldLineNumber = -1,
                    NewLineNumber = newLine++,
                });
            }
            else if (rawLine.StartsWith('-'))
            {
                currentLines.Add(new DiffLine
                {
                    Type = DiffLineType.Deletion,
                    Content = rawLine[1..],
                    OldLineNumber = oldLine++,
                    NewLineNumber = -1,
                });
            }
            else if (rawLine.StartsWith(' '))
            {
                currentLines.Add(new DiffLine
                {
                    Type = DiffLineType.Context,
                    Content = rawLine[1..],
                    OldLineNumber = oldLine++,
                    NewLineNumber = newLine++,
                });
            }
        }

        // Flush last hunk
        if (currentLines is not null)
        {
            hunks.Add(new DiffHunk
            {
                OldStart = oldStart, OldCount = oldCount,
                NewStart = newStart, NewCount = newCount,
                Lines = currentLines,
            });
        }

        return hunks;
    }

    public Task PullAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
        if (signature is null) return Task.CompletedTask;

        Commands.Pull(_repo, signature, new PullOptions());
        RefreshStatus();
        return Task.CompletedTask;
    }

    public Task PushAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        var remote = _repo.Network.Remotes["origin"];
        if (remote is null || _repo.Head?.TrackedBranch is null) return Task.CompletedTask;

        _repo.Network.Push(_repo.Head, new PushOptions());
        return Task.CompletedTask;
    }

    public Task FetchAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null) return Task.CompletedTask;

        foreach (var remote in _repo.Network.Remotes)
        {
            var refSpecs = remote.FetchRefSpecs.Select(rs => rs.Specification).ToList();
            Commands.Fetch(_repo, remote.Name, refSpecs, new FetchOptions(), null);
        }

        return Task.CompletedTask;
    }

    private void RefreshStatus()
    {
        if (_repo is null) return;

        var status = _repo.RetrieveStatus(new StatusOptions());
        var files = new List<GitFileStatus>();

        foreach (var entry in status)
        {
            if (entry.State == LibGit2Sharp.FileStatus.Ignored)
                continue;

            var (fileStatus, isStaged) = MapFileStatus(entry.State);
            files.Add(new GitFileStatus
            {
                Path = entry.FilePath,
                Status = fileStatus,
                IsStaged = isStaged,
            });
        }

        _cachedStatus = new RepositoryStatus
        {
            Files = files,
            HasStagedChanges = files.Any(f => f.IsStaged),
            HasUnstagedChanges = files.Any(f => !f.IsStaged),
            AheadCount = _repo.Head?.TrackingDetails?.AheadBy ?? 0,
            BehindCount = _repo.Head?.TrackingDetails?.BehindBy ?? 0,
        };

        StatusChanged?.Invoke(this, _cachedStatus);
    }

    private static (Core.Interfaces.FileStatus status, bool isStaged) MapFileStatus(
        LibGit2Sharp.FileStatus state)
    {
        // Staged states
        if (state.HasFlag(LibGit2Sharp.FileStatus.NewInIndex))
            return (Core.Interfaces.FileStatus.Added, true);
        if (state.HasFlag(LibGit2Sharp.FileStatus.ModifiedInIndex))
            return (Core.Interfaces.FileStatus.Modified, true);
        if (state.HasFlag(LibGit2Sharp.FileStatus.DeletedFromIndex))
            return (Core.Interfaces.FileStatus.Deleted, true);
        if (state.HasFlag(LibGit2Sharp.FileStatus.RenamedInIndex))
            return (Core.Interfaces.FileStatus.Renamed, true);

        // Unstaged states
        if (state.HasFlag(LibGit2Sharp.FileStatus.NewInWorkdir))
            return (Core.Interfaces.FileStatus.Untracked, false);
        if (state.HasFlag(LibGit2Sharp.FileStatus.ModifiedInWorkdir))
            return (Core.Interfaces.FileStatus.Modified, false);
        if (state.HasFlag(LibGit2Sharp.FileStatus.DeletedFromWorkdir))
            return (Core.Interfaces.FileStatus.Deleted, false);
        if (state.HasFlag(LibGit2Sharp.FileStatus.RenamedInWorkdir))
            return (Core.Interfaces.FileStatus.Renamed, false);
        if (state.HasFlag(LibGit2Sharp.FileStatus.Conflicted))
            return (Core.Interfaces.FileStatus.Conflicted, false);

        return (Core.Interfaces.FileStatus.Unmodified, false);
    }

    public void Dispose()
    {
        _repo?.Dispose();
        _repo = null;
    }
}
