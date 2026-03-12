using System.Diagnostics;
using System.Text.Json;
using NVS.Core.Debug;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Debug;

/// <summary>
/// Implements IDebugService by managing a DapClient lifecycle.
/// Bridges DAP protocol to the application-level debug abstraction.
/// </summary>
public sealed class DebugService : IDebugService, IAsyncDisposable
{
    private readonly DebugAdapterRegistry _adapterRegistry;
    private DapClient? _client;
    private Process? _adapterProcess;
    private int _activeThreadId;

    public bool IsDebugging { get; private set; }
    public bool IsPaused { get; private set; }
    public DebugSession? CurrentSession { get; private set; }

    public event EventHandler<DebugSession>? DebuggingStarted;
    public event EventHandler<DebugSession>? DebuggingStopped;
    public event EventHandler? DebuggingPaused;
    public event EventHandler? DebuggingContinued;
    public event EventHandler<ThreadInfo>? ThreadStarted;
    public event EventHandler<ThreadInfo>? ThreadExited;
    public event EventHandler<Breakpoint>? BreakpointHit;
    public event EventHandler<OutputEvent>? OutputReceived;

    public DebugService(DebugAdapterRegistry adapterRegistry)
    {
        _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
    }

    // Internal constructor for testing — allows injecting a pre-built DapClient
    internal DebugService(DebugAdapterRegistry adapterRegistry, DapClient client)
        : this(adapterRegistry)
    {
        _client = client;
    }

    public async Task<DebugSession> StartDebuggingAsync(DebugConfiguration configuration, CancellationToken cancellationToken = default)
    {
        if (IsDebugging)
            throw new InvalidOperationException("A debug session is already active.");

        var adapterType = configuration.Type;
        var adapterInfo = _adapterRegistry.GetAdapter(adapterType)
            ?? throw new InvalidOperationException($"No debug adapter registered for type '{adapterType}'.");

        if (_client is null)
        {
            var adapterPath = _adapterRegistry.FindAdapterExecutable(adapterType)
                ?? throw new InvalidOperationException(
                    $"Debug adapter '{adapterInfo.DisplayName}' not found. " +
                    $"Please install {adapterInfo.ExecutableName} and ensure it's on your PATH.");

            _adapterProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = adapterPath,
                    Arguments = string.Join(' ', adapterInfo.Arguments),
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                },
            };

            _adapterProcess.Start();

            var transport = new DapTransport(
                _adapterProcess.StandardOutput.BaseStream,
                _adapterProcess.StandardInput.BaseStream);

