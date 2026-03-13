using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using NVS.Core.Interfaces;
using NVS.Core.Models.NuGet;

namespace NVS.Services.NuGet;

/// <summary>
/// NuGet package management service wrapping the dotnet CLI and NuGet.org search API.
/// </summary>
public sealed partial class NuGetPackageService : INuGetService
{
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://azuresearch-usnc.nuget.org/"),
        Timeout = TimeSpan.FromSeconds(15)
    };

    public event EventHandler<PackageOperationResult>? PackageOperationCompleted;

    public async Task<IReadOnlyList<NuGetPackageInfo>> SearchPackagesAsync(
        string query,
        int skip = 0,
        int take = 20,
        bool includePrerelease = false,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return [];

        var prerelease = includePrerelease ? "true" : "false";
        var url = $"query?q={Uri.EscapeDataString(query)}&skip={skip}&take={take}&prerelease={prerelease}&semVerLevel=2.0.0";

        try
        {
            var response = await HttpClient.GetStringAsync(url, cancellationToken);
            return ParseSearchResponse(response);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    public async Task<IReadOnlyList<InstalledPackage>> GetInstalledPackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunDotnetAsync(
            $"list \"{projectPath}\" package --format json",
            Path.GetDirectoryName(projectPath)!,
            cancellationToken);

        if (exitCode != 0)
        {
            // Fallback: parse csproj directly
            return ParseCsprojPackages(projectPath);
        }

        return ParseListPackageJson(stdout);
    }

    public async Task<IReadOnlyList<InstalledPackage>> GetOutdatedPackagesAsync(
        string projectPath,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, stdout, _) = await RunDotnetAsync(
            $"list \"{projectPath}\" package --outdated --format json",
            Path.GetDirectoryName(projectPath)!,
            cancellationToken);

        if (exitCode != 0)
            return [];

        return ParseListPackageJson(stdout, outdated: true);
    }

    public async Task<PackageOperationResult> AddPackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        var versionArg = version is not null ? $" --version {version}" : "";
        var (exitCode, stdout, stderr) = await RunDotnetAsync(
            $"add \"{projectPath}\" package {packageId}{versionArg}",
            Path.GetDirectoryName(projectPath)!,
            cancellationToken);

        var result = new PackageOperationResult
        {
            Success = exitCode == 0,
            PackageId = packageId,
            Version = version,
            Message = exitCode == 0 ? $"Added {packageId}" : $"Failed to add {packageId}",
            ErrorOutput = exitCode != 0 ? stderr : null
        };

        PackageOperationCompleted?.Invoke(this, result);
        return result;
    }

    public async Task<PackageOperationResult> RemovePackageAsync(
        string projectPath,
        string packageId,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunDotnetAsync(
            $"remove \"{projectPath}\" package {packageId}",
            Path.GetDirectoryName(projectPath)!,
            cancellationToken);

        var result = new PackageOperationResult
        {
            Success = exitCode == 0,
            PackageId = packageId,
            Message = exitCode == 0 ? $"Removed {packageId}" : $"Failed to remove {packageId}",
            ErrorOutput = exitCode != 0 ? stderr : null
        };

        PackageOperationCompleted?.Invoke(this, result);
        return result;
    }

    public async Task<PackageOperationResult> UpdatePackageAsync(
        string projectPath,
        string packageId,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        // dotnet doesn't have a dedicated update command — remove then add
        // Or use add with specific version which updates the reference
        var versionArg = version is not null ? $" --version {version}" : "";
        var (exitCode, _, stderr) = await RunDotnetAsync(
            $"add \"{projectPath}\" package {packageId}{versionArg}",
            Path.GetDirectoryName(projectPath)!,
            cancellationToken);

        var result = new PackageOperationResult
        {
            Success = exitCode == 0,
            PackageId = packageId,
            Version = version,
            Message = exitCode == 0 ? $"Updated {packageId}" : $"Failed to update {packageId}",
            ErrorOutput = exitCode != 0 ? stderr : null
        };

        PackageOperationCompleted?.Invoke(this, result);
        return result;
    }

    public async Task<PackageOperationResult> RestoreAsync(
        string projectOrSolutionPath,
        CancellationToken cancellationToken = default)
    {
        var (exitCode, _, stderr) = await RunDotnetAsync(
            $"restore \"{projectOrSolutionPath}\"",
            Path.GetDirectoryName(projectOrSolutionPath)!,
            cancellationToken);

        var result = new PackageOperationResult
        {
            Success = exitCode == 0,
            PackageId = "restore",
            Message = exitCode == 0 ? "Restore completed" : "Restore failed",
            ErrorOutput = exitCode != 0 ? stderr : null
        };

        PackageOperationCompleted?.Invoke(this, result);
        return result;
    }

    // --- dotnet CLI execution ---

    internal static async Task<(int exitCode, string stdout, string stderr)> RunDotnetAsync(
        string arguments,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken);

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    // --- NuGet.org search response parsing ---

    internal static IReadOnlyList<NuGetPackageInfo> ParseSearchResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data");
            var packages = new List<NuGetPackageInfo>();

            foreach (var item in data.EnumerateArray())
            {
                var versions = new List<string>();
                if (item.TryGetProperty("versions", out var versionsArray))
                {
                    foreach (var v in versionsArray.EnumerateArray())
                    {
                        if (v.TryGetProperty("version", out var ver))
                            versions.Add(ver.GetString() ?? "");
                    }
                }

                packages.Add(new NuGetPackageInfo
                {
                    Id = item.GetProperty("id").GetString() ?? "",
                    Version = item.TryGetProperty("version", out var version)
                        ? version.GetString() ?? "" : "",
                    Description = item.TryGetProperty("description", out var desc)
                        ? desc.GetString() ?? "" : "",
                    Authors = item.TryGetProperty("authors", out var authors)
                        ? string.Join(", ", authors.EnumerateArray().Select(a => a.GetString())) : "",
                    IconUrl = item.TryGetProperty("iconUrl", out var icon) ? icon.GetString() : null,
                    ProjectUrl = item.TryGetProperty("projectUrl", out var projUrl) ? projUrl.GetString() : null,
                    LicenseUrl = item.TryGetProperty("licenseUrl", out var licUrl) ? licUrl.GetString() : null,
                    TotalDownloads = item.TryGetProperty("totalDownloads", out var dl) ? dl.GetInt64() : 0,
                    IsVerified = item.TryGetProperty("verified", out var verified) && verified.GetBoolean(),
                    Tags = item.TryGetProperty("tags", out var tags)
                        ? tags.EnumerateArray().Select(t => t.GetString() ?? "").ToList() : [],
                    Versions = versions
                });
            }

            return packages;
        }
        catch
        {
            return [];
        }
    }

    // --- dotnet list package --format json parsing ---

    internal static IReadOnlyList<InstalledPackage> ParseListPackageJson(string json, bool outdated = false)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var packages = new List<InstalledPackage>();

            if (!doc.RootElement.TryGetProperty("projects", out var projects))
                return packages;

            foreach (var project in projects.EnumerateArray())
            {
                if (!project.TryGetProperty("frameworks", out var frameworks))
                    continue;

                foreach (var framework in frameworks.EnumerateArray())
                {
                    var propName = outdated ? "topLevelPackages" : "topLevelPackages";
                    if (!framework.TryGetProperty(propName, out var pkgs))
                        continue;

                    foreach (var pkg in pkgs.EnumerateArray())
                    {
                        var id = pkg.GetProperty("id").GetString() ?? "";
                        var requested = pkg.TryGetProperty("requestedVersion", out var rv) ? rv.GetString() ?? "" : "";
                        var resolved = pkg.TryGetProperty("resolvedVersion", out var rsv) ? rsv.GetString() : null;
                        var latest = pkg.TryGetProperty("latestVersion", out var lv) ? lv.GetString() : null;

                        // Avoid duplicate entries from multiple frameworks
                        if (packages.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        packages.Add(new InstalledPackage
                        {
                            Id = id,
                            RequestedVersion = requested,
                            ResolvedVersion = resolved,
                            LatestVersion = latest
                        });
                    }
                }
            }

            return packages;
        }
        catch
        {
            return [];
        }
    }

    // --- Fallback: parse csproj directly ---

    internal static IReadOnlyList<InstalledPackage> ParseCsprojPackages(string csprojPath)
    {
        try
        {
            if (!File.Exists(csprojPath))
                return [];

            var content = File.ReadAllText(csprojPath);
            var packages = new List<InstalledPackage>();

            foreach (Match match in PackageReferenceRegex().Matches(content))
            {
                var id = match.Groups["id"].Value;
                var version = match.Groups["version"].Value;
                if (!string.IsNullOrWhiteSpace(id))
                {
                    packages.Add(new InstalledPackage
                    {
                        Id = id,
                        RequestedVersion = version
                    });
                }
            }

            return packages;
        }
        catch
        {
            return [];
        }
    }

    [GeneratedRegex(
        @"<PackageReference\s+Include=""(?<id>[^""]+)""\s+Version=""(?<version>[^""]*)""",
        RegexOptions.IgnoreCase)]
    private static partial Regex PackageReferenceRegex();
}
