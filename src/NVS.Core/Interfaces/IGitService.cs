namespace NVS.Core.Interfaces;

public interface IGitService
{
    bool IsRepository { get; }
    string? CurrentBranch { get; }
    RepositoryStatus Status { get; }

    Task InitializeAsync(string path, CancellationToken cancellationToken = default);

    // Staging & committing
    Task<CommitResult> CommitAsync(string message, CancellationToken cancellationToken = default);
    Task StageAsync(string path, CancellationToken cancellationToken = default);
    Task StageAllAsync(CancellationToken cancellationToken = default);
    Task UnstageAsync(string path, CancellationToken cancellationToken = default);

    // Repository initialization
    Task<GitOperationResult> InitRepositoryAsync(string path, CancellationToken cancellationToken = default);
    Task<GitOperationResult> CreateGitignoreAsync(string repoPath, string template, CancellationToken cancellationToken = default);
    IReadOnlyList<string> GetGitignoreTemplates();

    // Branches
    Task<IReadOnlyList<Branch>> GetBranchesAsync(CancellationToken cancellationToken = default);
    Task CheckoutAsync(string branchName, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(string branchName, CancellationToken cancellationToken = default);
    Task<GitOperationResult> DeleteBranchAsync(string branchName, CancellationToken cancellationToken = default);
    Task<GitOperationResult> RenameBranchAsync(string oldName, string newName, CancellationToken cancellationToken = default);
    Task<MergeResult> MergeBranchAsync(string branchName, CancellationToken cancellationToken = default);

    // Stash
    Task<GitOperationResult> StashSaveAsync(string? message = null, bool includeUntracked = false, CancellationToken cancellationToken = default);
    Task<GitOperationResult> StashPopAsync(int index = 0, CancellationToken cancellationToken = default);
    Task<GitOperationResult> StashApplyAsync(int index = 0, CancellationToken cancellationToken = default);
    Task<GitOperationResult> StashDropAsync(int index = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StashEntry>> GetStashListAsync(CancellationToken cancellationToken = default);

    // Tags
    Task<GitOperationResult> CreateTagAsync(string name, string? message = null, string? target = null, CancellationToken cancellationToken = default);
    Task<GitOperationResult> DeleteTagAsync(string name, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken cancellationToken = default);

    // History & diff
    Task<IReadOnlyList<Commit>> GetLogAsync(int limit = 100, int skip = 0, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiffHunk>> GetDiffAsync(string? path = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiffHunk>> GetStagedDiffAsync(string? path = null, CancellationToken cancellationToken = default);
    Task<string?> GetFileContentFromHeadAsync(string path, CancellationToken cancellationToken = default);
    Task<string?> GetFileContentFromIndexAsync(string path, CancellationToken cancellationToken = default);
    Task<GitOperationResult> CherryPickAsync(string commitSha, CancellationToken cancellationToken = default);

    // Reset, amend, rebase
    Task<GitOperationResult> ResetAsync(ResetMode mode, int commitCount = 1, CancellationToken cancellationToken = default);
    Task<CommitResult> AmendCommitAsync(string? newMessage = null, CancellationToken cancellationToken = default);
    Task<GitOperationResult> RebaseAsync(string ontoBranch, CancellationToken cancellationToken = default);

    // Conflict resolution
    Task<GitOperationResult> MarkResolvedAsync(string path, CancellationToken cancellationToken = default);

    // Partial (hunk) staging
    Task<GitOperationResult> StageHunkAsync(string path, int hunkIndex, CancellationToken cancellationToken = default);
    Task<GitOperationResult> UnstageHunkAsync(string path, int hunkIndex, CancellationToken cancellationToken = default);

    // Remotes
    Task PullAsync(CancellationToken cancellationToken = default);
    Task PushAsync(CancellationToken cancellationToken = default);
    Task FetchAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Remote>> GetRemotesAsync(CancellationToken cancellationToken = default);
    Task<GitOperationResult> AddRemoteAsync(string name, string url, CancellationToken cancellationToken = default);
    Task<GitOperationResult> RemoveRemoteAsync(string name, CancellationToken cancellationToken = default);
    Task<GitOperationResult> SetRemoteUrlAsync(string name, string url, CancellationToken cancellationToken = default);

    event EventHandler<RepositoryStatus>? StatusChanged;
}

public sealed record GitOperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }

    public static GitOperationResult Ok() => new() { Success = true };
    public static GitOperationResult Fail(string message) => new() { Success = false, ErrorMessage = message };
}

public sealed record MergeResult
{
    public bool Success { get; init; }
    public MergeStatus Status { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<string> ConflictedFiles { get; init; } = [];
}

public enum MergeStatus
{
    FastForward,
    NonFastForward,
    UpToDate,
    Conflicts
}

public sealed record RepositoryStatus
{
    public bool HasUnstagedChanges { get; init; }
    public bool HasStagedChanges { get; init; }
    public IReadOnlyList<GitFileStatus> Files { get; init; } = [];
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
}

public sealed record GitFileStatus
{
    public required string Path { get; init; }
    public required FileStatus Status { get; init; }
    public bool IsStaged { get; init; }
}

public enum FileStatus
{
    Unmodified,
    Added,
    Modified,
    Deleted,
    Renamed,
    Copied,
    Untracked,
    Ignored,
    Conflicted
}

public sealed record CommitResult
{
    public bool Success { get; init; }
    public string? CommitHash { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record Branch
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsRemote { get; init; }
    public string? Upstream { get; init; }
    public int AheadCount { get; init; }
    public int BehindCount { get; init; }
}

public sealed record Commit
{
    public required string Hash { get; init; }
    public required string Message { get; init; }
    public required string Author { get; init; }
    public required string AuthorEmail { get; init; }
    public DateTimeOffset Date { get; init; }
}

public sealed record StashEntry
{
    public required int Index { get; init; }
    public required string Message { get; init; }
    public DateTimeOffset Date { get; init; }
}

public sealed record Tag
{
    public required string Name { get; init; }
    public required string TargetHash { get; init; }
    public string? Message { get; init; }
    public string? TaggerName { get; init; }
    public DateTimeOffset? Date { get; init; }
    public bool IsAnnotated { get; init; }
}

public sealed record Remote
{
    public required string Name { get; init; }
    public required string Url { get; init; }
    public string? PushUrl { get; init; }
}

public sealed record DiffHunk
{
    public required int OldStart { get; init; }
    public required int OldCount { get; init; }
    public required int NewStart { get; init; }
    public required int NewCount { get; init; }
    public required IReadOnlyList<DiffLine> Lines { get; init; }
}

public sealed record DiffLine
{
    public required DiffLineType Type { get; init; }
    public required string Content { get; init; }
    public int OldLineNumber { get; init; }
    public int NewLineNumber { get; init; }
}

public enum DiffLineType
{
    Context,
    Addition,
    Deletion
}

public enum ResetMode
{
    Soft,
    Mixed,
    Hard
}

public sealed record ConflictBlock
{
    public required int StartLine { get; init; }
    public required int EndLine { get; init; }
    public required IReadOnlyList<string> OursLines { get; init; }
    public required IReadOnlyList<string> TheirsLines { get; init; }
    public string? OursLabel { get; init; }
    public string? TheirsLabel { get; init; }
}
