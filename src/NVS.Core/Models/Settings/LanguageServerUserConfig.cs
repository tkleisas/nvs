namespace NVS.Core.Models.Settings;

public sealed record LanguageServerUserConfig
{
    public bool Enabled { get; init; } = true;
    public string? CustomCommand { get; init; }
    public IReadOnlyList<string> CustomArgs { get; init; } = [];
}
