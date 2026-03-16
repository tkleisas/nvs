using System.Text;
using System.Text.Json;
using NVS.Core.Debug;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.Services.Debug;

namespace NVS.Services.Tests;

/// <summary>
/// Tests for DapClient request/response lifecycle, event dispatching, and error handling.
/// Uses a MockDapServer that communicates over in-memory streams.
/// </summary>
public sealed class DapClientTests : IAsyncDisposable
{
    private readonly MockDapServer _server;
    private readonly DapClient _client;

    public DapClientTests()
    {
        // Create bidirectional pipes: client writes to serverInput, server writes to clientInput
        var clientInput = new BlockingMemoryStream();
        var clientOutput = new BlockingMemoryStream();

        _server = new MockDapServer(clientOutput, clientInput);
        var transport = new DapTransport(clientInput, clientOutput);
        _client = new DapClient(transport);
    }

    public async ValueTask DisposeAsync()
    {
        _server.Stop();
        await _client.DisposeAsync();
    }

    // ── Initialize ────────────────────────────────────────────────────

    [Fact]
    public async Task InitializeAsync_ShouldSendRequestAndReturnCapabilities()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities
        {
            SupportsConfigurationDoneRequest = true,
            SupportsConditionalBreakpoints = true,
        });
        _server.Start();

        var caps = await _client.InitializeAsync();

        caps.Should().NotBeNull();
        caps.SupportsConfigurationDoneRequest.Should().BeTrue();
        caps.SupportsConditionalBreakpoints.Should().BeTrue();
        _client.IsConnected.Should().BeTrue();
        _client.ServerCapabilities.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyConnected_ShouldThrow()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        await _client.InitializeAsync();

        var act = () => _client.InitializeAsync();
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── Launch ────────────────────────────────────────────────────────

    [Fact]
    public async Task LaunchAsync_ShouldSendLaunchRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.LaunchAsync(new DapLaunchRequestArguments { Program = "/bin/app" });

        _server.ReceivedCommands.Should().Contain("launch");
    }

    // ── ConfigurationDone ─────────────────────────────────────────────

    [Fact]
    public async Task ConfigurationDoneAsync_ShouldSendRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("configurationDone", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.ConfigurationDoneAsync();

        _server.ReceivedCommands.Should().Contain("configurationDone");
    }

    // ── SetBreakpoints ────────────────────────────────────────────────

    [Fact]
    public async Task SetBreakpointsAsync_ShouldReturnVerifiedBreakpoints()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("setBreakpoints", new DapSetBreakpointsResponseBody
        {
            Breakpoints =
            [
                new DapBreakpoint { Id = 1, Verified = true, Line = 10 },
                new DapBreakpoint { Id = 2, Verified = true, Line = 20 },
            ]
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.SetBreakpointsAsync(new DapSetBreakpointsArguments
        {
            Source = new DapSource { Path = "/src/Program.cs" },
            Breakpoints = [new DapSourceBreakpoint { Line = 10 }, new DapSourceBreakpoint { Line = 20 }],
        });

        result.Breakpoints.Should().HaveCount(2);
        result.Breakpoints[0].Verified.Should().BeTrue();
        result.Breakpoints[0].Line.Should().Be(10);
    }

    // ── Continue / Step ───────────────────────────────────────────────

    [Fact]
    public async Task ContinueAsync_ShouldSendContinueRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("continue", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.ContinueAsync(1);

        _server.ReceivedCommands.Should().Contain("continue");
    }

    [Fact]
    public async Task NextAsync_ShouldSendNextRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("next", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.NextAsync(1);

        _server.ReceivedCommands.Should().Contain("next");
    }

    [Fact]
    public async Task StepInAsync_ShouldSendStepInRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("stepIn", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.StepInAsync(1);

        _server.ReceivedCommands.Should().Contain("stepIn");
    }

    [Fact]
    public async Task StepOutAsync_ShouldSendStepOutRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("stepOut", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.StepOutAsync(1);

        _server.ReceivedCommands.Should().Contain("stepOut");
    }

    [Fact]
    public async Task PauseAsync_ShouldSendPauseRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("pause", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.PauseAsync(1);

        _server.ReceivedCommands.Should().Contain("pause");
    }

    // ── Threads ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetThreadsAsync_ShouldReturnThreadList()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("threads", new DapThreadsResponseBody
        {
            Threads = [new DapThread { Id = 1, Name = "Main Thread" }, new DapThread { Id = 2, Name = "Worker" }]
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.GetThreadsAsync();

        result.Threads.Should().HaveCount(2);
        result.Threads[0].Name.Should().Be("Main Thread");
    }

    // ── Stack Trace ───────────────────────────────────────────────────

    [Fact]
    public async Task GetStackTraceAsync_ShouldReturnFrames()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("stackTrace", new DapStackTraceResponseBody
        {
            StackFrames =
            [
                new DapStackFrame
                {
                    Id = 1, Name = "Main", Line = 10, Column = 5,
                    Source = new DapSource { Path = "/src/Program.cs", Name = "Program.cs" },
                },
            ],
            TotalFrames = 1,
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.GetStackTraceAsync(new DapStackTraceArguments { ThreadId = 1 });

        result.StackFrames.Should().HaveCount(1);
        result.StackFrames[0].Name.Should().Be("Main");
        result.StackFrames[0].Source!.Path.Should().Be("/src/Program.cs");
    }

    // ── Scopes & Variables ────────────────────────────────────────────

    [Fact]
    public async Task GetScopesAsync_ShouldReturnScopes()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("scopes", new DapScopesResponseBody
        {
            Scopes = [new DapScope { Name = "Locals", VariablesReference = 100 }]
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.GetScopesAsync(1);

        result.Scopes.Should().HaveCount(1);
        result.Scopes[0].Name.Should().Be("Locals");
        result.Scopes[0].VariablesReference.Should().Be(100);
    }

    [Fact]
    public async Task GetVariablesAsync_ShouldReturnVariables()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("variables", new DapVariablesResponseBody
        {
            Variables =
            [
                new DapVariable { Name = "x", Value = "42", Type = "int", VariablesReference = 0 },
                new DapVariable { Name = "name", Value = "\"hello\"", Type = "string", VariablesReference = 0 },
            ]
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.GetVariablesAsync(100);

        result.Variables.Should().HaveCount(2);
        result.Variables[0].Name.Should().Be("x");
        result.Variables[0].Value.Should().Be("42");
    }

    // ── Evaluate ──────────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_ShouldReturnResult()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("evaluate", new DapEvaluateResponseBody
        {
            Result = "42",
            Type = "int",
            VariablesReference = 0,
        });
        _server.Start();

        await _client.InitializeAsync();
        var result = await _client.EvaluateAsync(new DapEvaluateArguments
        {
            Expression = "x",
            FrameId = 1,
            Context = "hover",
        });

        result.Result.Should().Be("42");
        result.Type.Should().Be("int");
        _server.ReceivedCommands.Should().Contain("evaluate");
    }

    // ── Disconnect ────────────────────────────────────────────────────

    [Fact]
    public async Task DisconnectAsync_ShouldSendDisconnectRequest()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("disconnect", null);
        _server.Start();

        await _client.InitializeAsync();
        await _client.DisconnectAsync();

        _server.ReceivedCommands.Should().Contain("disconnect");
        _client.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_ShouldNotThrow()
    {
        _server.Start();
        await _client.DisconnectAsync(); // Should be a no-op
    }

    // ── Error Handling ────────────────────────────────────────────────

    [Fact]
    public async Task SendRequest_WhenResponseFails_ShouldThrowDapRequestException()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueFailure("launch", "File not found");
        _server.Start();

        await _client.InitializeAsync();

        var act = () => _client.LaunchAsync(new DapLaunchRequestArguments { Program = "/nonexistent" });
        await act.Should().ThrowAsync<DapRequestException>()
            .WithMessage("*launch*File not found*");
    }

    // ── Event Dispatch ────────────────────────────────────────────────

    [Fact]
    public async Task StoppedEvent_ShouldRaiseStoppedHandler()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        DapStoppedEventBody? receivedBody = null;
        var tcs = new TaskCompletionSource<DapStoppedEventBody>();
        _client.Stopped += (_, body) => tcs.TrySetResult(body);

        await _client.InitializeAsync();

        _server.SendEvent("stopped", new DapStoppedEventBody
        {
            Reason = "breakpoint", ThreadId = 1, AllThreadsStopped = true,
        });

        receivedBody = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        receivedBody.Should().NotBeNull();
        receivedBody!.Reason.Should().Be("breakpoint");
        receivedBody.ThreadId.Should().Be(1);
    }

    [Fact]
    public async Task OutputEvent_ShouldRaiseOutputHandler()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        var tcs = new TaskCompletionSource<DapOutputEventBody>();
        _client.OutputReceived += (_, body) => tcs.TrySetResult(body);

        await _client.InitializeAsync();

        _server.SendEvent("output", new DapOutputEventBody
        {
            Category = "stdout", Output = "Hello World\n",
        });

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        result.Output.Should().Be("Hello World\n");
        result.Category.Should().Be("stdout");
    }

    [Fact]
    public async Task TerminatedEvent_ShouldRaiseTerminatedHandler()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        var tcs = new TaskCompletionSource<DapTerminatedEventBody>();
        _client.Terminated += (_, body) => tcs.TrySetResult(body);

        await _client.InitializeAsync();

        _server.SendEvent("terminated", new DapTerminatedEventBody());

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task InitializedEvent_ShouldRaiseInitializedHandler()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        var tcs = new TaskCompletionSource<bool>();
        _client.Initialized += (_, _) => tcs.TrySetResult(true);

        await _client.InitializeAsync();

        // Send initialized event (no body)
        _server.SendEvent("initialized", null);

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ThreadEvent_ShouldRaiseThreadHandler()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.Start();

        var tcs = new TaskCompletionSource<DapThreadEventBody>();
        _client.ThreadEvent += (_, body) => tcs.TrySetResult(body);

        await _client.InitializeAsync();

        _server.SendEvent("thread", new DapThreadEventBody { Reason = "started", ThreadId = 2 });

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        result.Reason.Should().Be("started");
        result.ThreadId.Should().Be(2);
    }
}

