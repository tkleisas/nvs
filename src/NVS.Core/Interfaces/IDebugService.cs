using NVS.Core.Models;
using NVS.Core.Models.Settings;

namespace NVS.Core.Interfaces;

public interface IDebugService
{
    bool IsDebugging { get; }
    bool IsPaused { get; }
    DebugSession? CurrentSession { get; }

    /// <summary>
    /// Callback the IDE sets to handle DAP "runInTerminal" reverse requests.
    /// The adapter calls this when console is set to "integratedTerminal".
    /// Returns the launched process ID (or 0 if unknown).
    /// </summary>
    Func<RunInTerminalRequest, Task<int>>? RunInTerminalHandler { get; set; }

    /// <summary>
    /// Resolves the full path to the debug adapter executable, auto-downloading if needed.
    /// </summary>
    Task<string> ResolveAdapterPathAsync(string adapterType, CancellationToken cancellationToken = default);
    
    Task<DebugSession> StartDebuggingAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default);
    Task StopDebuggingAsync(CancellationToken cancellationToken = default);
    Task PauseAsync(CancellationToken cancellationToken = default);
    Task ContinueAsync(CancellationToken cancellationToken = default);
    Task StepOverAsync(CancellationToken cancellationToken = default);
    Task StepIntoAsync(CancellationToken cancellationToken = default);
    Task StepOutAsync(CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StackFrame>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default);
    int ActiveThreadId { get; }
    int ActiveFrameId { get; }
    Task<IReadOnlyList<Variable>> GetVariablesAsync(int frameId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Variable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);
    Task<string?> EvaluateAsync(string expression, int? frameId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Breakpoint>> SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default);
    Task ResyncBreakpointsAsync(CancellationToken cancellationToken = default);
    
    event EventHandler<DebugSession>? DebuggingStarted;
    event EventHandler<DebugSession>? DebuggingStopped;
    event EventHandler? DebuggingPaused;
    event EventHandler? DebuggingContinued;
    event EventHandler<ThreadInfo>? ThreadStarted;
    event EventHandler<ThreadInfo>? ThreadExited;
    event EventHandler<Breakpoint>? BreakpointHit;
    event EventHandler<OutputEvent>? OutputReceived;
}

public sealed record DebugSession
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Type { get; init; }
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record ThreadInfo
{
    public required int Id { get; init; }
    public required string Name { get; init; }
}

public sealed record StackFrame
{
    public required int Id { get; init; }
    public required string Name { get; init; }
    public string? Source { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
}

public sealed record Variable
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Type { get; init; }
    public int? VariablesReference { get; init; }
}

public sealed record Breakpoint
{
    public required Guid Id { get; init; }
    public required string Path { get; init; }
    public required int Line { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsVerified { get; init; }
    public string? Condition { get; init; }
    public int? HitCount { get; init; }
}

public sealed record OutputEvent
{
    public required string Output { get; init; }
    public required OutputCategory Category { get; init; }
}

public enum OutputCategory
{
    Console,
    Stdout,
    Stderr,
    Telemetry
}

public sealed record RunInTerminalRequest
{
    public required string Cwd { get; init; }
    public required IReadOnlyList<string> Args { get; init; }
    public IReadOnlyDictionary<string, string?>? Env { get; init; }
    public string? Title { get; init; }
}
