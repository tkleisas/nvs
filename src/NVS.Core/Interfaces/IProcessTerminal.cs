namespace NVS.Core.Interfaces;

/// <summary>
/// One terminal = one underlying process spawned through a cross-platform PTY
/// (or, when <see cref="TerminalStartOptions.AllocatePty"/> is false, plain
/// redirected pipes). Multiple consumers can subscribe to the same output via
/// <see cref="OutputReceived"/> (events) or <see cref="OutputObservable"/> (Rx).
/// </summary>
public interface IProcessTerminal : IAsyncDisposable
{
    /// <summary>Identity / metadata for this terminal session.</summary>
    TerminalSession Session { get; }

    /// <summary>True while the child process is still running.</summary>
    bool IsRunning { get; }

    /// <summary>OS process id of the spawned child, or null before/after start.</summary>
    int? ProcessId { get; }

    /// <summary>Exit code of the child, available after it exits; null while running.</summary>
    int? ExitCode { get; }

    /// <summary>
    /// Core observable output surface — raised for every stdout/stderr chunk read from
    /// the PTY. Multiple subscribers are supported. Subscribers are responsible for
    /// marshalling to the UI thread themselves.
    /// </summary>
    event EventHandler<TerminalOutputChunk>? OutputReceived;

    /// <summary>Raised once when the child process exits; payload is the exit code.</summary>
    event EventHandler<int>? Exited;

    /// <summary>
    /// Rx-style projection over <see cref="OutputReceived"/> — the same chunks, composable
    /// (filter/throttle/scan). Useful for watchers (URL scraping) and LLM tools.
    /// </summary>
    IObservable<TerminalOutputChunk> OutputObservable { get; }

    /// <summary>Spawns the child process per <paramref name="options"/>. Must be called once.</summary>
    Task StartAsync(TerminalStartOptions options, CancellationToken cancellationToken = default);

    /// <summary>Sends raw text (typically keystrokes / a command line) to the PTY stdin.</summary>
    Task SendInputAsync(string text);

    /// <summary>Resizes the PTY to the given dimensions (cols x rows).</summary>
    Task ResizeAsync(int cols, int rows);

    /// <summary>Kills the entire child process tree synchronously and waits for exit.</summary>
    Task KillAsync();
}

/// <summary>
/// Manages the live set of <see cref="IProcessTerminal"/> sessions in the IDE —
/// the single source of truth for terminal lifecycle. Replaces the old
/// <c>ITerminalService</c>.
/// </summary>
public interface ITerminalHost
{
    /// <summary>All currently-tracked terminals (open + closed-but-not-yet-disposed).</summary>
    IReadOnlyList<IProcessTerminal> Terminals { get; }

    /// <summary>The terminal the UI considers active (e.g. the visible tab).</summary>
    IProcessTerminal? Active { get; set; }

    /// <summary>Creates a NEW terminal session without starting it. Caller starts and drives it.</summary>
    IProcessTerminal CreateTerminal(TerminalStartOptions options);

    /// <summary>Convenience: creates AND starts a terminal running the user's default shell.</summary>
    Task<IProcessTerminal> CreateShellAsync(TerminalStartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience: creates AND starts a terminal that runs the given <paramref name="command"/>
    /// as a one-shot process (not a shell). Used by the run/debug flow to <c>dotnet run …</c>
    /// into a child owned by the IDE so Stop can reliably kill its tree.
    /// </summary>
    Task<IProcessTerminal> RunCommandAsync(
        string command,
        IReadOnlyList<string>? args = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TerminalSessionKind kind = TerminalSessionKind.Run,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes (and kills if running) the given terminal. Returns true when it was found and
    /// removed from the host. Disposes the terminal.
    /// </summary>
    bool CloseTerminal(IProcessTerminal terminal);

    /// <summary>Looks up a terminal by its <see cref="TerminalSession.Id"/>.</summary>
    IProcessTerminal? FindById(Guid sessionId);

    /// <summary>Kills and closes every currently-tracked terminal (e.g. on app shutdown).</summary>
    Task CloseAllAsync();

    event EventHandler<IProcessTerminal>? TerminalCreated;
    event EventHandler<IProcessTerminal>? TerminalClosed;
    event EventHandler<IProcessTerminal>? ActiveTerminalChanged;
}

/// <summary>Metadata describing one terminal session.</summary>
public sealed record TerminalSession
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Title { get; init; }
    public required TerminalSessionKind Kind { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>How a terminal is used — drives default titles and behaviour.</summary>
public enum TerminalSessionKind
{
    /// <summary>An interactive user shell (the dock terminal panel).</summary>
    Shell,

    /// <summary>A child process launched by Run-without-debugging (e.g. <c>dotnet run</c>).</summary>
    Run,

    /// <summary>A child process launched/attached by the debug flow.</summary>
    Debug,
}

/// <summary>Options for starting a terminal session via <see cref="IProcessTerminal.StartAsync"/>.</summary>
public sealed record TerminalStartOptions
{
    /// <summary>Optional friendly title (defaults derived from <see cref="Kind"/>).</summary>
    public string? Title { get; init; }

    /// <summary>How this terminal is used — Shell / Run / Debug.</summary>
    public TerminalSessionKind Kind { get; init; } = TerminalSessionKind.Shell;

    /// <summary>
    /// Executable to launch. When null (and <see cref="Kind"/> is Shell), the host's
    /// default user shell (pwsh / powershell / cmd on Windows; <c>$SHELL</c> on Unix) is used.
    /// </summary>
    public string? Command { get; init; }

    /// <summary>Arguments passed to <see cref="Command"/>. Ignored when <see cref="Command"/> is null.</summary>
    public IReadOnlyList<string> Args { get; init; } = [];

    /// <summary>Initial working directory. Defaults to the workspace root.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Extra environment variables applied on top of the inherited process environment.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();

    /// <summary>True (default) → allocate a real PTY. False → use plain redirected pipes
    /// (suppression of VT sequences; suitable for capture-only scenarios like <c>dotnet build</c>).</summary>
    public bool AllocatePty { get; init; } = true;

    /// <summary>Initial PTY column count.</summary>
    public int Cols { get; init; } = 120;

    /// <summary>Initial PTY row count.</summary>
    public int Rows { get; init; } = 30;
}

/// <summary>One chunk of output read from a terminal's PTY.</summary>
public sealed class TerminalOutputChunk : EventArgs
{
    /// <summary>The raw text (already-decoded UTF-8 from the PTY byte stream).</summary>
    public required string Text { get; init; }

    /// <summary>
    /// True when the chunk came from the child's stderr. NOTE: real PTYs merge stdout
    /// and stderr into one stream, so this is only meaningful when the terminal was
    /// started with <see cref="TerminalStartOptions.AllocatePty"/> = false; with a PTY
    /// this is always false.
    /// </summary>
    public bool IsError { get; init; }

    /// <summary>Id of the owning <see cref="TerminalSession"/>.</summary>
    public Guid SessionId { get; init; }
}