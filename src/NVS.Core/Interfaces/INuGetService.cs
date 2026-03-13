using NVS.Core.Models.NuGet;

namespace NVS.Core.Interfaces;

public interface INuGetService
{
    /// <summary>Search nuget.org for packages matching the query.</summary>
    Task<IReadOnlyList<NuGetPackageInfo>> SearchPackagesAsync(
        string query,
        int skip = 0,
        int take = 20,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default);

    /// <summary>List installed packages for a project (.csproj path).</summary>
    Task<IReadOnlyList<InstalledPackage>> GetInstalledPackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>List installed packages with available updates.</summary>
    Task<IReadOnlyList<InstalledPackage>> GetOutdatedPackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default);

    /// <summary>Add a package to a project.</summary>
    Task<PackageOperationResult> AddPackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>Remove a package from a project.</summary>
    Task<PackageOperationResult> RemovePackageAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default);

    /// <summary>Update a package to a specific or latest version.</summary>
    Task<PackageOperationResult> UpdatePackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>Restore packages for a project or solution.</summary>
    Task<PackageOperationResult> RestoreAsync(
        string projectOrSolutionPath,
        CancellationToken cancellationToken = default);

    event EventHandler<PackageOperationResult>? PackageOperationCompleted;
}
