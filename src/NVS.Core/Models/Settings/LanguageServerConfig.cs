namespace NVS.Core.Models.Settings;

public sealed record LanguageServerConfig
{
    public required string Command { get; init; }
    public IReadOnlyList<string> Args { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
    public string? Cwd { get; init; }
}
