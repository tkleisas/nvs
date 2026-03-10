namespace NVS.Core.Models;

public sealed record Project
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required string Type { get; init; }
    public Guid WorkspaceId { get; init; }
    public IReadOnlyList<string> SourceFolders { get; init; } = [];
    public IReadOnlyDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}
