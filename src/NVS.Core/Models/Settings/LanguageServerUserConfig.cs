namespace NVS.Core.Models.Settings;

public sealed record LanguageServerUserConfig
{
    public bool Enabled { get; init; } = true;
    public string? CustomCommand { get; init; }
    public IReadOnlyList<string> CustomArgs { get; init; } = [];

    /// <summary>
    /// When set, overrides the default language server for this server's language(s).
    /// Value is the server ID (e.g. "csharp-ls" or "pylsp").
    /// Stored per-language in AppSettings.PreferredLanguageServers instead.
    /// </summary>
    public string? PreferredServerId { get; init; }
}
