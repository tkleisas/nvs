using LibGit2Sharp;
using NVS.Core.Interfaces;
using Commit = NVS.Core.Interfaces.Commit;
using Branch = NVS.Core.Interfaces.Branch;
using Tag = NVS.Core.Interfaces.Tag;
using Remote = NVS.Core.Interfaces.Remote;
using RepositoryStatus = NVS.Core.Interfaces.RepositoryStatus;
using MergeResult = NVS.Core.Interfaces.MergeResult;

namespace NVS.Services.Git;

public sealed class GitService : IGitService, IDisposable
{
    private Repository? _repo;
    private RepositoryStatus _cachedStatus = new();

    private static readonly Dictionary<string, string> GitignoreTemplates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dotnet"] = """
            ## .NET / C#
            bin/
            obj/
            *.user
            *.suo
            *.userosscache
            *.sln.docstates
            .vs/
            [Dd]ebug/
            [Rr]elease/
            x64/
            x86/
            build/
            [Bb]in/
            [Oo]bj/
            TestResults/
            *.nupkg
            *.snupkg
            project.lock.json
            project.fragment.lock.json
            artifacts/
            """,
        ["node"] = """
            node_modules/
            npm-debug.log*
            yarn-debug.log*
            yarn-error.log*
            .npm
            .eslintcache
            dist/
            build/
            coverage/
            .env
            .env.local
            .env.*.local
            """,
        ["python"] = """
            __pycache__/
            *.py[cod]
            *$py.class
            *.so
            .Python
            env/
            venv/
            .venv/
            build/
            dist/
            *.egg-info/
            .eggs/
            *.egg
            .mypy_cache/
            .pytest_cache/
            .coverage
            htmlcov/
            """,
        ["java"] = """
            *.class
            *.jar
            *.war
            *.ear
            *.nar
            target/
            .gradle/
            build/
            out/
            .idea/
            *.iml
            .settings/
            .project
            .classpath
            """,
        ["go"] = """
            *.exe
            *.exe~
            *.dll
            *.so
            *.dylib
            *.test
            *.out
            vendor/
            go.work
            """,
        ["rust"] = """
            /target
            **/*.rs.bk
            Cargo.lock
            """,
    };

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

    // ── Repository Initialization ──────────────────────────────────

    public Task<GitOperationResult> InitRepositoryAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            Repository.Init(path);
            Dispose();
            _repo = new Repository(path);
            RefreshStatus();
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> CreateGitignoreAsync(string repoPath, string template, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!GitignoreTemplates.TryGetValue(template, out var content))
                return Task.FromResult(GitOperationResult.Fail($"Unknown template: {template}"));

            // Dedent the raw string literal content
            var lines = content.Split('\n').Select(l => l.TrimStart()).Where(l => l.Length > 0);
            var gitignorePath = System.IO.Path.Combine(repoPath, ".gitignore");
            File.WriteAllText(gitignorePath, string.Join(Environment.NewLine, lines) + Environment.NewLine);

            RefreshStatus();
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public IReadOnlyList<string> GetGitignoreTemplates() => [.. GitignoreTemplates.Keys];

    // ── Staging & Committing ───────────────────────────────────────

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

    // ── Branches ───────────────────────────────────────────────────

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

    public Task<GitOperationResult> DeleteBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            var branch = _repo.Branches[branchName];
            if (branch is null)
                return Task.FromResult(GitOperationResult.Fail($"Branch '{branchName}' not found"));
            if (branch.IsCurrentRepositoryHead)
                return Task.FromResult(GitOperationResult.Fail("Cannot delete the current branch"));

            _repo.Branches.Remove(branch);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> RenameBranchAsync(string oldName, string newName, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            var branch = _repo.Branches[oldName];
            if (branch is null)
                return Task.FromResult(GitOperationResult.Fail($"Branch '{oldName}' not found"));

            _repo.Branches.Rename(oldName, newName);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<MergeResult> MergeBranchAsync(string branchName, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(new MergeResult { Success = false, ErrorMessage = "No repository" });

        try
        {
            var branch = _repo.Branches[branchName];
            if (branch is null)
                return Task.FromResult(new MergeResult { Success = false, ErrorMessage = $"Branch '{branchName}' not found" });

            var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
            if (signature is null)
                return Task.FromResult(new MergeResult { Success = false, ErrorMessage = "Git user.name and user.email are not configured" });

            var mergeResult = _repo.Merge(branch, signature, new MergeOptions());
            RefreshStatus();

            var status = mergeResult.Status switch
            {
                LibGit2Sharp.MergeStatus.FastForward => Core.Interfaces.MergeStatus.FastForward,
                LibGit2Sharp.MergeStatus.NonFastForward => Core.Interfaces.MergeStatus.NonFastForward,
                LibGit2Sharp.MergeStatus.UpToDate => Core.Interfaces.MergeStatus.UpToDate,
                LibGit2Sharp.MergeStatus.Conflicts => Core.Interfaces.MergeStatus.Conflicts,
                _ => Core.Interfaces.MergeStatus.NonFastForward,
            };

            var conflictedFiles = status == Core.Interfaces.MergeStatus.Conflicts
                ? _repo.Index.Conflicts.Select(c => c.Ancestor?.Path ?? c.Ours?.Path ?? c.Theirs?.Path ?? "").Where(p => p.Length > 0).ToList()
                : [];

            return Task.FromResult(new MergeResult
            {
                Success = status != Core.Interfaces.MergeStatus.Conflicts,
                Status = status,
                ConflictedFiles = conflictedFiles,
            });
        }
        catch (Exception ex)
        {
            return Task.FromResult(new MergeResult { Success = false, ErrorMessage = ex.Message });
        }
    }

    // ── Stash ──────────────────────────────────────────────────────

    public Task<GitOperationResult> StashSaveAsync(string? message = null, bool includeUntracked = false, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
            if (signature is null)
                return Task.FromResult(GitOperationResult.Fail("Git user.name and user.email are not configured"));

            var options = includeUntracked ? StashModifiers.IncludeUntracked : StashModifiers.Default;
            var stash = _repo.Stashes.Add(signature, message ?? "WIP on " + (CurrentBranch ?? "HEAD"), options);
            if (stash is null)
                return Task.FromResult(GitOperationResult.Fail("Nothing to stash"));

            RefreshStatus();
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> StashPopAsync(int index = 0, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            if (_repo.Stashes.Count() <= index)
                return Task.FromResult(GitOperationResult.Fail($"Stash @{{{index}}} not found"));

            var result = _repo.Stashes.Apply(index);
            if (result == StashApplyStatus.Applied)
            {
                _repo.Stashes.Remove(index);
                RefreshStatus();
                return Task.FromResult(GitOperationResult.Ok());
            }

            return Task.FromResult(GitOperationResult.Fail($"Stash apply returned: {result}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> StashApplyAsync(int index = 0, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            if (_repo.Stashes.Count() <= index)
                return Task.FromResult(GitOperationResult.Fail($"Stash @{{{index}}} not found"));

            var result = _repo.Stashes.Apply(index);
            RefreshStatus();

            return result == StashApplyStatus.Applied
                ? Task.FromResult(GitOperationResult.Ok())
                : Task.FromResult(GitOperationResult.Fail($"Stash apply returned: {result}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> StashDropAsync(int index = 0, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            if (_repo.Stashes.Count() <= index)
                return Task.FromResult(GitOperationResult.Fail($"Stash @{{{index}}} not found"));

            _repo.Stashes.Remove(index);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<IReadOnlyList<StashEntry>> GetStashListAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<StashEntry>>([]);

        var entries = _repo.Stashes.Select((s, i) => new StashEntry
        {
            Index = i,
            Message = s.Message,
            Date = s.WorkTree.Author.When,
        }).ToList();

        return Task.FromResult<IReadOnlyList<StashEntry>>(entries);
    }

    // ── Tags ───────────────────────────────────────────────────────

    public Task<GitOperationResult> CreateTagAsync(string name, string? message = null, string? target = null, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            var targetObj = target is not null
                ? _repo.Lookup(target) ?? throw new InvalidOperationException($"Target '{target}' not found")
                : _repo.Head.Tip;

            if (message is not null)
            {
                var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
                if (signature is null)
                    return Task.FromResult(GitOperationResult.Fail("Git user.name and user.email are not configured"));

                _repo.Tags.Add(name, targetObj, signature, message);
            }
            else
            {
                _repo.Tags.Add(name, targetObj);
            }

            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> DeleteTagAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            _repo.Tags.Remove(name);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<Tag>>([]);

        var tags = _repo.Tags.Select(t =>
        {
            var isAnnotated = t.IsAnnotated;
            string? message = null;
            string? taggerName = null;
            DateTimeOffset? date = null;

            if (isAnnotated && t.Annotation is not null)
            {
                message = t.Annotation.Message;
                taggerName = t.Annotation.Tagger?.Name;
                date = t.Annotation.Tagger?.When;
            }

            return new Tag
            {
                Name = t.FriendlyName,
                TargetHash = t.Target.Sha,
                IsAnnotated = isAnnotated,
                Message = message,
                TaggerName = taggerName,
                Date = date,
            };
        }).ToList();

        return Task.FromResult<IReadOnlyList<Tag>>(tags);
    }

    // ── History & Diff ─────────────────────────────────────────────

    public Task<IReadOnlyList<Commit>> GetLogAsync(int limit = 100, int skip = 0, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<Commit>>([]);

        var commits = _repo.Commits
            .Skip(skip)
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

    public Task<GitOperationResult> CherryPickAsync(string commitSha, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            var commit = _repo.Lookup<LibGit2Sharp.Commit>(new ObjectId(commitSha));
            if (commit is null)
                return Task.FromResult(GitOperationResult.Fail($"Commit '{commitSha}' not found"));

            var signature = _repo.Config.BuildSignature(DateTimeOffset.Now);
            if (signature is null)
                return Task.FromResult(GitOperationResult.Fail("Git user.name and user.email are not configured"));

            var result = _repo.CherryPick(commit, signature);
            RefreshStatus();

            return result.Status == CherryPickStatus.CherryPicked
                ? Task.FromResult(GitOperationResult.Ok())
                : Task.FromResult(GitOperationResult.Fail($"Cherry-pick resulted in conflicts"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    // ── Remotes ────────────────────────────────────────────────────

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

    public Task<IReadOnlyList<Remote>> GetRemotesAsync(CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult<IReadOnlyList<Remote>>([]);

        var remotes = _repo.Network.Remotes.Select(r => new Remote
        {
            Name = r.Name,
            Url = r.Url,
            PushUrl = r.PushUrl,
        }).ToList();

        return Task.FromResult<IReadOnlyList<Remote>>(remotes);
    }

    public Task<GitOperationResult> AddRemoteAsync(string name, string url, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            _repo.Network.Remotes.Add(name, url);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> RemoveRemoteAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            _repo.Network.Remotes.Remove(name);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    public Task<GitOperationResult> SetRemoteUrlAsync(string name, string url, CancellationToken cancellationToken = default)
    {
        if (_repo is null)
            return Task.FromResult(GitOperationResult.Fail("No repository"));

        try
        {
            _repo.Network.Remotes.Update(name, r => r.Url = url);
            return Task.FromResult(GitOperationResult.Ok());
        }
        catch (Exception ex)
        {
            return Task.FromResult(GitOperationResult.Fail(ex.Message));
        }
    }

    // ── Patch Parsing ──────────────────────────────────────────────

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

    // ── Internal Helpers ───────────────────────────────────────────

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
