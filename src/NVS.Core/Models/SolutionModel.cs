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
                             || string.Equals(OutputType, "WinExe", StringComparison.OrdinalIgnoreCase);
    public IReadOnlyList<string> PackageReferences { get; init; } = [];
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
}
