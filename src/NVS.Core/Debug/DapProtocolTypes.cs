using System.Text.Json.Serialization;

namespace NVS.Core.Debug;

// ── Initialize ──────────────────────────────────────────────────────

public sealed record DapInitializeRequestArguments
{
    [JsonPropertyName("clientID")]
    public string? ClientId { get; init; }

    [JsonPropertyName("clientName")]
    public string? ClientName { get; init; }

    [JsonPropertyName("adapterID")]
    public required string AdapterId { get; init; }

    [JsonPropertyName("linesStartAt1")]
    public bool LinesStartAt1 { get; init; } = true;

    [JsonPropertyName("columnsStartAt1")]
    public bool ColumnsStartAt1 { get; init; } = true;

    [JsonPropertyName("pathFormat")]
    public string PathFormat { get; init; } = "path";

    [JsonPropertyName("supportsVariableType")]
    public bool SupportsVariableType { get; init; } = true;

    [JsonPropertyName("supportsRunInTerminalRequest")]
    public bool SupportsRunInTerminalRequest { get; init; }
}

public sealed record DapCapabilities
{
    [JsonPropertyName("supportsConfigurationDoneRequest")]
    public bool SupportsConfigurationDoneRequest { get; init; }

    [JsonPropertyName("supportsFunctionBreakpoints")]
    public bool SupportsFunctionBreakpoints { get; init; }

    [JsonPropertyName("supportsConditionalBreakpoints")]
    public bool SupportsConditionalBreakpoints { get; init; }

    [JsonPropertyName("supportsEvaluateForHovers")]
    public bool SupportsEvaluateForHovers { get; init; }

    [JsonPropertyName("supportsSetVariable")]
    public bool SupportsSetVariable { get; init; }

    [JsonPropertyName("supportsTerminateRequest")]
    public bool SupportsTerminateRequest { get; init; }
}

// ── Launch / Attach ─────────────────────────────────────────────────

public sealed record DapLaunchRequestArguments
{
    [JsonPropertyName("program")]
    public required string Program { get; init; }

    [JsonPropertyName("args")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Args { get; init; }

    [JsonPropertyName("cwd")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Cwd { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Env { get; init; }

    [JsonPropertyName("stopAtEntry")]
    public bool StopAtEntry { get; init; }

    [JsonPropertyName("noDebug")]
    public bool NoDebug { get; init; }

    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("console")]
    public string? Console { get; init; }
}

// ── RunInTerminal (reverse request from adapter) ────────────────────

public sealed record DapRunInTerminalArguments
{
    [JsonPropertyName("kind")]
    public string? Kind { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("cwd")]
    public string? Cwd { get; init; }

    [JsonPropertyName("args")]
    public IReadOnlyList<string>? Args { get; init; }

    [JsonPropertyName("env")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string?>? Env { get; init; }
}

public sealed record DapRunInTerminalResponseBody
{
    [JsonPropertyName("processId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProcessId { get; init; }

    [JsonPropertyName("shellProcessId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ShellProcessId { get; init; }
}

// ── Breakpoints ─────────────────────────────────────────────────────

public sealed record DapSourceBreakpoint
{
    [JsonPropertyName("line")]
    public required int Line { get; init; }

    [JsonPropertyName("column")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Column { get; init; }

    [JsonPropertyName("condition")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Condition { get; init; }
}

public sealed record DapSetBreakpointsArguments
{
    [JsonPropertyName("source")]
    public required DapSource Source { get; init; }

    [JsonPropertyName("breakpoints")]
    public required IReadOnlyList<DapSourceBreakpoint> Breakpoints { get; init; }
}

public sealed record DapSetBreakpointsResponseBody
{
    [JsonPropertyName("breakpoints")]
    public required IReadOnlyList<DapBreakpoint> Breakpoints { get; init; }
}

public sealed record DapBreakpoint
{
    [JsonPropertyName("id")]
    public int? Id { get; init; }

    [JsonPropertyName("verified")]
    public bool Verified { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }

    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DapSource? Source { get; init; }
}

// ── Stack Trace ─────────────────────────────────────────────────────

public sealed record DapStackTraceArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }

    [JsonPropertyName("startFrame")]
    public int? StartFrame { get; init; }

    [JsonPropertyName("levels")]
    public int? Levels { get; init; }
}

public sealed record DapStackTraceResponseBody
{
    [JsonPropertyName("stackFrames")]
    public required IReadOnlyList<DapStackFrame> StackFrames { get; init; }

    [JsonPropertyName("totalFrames")]
    public int? TotalFrames { get; init; }
}

public sealed record DapStackFrame
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DapSource? Source { get; init; }

    [JsonPropertyName("line")]
    public int Line { get; init; }

    [JsonPropertyName("column")]
    public int Column { get; init; }
}

// ── Scopes & Variables ──────────────────────────────────────────────

public sealed record DapScopesArguments
{
    [JsonPropertyName("frameId")]
    public required int FrameId { get; init; }
}

public sealed record DapScopesResponseBody
{
    [JsonPropertyName("scopes")]
    public required IReadOnlyList<DapScope> Scopes { get; init; }
}

public sealed record DapScope
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("variablesReference")]
    public required int VariablesReference { get; init; }

    [JsonPropertyName("expensive")]
    public bool Expensive { get; init; }
}

public sealed record DapVariablesArguments
{
    [JsonPropertyName("variablesReference")]
    public required int VariablesReference { get; init; }
}

public sealed record DapVariablesResponseBody
{
    [JsonPropertyName("variables")]
    public required IReadOnlyList<DapVariable> Variables { get; init; }
}

public sealed record DapVariable
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("value")]
    public required string Value { get; init; }

    [JsonPropertyName("type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Type { get; init; }

    [JsonPropertyName("variablesReference")]
    public int VariablesReference { get; init; }
}

// ── Threads ─────────────────────────────────────────────────────────

public sealed record DapThreadsResponseBody
{
    [JsonPropertyName("threads")]
    public required IReadOnlyList<DapThread> Threads { get; init; }
}

public sealed record DapThread
{
    [JsonPropertyName("id")]
    public required int Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }
}

// ── Common Types ────────────────────────────────────────────────────

public sealed record DapSource
{
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }

    [JsonPropertyName("path")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
}

// ── Continue / Step ─────────────────────────────────────────────────

public sealed record DapContinueArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapNextArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapStepInArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapStepOutArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapPauseArguments
{
    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapDisconnectArguments
{
    [JsonPropertyName("restart")]
    public bool Restart { get; init; }

    [JsonPropertyName("terminateDebuggee")]
    public bool TerminateDebuggee { get; init; } = true;
}

// ── Event Bodies ────────────────────────────────────────────────────

public sealed record DapStoppedEventBody
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("threadId")]
    public int? ThreadId { get; init; }

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("allThreadsStopped")]
    public bool AllThreadsStopped { get; init; }
}

public sealed record DapTerminatedEventBody
{
    [JsonPropertyName("restart")]
    public bool Restart { get; init; }
}

public sealed record DapOutputEventBody
{
    [JsonPropertyName("category")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; init; }

    [JsonPropertyName("output")]
    public required string Output { get; init; }

    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DapSource? Source { get; init; }

    [JsonPropertyName("line")]
    public int? Line { get; init; }
}

public sealed record DapThreadEventBody
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("threadId")]
    public required int ThreadId { get; init; }
}

public sealed record DapBreakpointEventBody
{
    [JsonPropertyName("reason")]
    public required string Reason { get; init; }

    [JsonPropertyName("breakpoint")]
    public required DapBreakpoint Breakpoint { get; init; }
}
