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

    /// <summary>
    /// Creates a new solution file using <c>dotnet new sln</c>.
    /// </summary>
    /// <param name="solutionName">The solution name (without extension).</param>
    /// <param name="directory">The directory where the .sln will be created.</param>
    /// <returns>The full path to the created .sln file.</returns>
    Task<string> CreateSolutionAsync(string solutionName, string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a project to an existing solution using <c>dotnet sln add</c>.
    /// </summary>
    /// <param name="solutionPath">Full path to the .sln file.</param>
    /// <param name="projectPath">Full path to the .csproj file to add.</param>
    Task AddProjectToSolutionAsync(string solutionPath, string projectPath, CancellationToken cancellationToken = default);

    event EventHandler<SolutionModel>? SolutionLoaded;
    event EventHandler? SolutionClosed;
}
