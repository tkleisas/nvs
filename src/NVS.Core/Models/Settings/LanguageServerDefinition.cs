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

    /// <summary>
    /// URL template for GitHubRelease installs. Placeholders: {version}, {rid}, {ext}.
    /// Example: "https://github.com/example/releases/download/{version}/binary-{rid}.{ext}"
    /// </summary>
    public string? DownloadUrlTemplate { get; init; }

    /// <summary>
    /// Pinned version for GitHubRelease installs (e.g. "v1.39.15").
    /// </summary>
    public string? Version { get; init; }

    /// <summary>
    /// When true, the server expects a solution/project path argument (e.g. "-s &lt;path&gt;").
    /// </summary>
    public bool RequiresSolutionArg { get; init; }

    /// <summary>
    /// The argument prefix for passing the solution path (e.g. "-s").
    /// </summary>
    public string? SolutionArgPrefix { get; init; }
}
