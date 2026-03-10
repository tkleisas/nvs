namespace NVS.Core.Interfaces;

public interface IBuildService
{
    bool IsBuilding { get; }
    BuildTask? CurrentTask { get; }
    
    Task<BuildResult> RunTaskAsync(BuildTask task, CancellationToken cancellationToken = default);
    Task<BuildResult> RunDefaultTaskAsync(CancellationToken cancellationToken = default);
    Task CancelAsync();
    
    event EventHandler<BuildOutputEventArgs>? OutputReceived;
    event EventHandler<BuildResult>? BuildCompleted;
}

public sealed record BuildTask
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}

public sealed record BuildResult
{
    public required bool Success { get; init; }
    public required int ExitCode { get; init; }
    public required TimeSpan Duration { get; init; }
    public IReadOnlyList<BuildError> Errors { get; init; } = [];
    public IReadOnlyList<BuildWarning> Warnings { get; init; } = [];
}

public sealed record BuildError
{
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

public sealed record BuildWarning
{
    public required string Message { get; init; }
    public string? FilePath { get; init; }
    public int? Line { get; init; }
    public int? Column { get; init; }
}

public sealed class BuildOutputEventArgs : EventArgs
{
    public required string Output { get; init; }
    public required bool IsError { get; init; }
}
