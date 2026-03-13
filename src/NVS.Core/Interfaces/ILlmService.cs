using NVS.Core.LLM;

namespace NVS.Core.Interfaces;

public interface ILlmService
{
    /// <summary>Whether the LLM service is configured with a valid endpoint.</summary>
    bool IsConfigured { get; }

    /// <summary>Whether a request is currently in progress.</summary>
    bool IsProcessing { get; }

    /// <summary>
    /// Send a chat completion request with streaming token delivery.
    /// Supports tool calling — if the response contains tool calls, they are returned in the response.
    /// </summary>
    Task<LlmResponse> SendAsync(
        ChatCompletionRequest request,
        Action<string>? onToken = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run the agent loop: send messages, execute tool calls, repeat until done or max iterations.
    /// </summary>
    Task<AgentLoopResult> RunAgentLoopAsync(
        List<ChatCompletionMessage> messages,
        IReadOnlyList<ToolDefinition>? tools = null,
        string? systemPrompt = null,
        Action<string>? onToken = null,
        Action<AgentToolCallEvent>? onToolCall = null,
        int maxIterations = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Cancel any in-progress request.</summary>
    void CancelCurrentRequest();

    event EventHandler? RequestStarted;
    event EventHandler? RequestCompleted;
    event EventHandler<LlmErrorEventArgs>? ErrorOccurred;
}

public sealed record LlmResponse
{
    public required string Content { get; init; }
    public List<ToolCall>? ToolCalls { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required string Model { get; init; }
    public string? FinishReason { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record AgentLoopResult
{
    /// <summary>Final assistant response content.</summary>
    public required string Content { get; init; }

    /// <summary>Total iterations (each tool-call round is one iteration).</summary>
    public required int Iterations { get; init; }

    /// <summary>Total tokens used across all iterations.</summary>
    public required int TotalInputTokens { get; init; }
    public required int TotalOutputTokens { get; init; }

    /// <summary>Whether the loop stopped due to reaching max iterations.</summary>
    public bool HitMaxIterations { get; init; }

    /// <summary>All tool calls made during the agent loop.</summary>
    public List<AgentToolCallEvent> ToolCallHistory { get; init; } = [];
}

/// <summary>Describes a tool call event during the agent loop.</summary>
public sealed record AgentToolCallEvent
{
    public required string ToolName { get; init; }
    public required string Arguments { get; init; }
    public required string Result { get; init; }
    public bool Success { get; init; } = true;
    public TimeSpan Duration { get; init; }
}

public sealed class LlmErrorEventArgs : EventArgs
{
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}
