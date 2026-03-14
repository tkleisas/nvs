namespace NVS.Core.Interfaces;

/// <summary>
/// Calculates code complexity metrics for C# source files and projects.
/// Uses Roslyn syntax/semantic analysis for accurate measurements.
/// </summary>
public interface ICodeMetricsService
{
    /// <summary>Calculate metrics for a single C# source file.</summary>
    Task<FileMetrics> CalculateFileMetricsAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Calculate metrics for all C# files in a project.</summary>
    Task<ProjectMetrics> CalculateProjectMetricsAsync(string projectPath, CancellationToken cancellationToken = default);
}

// ─── Metric Models ──────────────────────────────────────────────────────────

public sealed record ProjectMetrics
{
    public required string ProjectName { get; init; }
    public required string ProjectPath { get; init; }
    public IReadOnlyList<FileMetrics> Files { get; init; } = [];
    public LineMetrics TotalLines { get; init; } = new();
    public int TotalTypes { get; init; }
    public int TotalMethods { get; init; }
    public double AverageCyclomaticComplexity { get; init; }
    public double AverageMaintainabilityIndex { get; init; }
}

public sealed record FileMetrics
{
    public required string FilePath { get; init; }
    public required string FileName { get; init; }
    public LineMetrics Lines { get; init; } = new();
    public IReadOnlyList<TypeMetrics> Types { get; init; } = [];
}

public sealed record TypeMetrics
{
    public required string Name { get; init; }
    public required string FullName { get; init; }
    public TypeKindMetric Kind { get; init; }
    public int Line { get; init; }
    public int DepthOfInheritance { get; init; }
    public int ClassCoupling { get; init; }
    public IReadOnlyList<MethodMetrics> Methods { get; init; } = [];
}

public sealed record MethodMetrics
{
    public required string Name { get; init; }
    public int Line { get; init; }
    public int CyclomaticComplexity { get; init; }
    public LineMetrics Lines { get; init; } = new();
    public double MaintainabilityIndex { get; init; }
}

public sealed record LineMetrics
{
    public int Total { get; init; }
    public int Blank { get; init; }
    public int Comment { get; init; }
    public int Executable { get; init; }
}

public enum TypeKindMetric
{
    Class,
    Struct,
    Interface,
    Enum,
    Record,
}
