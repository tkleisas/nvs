namespace NVS.Core.Models.Settings;

public sealed record BuildTask
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public string? Cwd { get; init; }
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? ProblemMatcher { get; init; }
    public bool IsDefault { get; init; }
}
