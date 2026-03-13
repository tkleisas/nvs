namespace NVS.Core.Models.NuGet;

/// <summary>A NuGet package from search results or package metadata.</summary>
public sealed record NuGetPackageInfo
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public string Description { get; init; } = string.Empty;
    public string Authors { get; init; } = string.Empty;
    public string? IconUrl { get; init; }
    public string? ProjectUrl { get; init; }
    public string? LicenseUrl { get; init; }
    public long TotalDownloads { get; init; }
    public bool IsVerified { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> Versions { get; init; } = [];
}

/// <summary>A package installed in a specific project.</summary>
public sealed record InstalledPackage
{
    public required string Id { get; init; }
    public required string RequestedVersion { get; init; }
    public string? ResolvedVersion { get; init; }
    public string? LatestVersion { get; init; }
    public bool HasUpdate => LatestVersion is not null
        && !string.Equals(ResolvedVersion ?? RequestedVersion, LatestVersion, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Result of a package operation (install/remove/update).</summary>
public sealed record PackageOperationResult
{
    public required bool Success { get; init; }
    public required string PackageId { get; init; }
    public string? Version { get; init; }
    public string? Message { get; init; }
    public string? ErrorOutput { get; init; }
}