// ── DebugService Tests ──────────────────────────────────────────────────

public sealed class DebugServiceTests : IAsyncDisposable
{
    private readonly MockDapServer _server;
    private readonly DebugService _debugService;

    public DebugServiceTests()
    {
        var clientInput = new BlockingMemoryStream();
        var clientOutput = new BlockingMemoryStream();

        _server = new MockDapServer(clientOutput, clientInput);
        var transport = new DapTransport(clientInput, clientOutput);
        var client = new DapClient(transport);

        var registry = new DebugAdapterRegistry();
        _debugService = new DebugService(registry, client);
    }

    public async ValueTask DisposeAsync()
    {
        _server.Stop();
        await _debugService.DisposeAsync();
    }

    [Fact]
    public async Task StartDebuggingAsync_ShouldInitializeAndLaunch()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities { SupportsConfigurationDoneRequest = true });
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.Start();

        var config = new DebugConfiguration
        {
            Name = "Test Debug",
            Type = "coreclr",
            Request = "launch",
            Program = "/bin/myapp",
        };

        var session = await _debugService.StartDebuggingAsync(config);

        session.Should().NotBeNull();
        session.Name.Should().Be("Test Debug");
        session.Type.Should().Be("coreclr");
        _debugService.IsDebugging.Should().BeTrue();
        _debugService.IsPaused.Should().BeFalse();
    }

    [Fact]
    public async Task StartDebuggingAsync_ShouldRaiseDebuggingStartedEvent()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.Start();

        DebugSession? receivedSession = null;
        _debugService.DebuggingStarted += (_, s) => receivedSession = s;

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        receivedSession.Should().NotBeNull();
        receivedSession!.Name.Should().Be("Test");
    }

    [Fact]
    public async Task StartDebuggingAsync_WhenAlreadyDebugging_ShouldThrow()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.Start();

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        var act = () => _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test2", Type = "coreclr", Request = "launch", Program = "/app2",
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StopDebuggingAsync_ShouldDisconnectAndCleanup()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.EnqueueResponse("disconnect", null);
        _server.Start();

        DebugSession? stoppedSession = null;
        _debugService.DebuggingStopped += (_, s) => stoppedSession = s;

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        await _debugService.StopDebuggingAsync();

        _debugService.IsDebugging.Should().BeFalse();
        _debugService.CurrentSession.Should().BeNull();
        stoppedSession.Should().NotBeNull();
    }

    [Fact]
    public async Task GetThreadsAsync_ShouldReturnMappedThreads()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.EnqueueResponse("threads", new DapThreadsResponseBody
        {
            Threads = [new DapThread { Id = 1, Name = "Main" }]
        });
        _server.Start();

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        var threads = await _debugService.GetThreadsAsync();
        threads.Should().HaveCount(1);
        threads[0].Id.Should().Be(1);
        threads[0].Name.Should().Be("Main");
    }

    [Fact]
    public async Task GetStackTraceAsync_ShouldReturnMappedFrames()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.EnqueueResponse("stackTrace", new DapStackTraceResponseBody
        {
            StackFrames =
            [
                new DapStackFrame
                {
                    Id = 1, Name = "Main", Line = 10, Column = 1,
                    Source = new DapSource { Path = "/src/Program.cs" },
                }
            ]
        });
        _server.Start();

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        var frames = await _debugService.GetStackTraceAsync(1);
        frames.Should().HaveCount(1);
        frames[0].Name.Should().Be("Main");
        frames[0].Source.Should().Be("/src/Program.cs");
        frames[0].Line.Should().Be(10);
    }

    [Fact]
    public async Task GetVariablesAsync_ShouldFetchScopesThenVariables()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.EnqueueResponse("scopes", new DapScopesResponseBody
        {
            Scopes = [new DapScope { Name = "Locals", VariablesReference = 100 }]
        });
        _server.EnqueueResponse("variables", new DapVariablesResponseBody
        {
            Variables =
            [
                new DapVariable { Name = "x", Value = "42", Type = "int" },
                new DapVariable { Name = "s", Value = "\"hello\"", Type = "string" },
            ]
        });
        _server.Start();

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        var vars = await _debugService.GetVariablesAsync(1);
        vars.Should().HaveCount(2);
        vars[0].Name.Should().Be("x");
        vars[0].Value.Should().Be("42");
        vars[1].Type.Should().Be("string");
    }

    [Fact]
    public async Task SetBreakpointsAsync_ShouldReturnMappedBreakpoints()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.EnqueueResponse("setBreakpoints", new DapSetBreakpointsResponseBody
        {
            Breakpoints =
            [
                new DapBreakpoint { Id = 1, Verified = true, Line = 10 },
            ]
        });
        _server.Start();

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        var bps = await _debugService.SetBreakpointsAsync("/src/Program.cs", [10]);
        bps.Should().HaveCount(1);
        bps[0].Line.Should().Be(10);
        bps[0].IsVerified.Should().BeTrue();
        bps[0].Path.Should().Be("/src/Program.cs");
    }

    [Fact]
    public async Task OutputEvent_ShouldRaiseOutputReceivedEvent()
    {
        _server.EnqueueResponse("initialize", new DapCapabilities());
        _server.EnqueueResponse("launch", null);
        _server.EnqueueEventAfterResponse("launch", "initialized");
        _server.EnqueueResponse("configurationDone", null);
        _server.Start();

        var tcs = new TaskCompletionSource<OutputEvent>();
        _debugService.OutputReceived += (_, evt) => tcs.TrySetResult(evt);

        await _debugService.StartDebuggingAsync(new DebugConfiguration
        {
            Name = "Test", Type = "coreclr", Request = "launch", Program = "/app",
        });

        _server.SendEvent("output", new DapOutputEventBody { Category = "stdout", Output = "Hello\n" });

        var result = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(2));
        result.Output.Should().Be("Hello\n");
        result.Category.Should().Be(OutputCategory.Stdout);
    }
}

