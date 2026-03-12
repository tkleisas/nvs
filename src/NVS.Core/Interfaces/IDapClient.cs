using System.Text.Json;
using NVS.Core.Debug;

namespace NVS.Core.Interfaces;

/// <summary>
/// Client interface for the Debug Adapter Protocol (DAP).
/// Manages the request/response lifecycle with a debug adapter process.
/// </summary>
public interface IDapClient : IAsyncDisposable
{
    bool IsConnected { get; }
    DapCapabilities? ServerCapabilities { get; }

    Task<DapCapabilities> InitializeAsync(CancellationToken cancellationToken = default);
    Task LaunchAsync(DapLaunchRequestArguments args, CancellationToken cancellationToken = default);
    Task ConfigurationDoneAsync(CancellationToken cancellationToken = default);
    Task<DapSetBreakpointsResponseBody> SetBreakpointsAsync(DapSetBreakpointsArguments args, CancellationToken cancellationToken = default);
    Task ContinueAsync(int threadId, CancellationToken cancellationToken = default);
    Task NextAsync(int threadId, CancellationToken cancellationToken = default);
    Task StepInAsync(int threadId, CancellationToken cancellationToken = default);
    Task StepOutAsync(int threadId, CancellationToken cancellationToken = default);
    Task PauseAsync(int threadId, CancellationToken cancellationToken = default);
    Task<DapThreadsResponseBody> GetThreadsAsync(CancellationToken cancellationToken = default);
    Task<DapStackTraceResponseBody> GetStackTraceAsync(DapStackTraceArguments args, CancellationToken cancellationToken = default);
    Task<DapScopesResponseBody> GetScopesAsync(int frameId, CancellationToken cancellationToken = default);
    Task<DapVariablesResponseBody> GetVariablesAsync(int variablesReference, CancellationToken cancellationToken = default);
    Task DisconnectAsync(bool terminateDebuggee = true, CancellationToken cancellationToken = default);

    event EventHandler<DapStoppedEventBody>? Stopped;
    event EventHandler<DapTerminatedEventBody>? Terminated;
    event EventHandler<DapOutputEventBody>? OutputReceived;
    event EventHandler<DapThreadEventBody>? ThreadEvent;
    event EventHandler<DapBreakpointEventBody>? BreakpointEvent;
    event EventHandler? Initialized;
}
