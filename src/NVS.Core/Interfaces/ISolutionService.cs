using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface ISolutionService
{
    SolutionModel? CurrentSolution { get; }
    bool IsSolutionLoaded { get; }

    Task<SolutionModel> LoadSolutionAsync(string solutionPath, CancellationToken cancellationToken = default);
    Task<ProjectModel> LoadProjectAsync(string projectPath, CancellationToken cancellationToken = default);
    Task CloseSolutionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects the first .sln/.slnx file in the given directory.
    /// Returns null if none found.
    /// </summary>
    Task<string?> DetectSolutionFileAsync(string directoryPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the startup project (first Exe project, or null).
    /// </summary>
    ProjectModel? GetStartupProject();

    event EventHandler<SolutionModel>? SolutionLoaded;
    event EventHandler? SolutionClosed;
}