// ── DebugAdapterRegistry Tests ──────────────────────────────────────────

public sealed class DebugAdapterRegistryTests
{
    [Fact]
    public void GetAdapter_ForCoreclr_ShouldReturnBuiltInAdapter()
    {
        var registry = new DebugAdapterRegistry();

        var adapter = registry.GetAdapter("coreclr");

        adapter.Should().NotBeNull();
        adapter!.Type.Should().Be("coreclr");
        adapter.ExecutableName.Should().Be("netcoredbg");
        adapter.Arguments.Should().Contain("--interpreter=vscode");
    }

    [Fact]
    public void GetAdapter_ForUnknown_ShouldReturnNull()
    {
        var registry = new DebugAdapterRegistry();

        var adapter = registry.GetAdapter("unknown");

        adapter.Should().BeNull();
    }

    [Fact]
    public void Register_ShouldAddCustomAdapter()
    {
        var registry = new DebugAdapterRegistry();
        registry.Register(new DebugAdapterInfo
        {
            Type = "python",
            DisplayName = "Python (debugpy)",
            ExecutableName = "debugpy",
            Arguments = ["--listen", "5678"],
            SupportedRuntimes = ["python"],
        });

        var adapter = registry.GetAdapter("python");
        adapter.Should().NotBeNull();
        adapter!.DisplayName.Should().Be("Python (debugpy)");
    }

