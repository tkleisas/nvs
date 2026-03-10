namespace NVS.Core.Interfaces;

public interface ILlmService
{
    bool IsEnabled { get; }
    bool IsProcessing { get; }
    
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LlmResponseChunk> StreamCompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
    Task<LlmResponse> ChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
    IAsyncEnumerable<LlmResponseChunk> StreamChatAsync(IReadOnlyList<ChatMessage> messages, CancellationToken cancellationToken = default);
    
    void CancelCurrentRequest();
    
    event EventHandler? RequestStarted;
    event EventHandler? RequestCompleted;
    event EventHandler<LlmErrorEventArgs>? ErrorOccurred;
}

public sealed record LlmRequest
{
    public required string Prompt { get; init; }
    public int MaxTokens { get; init; } = 2048;
    public double Temperature { get; init; } = 0.7;
    public double TopP { get; init; } = 1.0;
    public IReadOnlyList<string>? StopSequences { get; init; }
}

public sealed record LlmResponse
{
    public required string Content { get; init; }
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
    public required string Model { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

public sealed record LlmResponseChunk
{
    public required string Content { get; init; }
    public bool IsComplete { get; init; }
}

public sealed record ChatMessage
{
    public required ChatRole Role { get; init; }
    public required string Content { get; init; }
}

public enum ChatRole
{
    System,
    User,
    Assistant
}

public sealed class LlmErrorEventArgs : EventArgs
{
    public required string Message { get; init; }
    public Exception? Exception { get; init; }
}
