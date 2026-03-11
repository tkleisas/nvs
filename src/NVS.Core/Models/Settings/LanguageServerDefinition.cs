using NVS.Core.Enums;

namespace NVS.Core.Models.Settings;

public sealed record LanguageServerDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required string License { get; init; }
    public required Language[] Languages { get; init; }
    public required string BinaryName { get; init; }
    public IReadOnlyList<string> DefaultArgs { get; init; } = [];
    public required InstallMethod InstallMethod { get; init; }
    public string? InstallCommand { get; init; }
    public string? InstallPackage { get; init; }
    public string? HomepageUrl { get; init; }
}
