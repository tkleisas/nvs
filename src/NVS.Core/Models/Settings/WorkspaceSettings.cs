namespace NVS.Core.Models.Settings;

public sealed record WorkspaceSettings
{
    public IReadOnlyList<string> Folders { get; init; } = [];
    public IReadOnlyDictionary<string, LanguageServerConfig> LanguageServers { get; init; } = new Dictionary<string, LanguageServerConfig>();
    public IReadOnlyList<DebugConfiguration> DebugConfigurations { get; init; } = [];
    public IReadOnlyList<BuildTask> BuildTasks { get; init; } = [];
}