            _client = new DapClient(transport);
        }

        SubscribeToClientEvents(_client);

        // DAP initialization sequence: initialize → (wait for initialized event) → launch → configurationDone
        await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var launchArgs = new DapLaunchRequestArguments
        {
            Program = configuration.Program ?? throw new InvalidOperationException("Program path is required."),
            Args = configuration.Args.Count > 0 ? configuration.Args : null,
            Cwd = configuration.Cwd,
        };

        await _client.LaunchAsync(launchArgs, cancellationToken).ConfigureAwait(false);
        await _client.ConfigurationDoneAsync(cancellationToken).ConfigureAwait(false);

        var session = new DebugSession
        {
            Id = Guid.NewGuid(),
            Name = configuration.Name,
            Type = configuration.Type,
        };

        CurrentSession = session;
        IsDebugging = true;
        IsPaused = false;

        DebuggingStarted?.Invoke(this, session);
        return session;
    }

    public async Task StopDebuggingAsync(CancellationToken cancellationToken = default)
    {
        if (!IsDebugging || _client is null) return;

        try
        {
            await _client.DisconnectAsync(terminateDebuggee: true, cancellationToken).ConfigureAwait(false);
        }
        catch { /* best effort */ }

        await CleanupSessionAsync().ConfigureAwait(false);
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        await _client!.PauseAsync(_activeThreadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task ContinueAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        await _client!.ContinueAsync(_activeThreadId, cancellationToken).ConfigureAwait(false);
        IsPaused = false;
        DebuggingContinued?.Invoke(this, EventArgs.Empty);
    }

    public async Task StepOverAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        await _client!.NextAsync(_activeThreadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepIntoAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        await _client!.StepInAsync(_activeThreadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepOutAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        await _client!.StepOutAsync(_activeThreadId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ThreadInfo>> GetThreadsAsync(CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        var result = await _client!.GetThreadsAsync(cancellationToken).ConfigureAwait(false);
        return result.Threads.Select(t => new ThreadInfo { Id = t.Id, Name = t.Name }).ToList();
    }

    public async Task<IReadOnlyList<Core.Interfaces.StackFrame>> GetStackTraceAsync(int threadId, CancellationToken cancellationToken = default)
    {
        EnsureDebugging();
        var result = await _client!.GetStackTraceAsync(
            new DapStackTraceArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);

        return result.StackFrames.Select(f => new Core.Interfaces.StackFrame
        {
            Id = f.Id,
            Name = f.Name,
            Source = f.Source?.Path,
            Line = f.Line,
            Column = f.Column,
        }).ToList();
    }

    public async Task<IReadOnlyList<Variable>> GetVariablesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        EnsureDebugging();

        // Get scopes for the frame, then variables for each scope
        var scopesResult = await _client!.GetScopesAsync(frameId, cancellationToken).ConfigureAwait(false);

        var allVariables = new List<Variable>();
        foreach (var scope in scopesResult.Scopes)
        {
            var varsResult = await _client.GetVariablesAsync(scope.VariablesReference, cancellationToken).ConfigureAwait(false);
            allVariables.AddRange(varsResult.Variables.Select(v => new Variable
            {
                Name = v.Name,
                Value = v.Value,
                Type = v.Type ?? "unknown",
                VariablesReference = v.VariablesReference > 0 ? v.VariablesReference : null,
            }));
        }

        return allVariables;
    }

    public async Task<IReadOnlyList<Breakpoint>> SetBreakpointsAsync(string path, IReadOnlyList<int> lines, CancellationToken cancellationToken = default)
    {
        EnsureDebugging();

        var args = new DapSetBreakpointsArguments
        {
            Source = new DapSource { Path = path, Name = Path.GetFileName(path) },
            Breakpoints = lines.Select(l => new DapSourceBreakpoint { Line = l }).ToList(),
        };

        var result = await _client!.SetBreakpointsAsync(args, cancellationToken).ConfigureAwait(false);

        return result.Breakpoints.Select(b => new Breakpoint
        {
            Id = Guid.NewGuid(),
            Path = path,
            Line = b.Line ?? 0,
            IsVerified = b.Verified,
        }).ToList();
    }

    // ── Event Handlers ────────────────────────────────────────────────

    private void SubscribeToClientEvents(DapClient client)
    {
        client.Stopped += OnClientStopped;
        client.Terminated += OnClientTerminated;
        client.OutputReceived += OnClientOutput;
        client.ThreadEvent += OnClientThreadEvent;
    }

    private void UnsubscribeFromClientEvents(DapClient client)
    {
        client.Stopped -= OnClientStopped;
        client.Terminated -= OnClientTerminated;
        client.OutputReceived -= OnClientOutput;
        client.ThreadEvent -= OnClientThreadEvent;
    }

    private void OnClientStopped(object? sender, DapStoppedEventBody body)
    {
        IsPaused = true;
        _activeThreadId = body.ThreadId ?? _activeThreadId;

        DebuggingPaused?.Invoke(this, EventArgs.Empty);

        if (body.Reason == "breakpoint")
        {
            BreakpointHit?.Invoke(this, new Breakpoint
            {
                Id = Guid.Empty,
                Path = string.Empty,
                Line = 0,
                IsVerified = true,
            });
        }
    }

    private void OnClientTerminated(object? sender, DapTerminatedEventBody body)
    {
        _ = CleanupSessionAsync();
    }

    private void OnClientOutput(object? sender, DapOutputEventBody body)
    {
        var category = body.Category switch
        {
            "stdout" => OutputCategory.Stdout,
            "stderr" => OutputCategory.Stderr,
            "console" => OutputCategory.Console,
            "telemetry" => OutputCategory.Telemetry,
            _ => OutputCategory.Console,
        };

        OutputReceived?.Invoke(this, new OutputEvent
        {
            Output = body.Output,
            Category = category,
        });
    }

    private void OnClientThreadEvent(object? sender, DapThreadEventBody body)
    {
        var threadInfo = new ThreadInfo { Id = body.ThreadId, Name = $"Thread {body.ThreadId}" };

        if (body.Reason == "started")
            ThreadStarted?.Invoke(this, threadInfo);
        else if (body.Reason == "exited")
            ThreadExited?.Invoke(this, threadInfo);
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private void EnsureDebugging()
    {
        if (!IsDebugging || _client is null)
            throw new InvalidOperationException("No active debug session.");
    }

    private async Task CleanupSessionAsync()
    {
        var session = CurrentSession;
        IsDebugging = false;
        IsPaused = false;
        CurrentSession = null;
        _activeThreadId = 0;

        if (_client is not null)
        {
            UnsubscribeFromClientEvents(_client);
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }

        if (_adapterProcess is not null)
        {
            try
            {
                if (!_adapterProcess.HasExited)
                    _adapterProcess.Kill(entireProcessTree: true);
            }
            catch { /* process may have already exited */ }

            _adapterProcess.Dispose();
            _adapterProcess = null;
        }

        if (session is not null)
            DebuggingStopped?.Invoke(this, session);
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDebugging)
        {
            try { await StopDebuggingAsync().ConfigureAwait(false); }
            catch { /* best effort */ }
        }

        await CleanupSessionAsync().ConfigureAwait(false);
    }
}
