using System.Diagnostics;
using System.Net.Sockets;
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
    private readonly IBreakpointStore? _breakpointStore;
    private DapClient? _client;
    private Process? _adapterProcess;
    private TcpClient? _tcpClient;
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

    public Func<RunInTerminalRequest, Task<int>>? RunInTerminalHandler { get; set; }

    public DebugService(DebugAdapterRegistry adapterRegistry, IBreakpointStore? breakpointStore = null)
    {
        _adapterRegistry = adapterRegistry ?? throw new ArgumentNullException(nameof(adapterRegistry));
        _breakpointStore = breakpointStore;
    }

    // Internal constructor for testing — allows injecting a pre-built DapClient
    internal DebugService(DebugAdapterRegistry adapterRegistry, DapClient client)
        : this(adapterRegistry)
    {
        _client = client;
    }

    public async Task<string> ResolveAdapterPathAsync(string adapterType, CancellationToken cancellationToken = default)
    {
        var adapterInfo = _adapterRegistry.GetAdapter(adapterType)
            ?? throw new InvalidOperationException($"No debug adapter registered for type '{adapterType}'.");

        var adapterPath = _adapterRegistry.FindAdapterExecutable(adapterType);

        if (adapterPath is null && adapterInfo.ExecutableName == "netcoredbg")
        {
            OutputReceived?.Invoke(this, new OutputEvent
            {
                Output = "netcoredbg not found — downloading automatically...\n",
                Category = OutputCategory.Console,
            });

            var progress = new Progress<string>(msg =>
                OutputReceived?.Invoke(this, new OutputEvent
                {
                    Output = msg + "\n",
                    Category = OutputCategory.Console,
                }));

            adapterPath = await _adapterRegistry.Downloader
                .EnsureNetcoredbgAsync(progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return adapterPath
            ?? throw new InvalidOperationException(
                $"Debug adapter '{adapterInfo.DisplayName}' not found. " +
                $"Please install {adapterInfo.ExecutableName} and ensure it's on your PATH.");
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
            if (configuration.ServerPort is int port)
            {
                // Connect via TCP to an adapter already running in server mode
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync("127.0.0.1", port, cancellationToken).ConfigureAwait(false);
                var stream = _tcpClient.GetStream();
                var transport = new DapTransport(stream, stream);
                _client = new DapClient(transport);
            }
            else
            {
                var adapterPath = await ResolveAdapterPathAsync(adapterType, cancellationToken).ConfigureAwait(false);

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
        }

        SubscribeToClientEvents(_client);
        WireRunInTerminalHandler(_client);

        // DAP initialization sequence:
        // 1. initialize → response (capabilities)
        // 2. launch → response (adapter sets up debuggee)
        // 3. Wait for "initialized" event from adapter
        // 4. setBreakpoints (per file)
        // 5. configurationDone → adapter starts execution

        var initializedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnInitialized(object? s, EventArgs e) => initializedTcs.TrySetResult();
        _client.Initialized += OnInitialized;

        try
        {
            await _client.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var launchArgs = new DapLaunchRequestArguments
            {
                Program = configuration.Program ?? throw new InvalidOperationException("Program path is required."),
                Args = configuration.Args.Count > 0 ? configuration.Args : null,
                Cwd = configuration.Cwd,
                Console = configuration.Console,
            };

            await _client.LaunchAsync(launchArgs, cancellationToken).ConfigureAwait(false);

            // Wait for the initialized event (with timeout)
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));
            await using (timeoutCts.Token.Register(() => initializedTcs.TrySetCanceled()))
            {
                await initializedTcs.Task.ConfigureAwait(false);
            }

            // Send all breakpoints from the store before configurationDone
            await SyncBreakpointsToAdapterAsync(cancellationToken).ConfigureAwait(false);

            await _client.ConfigurationDoneAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _client.Initialized -= OnInitialized;
        }

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
            // Give the adapter a few seconds to disconnect gracefully;
            // if the debuggee is blocked on I/O it may never respond.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(3));
            await _client.DisconnectAsync(terminateDebuggee: true, timeoutCts.Token).ConfigureAwait(false);
        }
        catch { /* best effort — timeout or adapter already exited */ }

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

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var allVariables = new List<Variable>();
        foreach (var scope in scopesResult.Scopes)
        {
            var varsResult = await _client.GetVariablesAsync(scope.VariablesReference, cancellationToken).ConfigureAwait(false);
            foreach (var v in varsResult.Variables)
            {
                if (!seen.Add(v.Name))
                    continue;

                allVariables.Add(new Variable
                {
                    Name = v.Name,
                    Value = v.Value,
                    Type = v.Type ?? "unknown",
                    VariablesReference = v.VariablesReference > 0 ? v.VariablesReference : null,
                });
            }
        }

        return allVariables;
    }

    public async Task<IReadOnlyList<Variable>> GetChildVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        EnsureDebugging();

        var result = await _client!.GetVariablesAsync(variablesReference, cancellationToken).ConfigureAwait(false);
        return result.Variables.Select(v => new Variable
        {
            Name = v.Name,
            Value = v.Value,
            Type = v.Type ?? "unknown",
            VariablesReference = v.VariablesReference > 0 ? v.VariablesReference : null,
        }).ToList();
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

    private void WireRunInTerminalHandler(DapClient client)
    {
        client.RunInTerminalHandler = async (dapArgs) =>
        {
            if (RunInTerminalHandler is null) return 0;

            var request = new RunInTerminalRequest
            {
                Cwd = dapArgs.Cwd ?? ".",
                Args = dapArgs.Args ?? [],
                Env = dapArgs.Env,
                Title = dapArgs.Title,
            };

            return await RunInTerminalHandler(request).ConfigureAwait(false);
        };
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

    /// <summary>
    /// Sends all breakpoints from the store to the debug adapter.
    /// Must be called after initialize/launch and before configurationDone.
    /// </summary>
    private async Task SyncBreakpointsToAdapterAsync(CancellationToken cancellationToken)
    {
        if (_breakpointStore is null || _client is null) return;

        var allBreakpoints = _breakpointStore.GetAllBreakpoints();
        if (allBreakpoints.Count == 0) return;

        // Group breakpoints by file path
        var byFile = allBreakpoints
            .Where(b => b.IsEnabled)
            .GroupBy(b => b.Path, StringComparer.OrdinalIgnoreCase);

        foreach (var group in byFile)
        {
            var args = new DapSetBreakpointsArguments
            {
                Source = new DapSource { Path = group.Key, Name = Path.GetFileName(group.Key) },
                Breakpoints = group.Select(b => new DapSourceBreakpoint { Line = b.Line }).ToList(),
            };

            try
            {
                var result = await _client.SetBreakpointsAsync(args, cancellationToken).ConfigureAwait(false);

                // Update verified status in the store
                foreach (var bp in result.Breakpoints)
                {
                    if (bp.Line.HasValue)
                        _breakpointStore.UpdateVerifiedStatus(group.Key, bp.Line.Value, bp.Verified);
                }
            }
            catch
            {
                // Best effort — don't fail the whole debug session for one file's breakpoints
            }
        }
    }

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

        if (_tcpClient is not null)
        {
            _tcpClient.Dispose();
            _tcpClient = null;
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
