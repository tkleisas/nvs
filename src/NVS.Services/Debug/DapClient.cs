using System.Collections.Concurrent;
using System.Text.Json;
using NVS.Core.Debug;
using NVS.Core.Interfaces;

namespace NVS.Services.Debug;

/// <summary>
/// DAP client implementation using Content-Length framed transport over stdio.
/// Follows the same request/response correlation pattern as LspClient.
/// </summary>
public sealed class DapClient : IDapClient
{
    private readonly DapTransport _transport;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private volatile bool _listenerExited;

    private int _nextSeq;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<DapResponse>> _pendingRequests = new();

    public bool IsConnected { get; private set; }
    public DapCapabilities? ServerCapabilities { get; private set; }

    public event EventHandler<DapStoppedEventBody>? Stopped;
    public event EventHandler<DapTerminatedEventBody>? Terminated;
    public event EventHandler<DapOutputEventBody>? OutputReceived;
    public event EventHandler<DapThreadEventBody>? ThreadEvent;
    public event EventHandler<DapBreakpointEventBody>? BreakpointEvent;
    public event EventHandler? Initialized;

    /// <summary>
    /// Handler for DAP "runInTerminal" reverse requests.
    /// The adapter calls this when console is set to "integratedTerminal".
    /// Returns the launched process ID (or 0 if unknown).
    /// </summary>
    public Func<DapRunInTerminalArguments, Task<int>>? RunInTerminalHandler { get; set; }

    public DapClient(DapTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    /// <summary>
    /// Starts the background message listener. Call before sending any requests.
    /// </summary>
    public void StartListening()
    {
        if (_listenerTask is not null)
            throw new InvalidOperationException("Listener is already running.");

        _listenerCts = new CancellationTokenSource();
        _listenerTask = ListenForMessagesAsync(_listenerCts.Token);
    }

    public async Task<DapCapabilities> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
            throw new InvalidOperationException("Client is already initialized.");

        if (_listenerTask is null)
            StartListening();

        var args = new DapInitializeRequestArguments
        {
            ClientId = "nvs",
            ClientName = "NVS IDE",
            AdapterId = "coreclr",
            SupportsRunInTerminalRequest = true,
        };

        var response = await SendRequestAsync("initialize", args, cancellationToken).ConfigureAwait(false);

        ServerCapabilities = response.Body.HasValue
            ? response.Body.Value.Deserialize<DapCapabilities>(DapTransport.JsonOptions)
            : new DapCapabilities();

        IsConnected = true;
        return ServerCapabilities!;
    }

    public async Task LaunchAsync(DapLaunchRequestArguments args, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("launch", args, cancellationToken).ConfigureAwait(false);
    }

    public async Task AttachAsync(DapAttachRequestArguments args, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("attach", args, cancellationToken).ConfigureAwait(false);
    }

