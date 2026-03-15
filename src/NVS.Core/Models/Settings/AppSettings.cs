namespace NVS.Core.Models.Settings;

public sealed record AppSettings
{
    public string Theme { get; init; } = "NVS Dark";
    public string KeybindingPreset { get; init; } = "vscode";
    public string Locale { get; init; } = "en-US";
    public bool CheckUpdatesOnStartup { get; init; } = false;
    public bool RestorePreviousSession { get; init; } = true;
    public string? LastWorkspacePath { get; init; }
    public EditorSettings Editor { get; init; } = new();
    public TerminalSettings Terminal { get; init; } = new();
    public WindowSettings Window { get; init; } = new();
    public DockLayoutSettings Dock { get; init; } = new();
    public LlmSettings Llm { get; init; } = new();
    public Dictionary<string, LanguageServerUserConfig> LanguageServers { get; init; } = new();

    /// <summary>
    /// Maps language name (e.g. "CSharp") to preferred server ID (e.g. "csharp-ls").
    /// When not set for a language, the registry default is used.
    /// </summary>
    public Dictionary<string, string> PreferredLanguageServers { get; init; } = new();

    public Dictionary<string, object> Properties { get; init; } = new();
}
