namespace NVS.Core.Interfaces;

public interface IGitService
{
    bool IsRepository { get; }
    string? CurrentBranch { get; }
    RepositoryStatus Status { get; }
    
    Task InitializeAsync(string path, CancellationToken cancellationToken = default);
    
    Task<CommitResult> CommitAsync(string message, CancellationToken cancellationToken = default);
    Task StageAsync(string path, CancellationToken cancellationToken = default);
    Task StageAllAsync(CancellationToken cancellationToken = default);
    Task UnstageAsync(string path, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Branch>> GetBranchesAsync(CancellationToken cancellationToken = default);
    Task CheckoutAsync(string branchName, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(string branchName, CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<Commit>> GetLogAsync(int limit = 100, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DiffHunk>> GetDiffAsync(string? path = null, CancellationToken cancellationToken = default);
    
    Task PullAsync(CancellationToken cancellationToken = default);
    Task PushAsync(CancellationToken cancellationToken = default);
    Task FetchAsync(CancellationToken cancellationToken = default);
    
    event EventHandler<RepositoryStatus>? StatusChanged;
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
