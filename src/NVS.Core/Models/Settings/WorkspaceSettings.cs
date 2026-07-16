namespace NVS.Core.Models.Settings;

public sealed record WorkspaceSettings
{
    public IReadOnlyList<string> Folders { get; init; } = [];
    public IReadOnlyDictionary<string, LanguageServerConfig> LanguageServers { get; init; } = new Dictionary<string, LanguageServerConfig>();
    public IReadOnlyList<DebugConfiguration> DebugConfigurations { get; init; } = [];
    public IReadOnlyList<BuildTask> BuildTasks { get; init; } = [];

    /// <summary>
    /// Per-project preferred launch profile name (for web app run/debug).
    /// Keyed by project name. Persisted so the toolbar selection survives restarts.
    /// </summary>
    public IReadOnlyDictionary<string, string> LaunchProfilePreferences { get; init; } = new Dictionary<string, string>();
}