    [Fact]
    public void GetAllAdapters_ShouldReturnAllRegistered()
    {
        var registry = new DebugAdapterRegistry();

        var adapters = registry.GetAllAdapters();

        adapters.Should().NotBeEmpty();
        adapters.Should().Contain(a => a.Type == "coreclr");
        adapters.Should().Contain(a => a.Type == "java");
        adapters.Should().Contain(a => a.Type == "php");
    }

    [Fact]
    public void GetAdapter_ForJava_ShouldReturnBuiltInAdapter()
    {
        var registry = new DebugAdapterRegistry();

        var adapter = registry.GetAdapter("java");

        adapter.Should().NotBeNull();
        adapter!.Type.Should().Be("java");
        adapter.DisplayName.Should().Contain("Java");
        adapter.ExecutableName.Should().Be("java");
        adapter.SupportedRuntimes.Should().Contain("java");
    }

    [Fact]
    public void GetAdapter_ForPhp_ShouldReturnBuiltInAdapter()
    {
        var registry = new DebugAdapterRegistry();

        var adapter = registry.GetAdapter("php");

        adapter.Should().NotBeNull();
        adapter!.Type.Should().Be("php");
        adapter.DisplayName.Should().Contain("PHP");
        adapter.ExecutableName.Should().Be("node");
        adapter.Arguments.Should().Contain("phpDebug.js");
        adapter.SupportedRuntimes.Should().Contain("php");
    }

