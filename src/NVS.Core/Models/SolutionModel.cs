using System.IO;

namespace NVS.Core.Models;

public sealed record SolutionModel
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required SolutionFormat Format { get; init; }
    public IReadOnlyList<ProjectReference> Projects { get; init; } = [];
    public string? StartupProjectPath { get; init; }
}

public enum SolutionFormat
{
    Sln,
    Slnx
}

public sealed record ProjectReference
{
    public required string Name { get; init; }
    public required string RelativePath { get; init; }
    public required Guid ProjectGuid { get; init; }
    public Guid TypeGuid { get; init; }
}

public sealed record ProjectModel
{
    public required string FilePath { get; init; }
    public required string Name { get; init; }
    public required string Sdk { get; init; }
    public required string TargetFramework { get; init; }
    public string? OutputType { get; init; }
    public string? RootNamespace { get; init; }
    public string? AssemblyName { get; init; }
    public bool IsExecutable => string.Equals(OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
                             || string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase)
                             || IsWebProject;

    /// <summary>
    /// True when the project targets the ASP.NET Core web SDK and is therefore a
    /// web application (Kestrel/IIS Express launch surface, launchSettings.json).
    /// Covers core SDKs such as Microsoft.NET.Sdk.Web and worker/Blazor flavors.
    /// </summary>
    public bool IsWebProject => IsWebSdk(Sdk);

    private static bool IsWebSdk(string? sdk)
    {
        if (string.IsNullOrWhiteSpace(sdk)) return false;
        return string.Equals(sdk.Trim(), "Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)
            || (sdk.StartsWith("Microsoft.NET.Sdk", StringComparison.OrdinalIgnoreCase)
                && (sdk.Contains(".Web", StringComparison.OrdinalIgnoreCase)
                    || sdk.Contains(".Functions", StringComparison.OrdinalIgnoreCase)
                    || sdk.Contains(".Blazor", StringComparison.OrdinalIgnoreCase)
                    || sdk.Contains(".Worker", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Directory containing the project file — used to locate launchSettings.json.
    /// </summary>
    public string ProjectDirectory => Path.GetDirectoryName(FilePath) ?? string.Empty;

    public IReadOnlyList<string> PackageReferences { get; init; } = [];
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
}
