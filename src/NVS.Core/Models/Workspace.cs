namespace NVS.Core.Models;

public sealed record Workspace
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string RootPath { get; init; }
    public IReadOnlyList<string> Folders { get; init; } = [];
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastOpenedAt { get; set; } = DateTimeOffset.UtcNow;
}