    public async Task ConfigurationDoneAsync(CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("configurationDone", null, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DapSetBreakpointsResponseBody> SetBreakpointsAsync(DapSetBreakpointsArguments args, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("setBreakpoints", args, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapSetBreakpointsResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task ContinueAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("continue", new DapContinueArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task NextAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("next", new DapNextArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepInAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("stepIn", new DapStepInArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task StepOutAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("stepOut", new DapStepOutArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PauseAsync(int threadId, CancellationToken cancellationToken = default)
    {
        await SendRequestAsync("pause", new DapPauseArguments { ThreadId = threadId }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<DapThreadsResponseBody> GetThreadsAsync(CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("threads", null, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapThreadsResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task<DapStackTraceResponseBody> GetStackTraceAsync(DapStackTraceArguments args, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("stackTrace", args, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapStackTraceResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task<DapScopesResponseBody> GetScopesAsync(int frameId, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("scopes", new DapScopesArguments { FrameId = frameId }, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapScopesResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task<DapVariablesResponseBody> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("variables", new DapVariablesArguments { VariablesReference = variablesReference }, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapVariablesResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task<DapEvaluateResponseBody> EvaluateAsync(DapEvaluateArguments args, CancellationToken cancellationToken = default)
    {
        var response = await SendRequestAsync("evaluate", args, cancellationToken).ConfigureAwait(false);
        return response.Body!.Value.Deserialize<DapEvaluateResponseBody>(DapTransport.JsonOptions)!;
    }

    public async Task DisconnectAsync(bool terminateDebuggee = true, CancellationToken cancellationToken = default)
    {
        if (!IsConnected) return;

        try
        {
            await SendRequestAsync("disconnect", new DapDisconnectArguments { TerminateDebuggee = terminateDebuggee }, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort — adapter may have already exited
        }
        finally
        {
            IsConnected = false;
        }
    }

    // ── Request/Response Communication ─────────────────────────────────

    internal async Task<DapResponse> SendRequestAsync(string command, object? arguments, CancellationToken cancellationToken = default)
    {
        if (_listenerExited)
            throw new InvalidOperationException("Connection to debug adapter has been lost.");

        var seq = Interlocked.Increment(ref _nextSeq);
        var tcs = new TaskCompletionSource<DapResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        _pendingRequests[seq] = tcs;

        try
        {
            var argsElement = arguments is not null
                ? JsonSerializer.SerializeToElement(arguments, DapTransport.JsonOptions)
                : (JsonElement?)null;

            var request = new DapRequest
            {
                Seq = seq,
                Command = command,
                Arguments = argsElement,
            };

            await _transport.WriteMessageAsync(request, cancellationToken).ConfigureAwait(false);
            var response = await tcs.Task.ConfigureAwait(false);

            if (!response.Success)
                throw new DapRequestException(command, response.Message ?? "Unknown error");

            return response;
        }
        finally
        {
            _pendingRequests.TryRemove(seq, out _);
        }
    }

    // ── Message Listener ──────────────────────────────────────────────

    private async Task ListenForMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var message = await _transport.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (message is null) break;

                switch (message)
                {
                    case DapResponse response:
                        HandleResponse(response);
                        break;
                    case DapEvent evt:
                        HandleEvent(evt);
                        break;
                    case DapRequest request:
                        // Reverse requests from adapter (e.g., runInTerminal) — respond empty for now
                        await HandleReverseRequestAsync(request, cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                if (cancellationToken.IsCancellationRequested) break;
            }
        }

        // Cancel all pending requests on disconnect
        foreach (var (_, tcs) in _pendingRequests)
            tcs.TrySetCanceled();

        _pendingRequests.Clear();
        _listenerExited = true;
    }

    private void HandleResponse(DapResponse response)
    {
        if (_pendingRequests.TryRemove(response.RequestSeq, out var tcs))
            tcs.TrySetResult(response);
    }

    private void HandleEvent(DapEvent evt)
    {
        switch (evt.Event)
        {
            case "stopped":
                var stoppedBody = evt.Body?.Deserialize<DapStoppedEventBody>(DapTransport.JsonOptions);
                if (stoppedBody is not null)
                    Stopped?.Invoke(this, stoppedBody);
                break;

            case "terminated":
                var terminatedBody = evt.Body?.Deserialize<DapTerminatedEventBody>(DapTransport.JsonOptions)
                    ?? new DapTerminatedEventBody();
                Terminated?.Invoke(this, terminatedBody);
                break;

            case "output":
                var outputBody = evt.Body?.Deserialize<DapOutputEventBody>(DapTransport.JsonOptions);
                if (outputBody is not null)
                    OutputReceived?.Invoke(this, outputBody);
                break;

            case "thread":
                var threadBody = evt.Body?.Deserialize<DapThreadEventBody>(DapTransport.JsonOptions);
                if (threadBody is not null)
                    ThreadEvent?.Invoke(this, threadBody);
                break;

            case "breakpoint":
                var bpBody = evt.Body?.Deserialize<DapBreakpointEventBody>(DapTransport.JsonOptions);
                if (bpBody is not null)
                    BreakpointEvent?.Invoke(this, bpBody);
                break;

            case "initialized":
                Initialized?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private async Task HandleReverseRequestAsync(DapRequest request, CancellationToken cancellationToken)
    {
        if (request.Command == "runInTerminal" && RunInTerminalHandler is not null)
        {
            var args = request.Arguments?.Deserialize<DapRunInTerminalArguments>(DapTransport.JsonOptions);
            if (args is not null)
            {
                var processId = await RunInTerminalHandler(args).ConfigureAwait(false);
                var responseBody = new DapRunInTerminalResponseBody { ProcessId = processId > 0 ? processId : null };
                var response = new DapResponse
                {
                    Seq = Interlocked.Increment(ref _nextSeq),
                    RequestSeq = request.Seq,
                    Success = true,
                    Command = request.Command,
                    Body = JsonSerializer.SerializeToElement(responseBody, DapTransport.JsonOptions),
                };
                await _transport.WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // Default: acknowledge unknown reverse requests
        var defaultResponse = new DapResponse
        {
            Seq = Interlocked.Increment(ref _nextSeq),
            RequestSeq = request.Seq,
            Success = true,
            Command = request.Command,
        };
        await _transport.WriteMessageAsync(defaultResponse, cancellationToken).ConfigureAwait(false);
    }

    // ── Dispose ───────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        // Cancel the listener first so pending requests are failed.
        _listenerCts?.Cancel();

        if (IsConnected)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await DisconnectAsync(cancellationToken: cts.Token).ConfigureAwait(false);
            }
            catch { /* best effort */ }
        }

        if (_listenerTask is not null)
        {
            try { await _listenerTask.ConfigureAwait(false); }
            catch { /* listener may throw on cancellation */ }
        }

        _listenerCts?.Dispose();
        await _transport.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Exception thrown when a DAP request returns a failed response.
/// </summary>
public sealed class DapRequestException : Exception
{
    public string Command { get; }

    public DapRequestException(string command, string message)
        : base($"DAP request '{command}' failed: {message}")
    {
        Command = command;
    }
}
