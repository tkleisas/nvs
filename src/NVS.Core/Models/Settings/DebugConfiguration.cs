namespace NVS.Core.Models.Settings;

public sealed record DebugConfiguration
{
    public required string Name { get; init; }
    public required string Type { get; init; }
    public required string Request { get; init; }
    public string? Program { get; init; }
    public string? Cwd { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? Console { get; init; }
    public IReadOnlyDictionary<string, object> AdditionalProperties { get; init; } = new Dictionary<string, object>();
}