    [Fact]
    public void FindAdapterExecutable_WithCustomPath_ShouldReturnIfExists()
    {
        var registry = new DebugAdapterRegistry();
        // Use a file we know exists
        var existingFile = typeof(DebugAdapterRegistryTests).Assembly.Location;

        var result = registry.FindAdapterExecutable("coreclr", existingFile);

        result.Should().Be(existingFile);
    }

    [Fact]
    public void FindAdapterExecutable_WithInvalidCustomPath_ShouldIgnoreIt()
    {
        var registry = new DebugAdapterRegistry();

        // Invalid custom path is ignored; may still find via PATH/tools fallback
        var result = registry.FindAdapterExecutable("coreclr", "/nonexistent/path/to/binary");

        // Don't assert null — netcoredbg may be installed on this machine
        // Just verify the invalid custom path was not returned
        if (result is not null)
            result.Should().NotBe("/nonexistent/path/to/binary");
    }

    [Fact]
    public void FindAdapterExecutable_ForUnknownType_ShouldReturnNull()
    {
        var registry = new DebugAdapterRegistry();

        var result = registry.FindAdapterExecutable("unknown-debugger");

        result.Should().BeNull();
    }
}

// ── Mock DAP Server ──────────────────────────────────────────────────────

/// <summary>
/// A mock DAP server that reads requests from a stream and writes canned responses.
/// Used for testing DapClient without a real debug adapter process.
/// </summary>
internal sealed class MockDapServer
{
    private readonly DapTransport _transport;
    private readonly Queue<CannedResponse> _responseQueue = new();
    private readonly Dictionary<string, List<(string EventName, object? Body)>> _eventsAfterResponse = new();
    private readonly List<string> _receivedCommands = [];
    private CancellationTokenSource? _cts;
    private Task? _runTask;
    private int _nextSeq;

    public IReadOnlyList<string> ReceivedCommands => _receivedCommands;

    public MockDapServer(Stream input, Stream output)
    {
        _transport = new DapTransport(input, output);
    }

    public void EnqueueResponse(string command, object? body)
    {
        _responseQueue.Enqueue(new CannedResponse(command, body, true, null));
    }

    public void EnqueueFailure(string command, string message)
    {
        _responseQueue.Enqueue(new CannedResponse(command, null, false, message));
    }

    /// <summary>
    /// Enqueue an event to be sent automatically after the response to a given command.
    /// </summary>
    public void EnqueueEventAfterResponse(string afterCommand, string eventName, object? body = null)
    {
        if (!_eventsAfterResponse.TryGetValue(afterCommand, out var list))
        {
            list = [];
            _eventsAfterResponse[afterCommand] = list;
        }
        list.Add((eventName, body));
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _runTask = RunAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        try { _runTask?.Wait(TimeSpan.FromSeconds(2)); }
        catch { /* expected */ }
    }

