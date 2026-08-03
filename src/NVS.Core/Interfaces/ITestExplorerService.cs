using NVS.Core.Models;

namespace NVS.Core.Interfaces;

/// <summary>
/// Discovers and runs .NET tests via the dotnet CLI and parses VSTest results.
/// </summary>
public interface ITestExplorerService
{
    /// <summary>Whether a discovery or run operation is currently in progress.</summary>
    bool IsBusy { get; }

    /// <summary>Discovers tests in a project via <c>dotnet test --list-tests</c>.</summary>
    Task<IReadOnlyList<TestInfo>> DiscoverTestsAsync(string projectPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs tests for the given solution or project path, optionally narrowed by a
    /// VSTest <c>--filter</c> expression, and returns the parsed TRX results.
    /// </summary>
    Task<TestRunSummary> RunTestsAsync(string targetPath, string? filter = null, CancellationToken cancellationToken = default);
}
