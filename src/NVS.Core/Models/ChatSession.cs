namespace NVS.Core.Models;

/// <summary>A persisted chat session with its metadata.</summary>
public sealed record ChatSession
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string TaskMode { get; init; } = "general";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>A persisted chat message within a session.</summary>
public sealed record ChatMessageRecord
{
    public long Id { get; init; }
    public required string SessionId { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}