    public void SendEvent(string eventName, object? body)
    {
        var bodyElement = body is not null
            ? JsonSerializer.SerializeToElement(body, DapTransport.JsonOptions)
            : (JsonElement?)null;

        var evt = new DapEvent
        {
            Seq = Interlocked.Increment(ref _nextSeq),
            Event = eventName,
            Body = bodyElement,
        };

        _ = _transport.WriteMessageAsync(evt);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var msg = await _transport.ReadMessageAsync(cancellationToken).ConfigureAwait(false);
                if (msg is null) break;

                if (msg is DapRequest request)
                {
                    _receivedCommands.Add(request.Command);

                    if (_responseQueue.Count > 0)
                    {
                        var canned = _responseQueue.Dequeue();

                        var bodyElement = canned.Body is not null
                            ? JsonSerializer.SerializeToElement(canned.Body, DapTransport.JsonOptions)
                            : (JsonElement?)null;

                        var response = new DapResponse
                        {
                            Seq = Interlocked.Increment(ref _nextSeq),
                            RequestSeq = request.Seq,
                            Success = canned.Success,
                            Command = request.Command,
                            Body = bodyElement,
                            Message = canned.ErrorMessage,
                        };

                        await _transport.WriteMessageAsync(response, cancellationToken).ConfigureAwait(false);

                        // Send any events queued to fire after this command's response
                        if (_eventsAfterResponse.TryGetValue(request.Command, out var events))
                        {
                            foreach (var (evtName, evtBody) in events)
                            {
                                var evtBodyElement = evtBody is not null
                                    ? JsonSerializer.SerializeToElement(evtBody, DapTransport.JsonOptions)
                                    : (JsonElement?)null;

                                var evt = new DapEvent
                                {
                                    Seq = Interlocked.Increment(ref _nextSeq),
                                    Event = evtName,
                                    Body = evtBodyElement,
                                };

                                await _transport.WriteMessageAsync(evt, cancellationToken).ConfigureAwait(false);
                            }
                            _eventsAfterResponse.Remove(request.Command);
                        }
                    }
                }
            }
            catch (OperationCanceledException) { break; }
            catch { if (cancellationToken.IsCancellationRequested) break; }
        }
    }

    private sealed record CannedResponse(string Command, object? Body, bool Success, string? ErrorMessage);
}

/// <summary>
/// A thread-safe in-memory stream that blocks on reads when no data is available.
/// Used as a pipe between DapClient and MockDapServer.
/// </summary>
internal sealed class BlockingMemoryStream : Stream
{
    private readonly Queue<byte[]> _buffers = new();
    private byte[]? _currentBuffer;
    private int _currentOffset;
    private readonly SemaphoreSlim _dataAvailable = new(0);
    private volatile bool _completed;

    public override bool CanRead => true;
    public override bool CanWrite => true;
    public override bool CanSeek => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var data = new byte[count];
        Array.Copy(buffer, offset, data, 0, count);
        lock (_buffers)
        {
            _buffers.Enqueue(data);
        }
        _dataAvailable.Release();
    }

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var data = buffer.ToArray();
        lock (_buffers)
        {
            _buffers.Enqueue(data);
        }
        _dataAvailable.Release();
        await Task.CompletedTask;
    }

    public override async Task FlushAsync(CancellationToken cancellationToken) => await Task.CompletedTask;

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (_currentBuffer is not null && _currentOffset < _currentBuffer.Length)
            {
                var available = _currentBuffer.Length - _currentOffset;
                var toCopy = Math.Min(available, buffer.Length);
                _currentBuffer.AsMemory(_currentOffset, toCopy).CopyTo(buffer);
                _currentOffset += toCopy;
                return toCopy;
            }

            if (_completed) return 0;

            await _dataAvailable.WaitAsync(cancellationToken).ConfigureAwait(false);

            lock (_buffers)
            {
                if (_buffers.Count > 0)
                {
                    _currentBuffer = _buffers.Dequeue();
                    _currentOffset = 0;
                }
            }
        }
    }

    public void Complete()
    {
        _completed = true;
        // Release semaphore to wake any blocked ReadAsync callers
        // so they see _completed = true and return 0
        try { _dataAvailable.Release(); } catch (SemaphoreFullException) { }
    }

    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
