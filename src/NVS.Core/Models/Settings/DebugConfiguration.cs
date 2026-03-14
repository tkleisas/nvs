namespace NVS.Core.Models.Settings;

public sealed record DebugConfiguration
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Request { get; init; }
    public string? Program { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? Console { get; init; }

    /// <summary>
    /// When set, connect to an already-running adapter via TCP on this port
    /// instead of launching a new adapter process.
    /// </summary>
    public int? ServerPort { get; init; }

    /// <summary>
    /// For attach mode: the PID of the already-running debuggee process.
    /// </summary>
    public int? ProcessId { get; init; }

    public IReadOnlyDictionary<string, object> AdditionalProperties { get; init; } = new Dictionary<string, object>();
}
