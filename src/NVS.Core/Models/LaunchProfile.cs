namespace NVS.Core.Models;

/// <summary>
/// A launch profile describing how a (web) application should be started
/// for run/debug. Populated from launchSettings.json for .NET projects;
/// other languages may map their own launch surface to the same shape.
/// </summary>
public sealed record LaunchProfile
{
    /// <summary>Profile name as it appears in launchSettings.json.</summary>
    public required string Name { get; init; }

    /// <summary>The command kind (e.g. "Project", "Executable", "IIS").</summary>
    public string? CommandName { get; init; }

    /// <summary>Semicolon-delimited application URLs, e.g. "https://localhost:5001;http://localhost:5000".</summary>
    public string? ApplicationUrl { get; init; }

    /// <summary>Relative path within the site to open the browser at, e.g. "/swagger".</summary>
    public string? LaunchUrl { get; init; }

    /// <summary>Whether to launch a browser automatically.</summary>
    public bool LaunchBrowser { get; init; }

    /// <summary>Environment variables to set for the launched process.</summary>
    public IReadOnlyDictionary<string, string> EnvironmentVariables { get; init; } = new Dictionary<string, string>();

    /// <summary>Extra command-line arguments passed to the application.</summary>
    public string? CommandLineArgs { get; init; }

    /// <summary>Working directory override (rare for .NET; defaults to project dir).</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>True when this profile launches the project itself via <c>dotnet run</c> / DAP launch
    /// (commandName is empty or "Project"). False for IISExpress/Executable/Docker profiles.</summary>
    public bool IsProjectLaunch => string.IsNullOrWhiteSpace(CommandName)
                                || string.Equals(CommandName, "Project", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolved first HTTP(S) URL derived from <see cref="ApplicationUrl"/>; null if not present.</summary>
    public string? FirstApplicationUrl => ParseFirstUrl(ApplicationUrl);

    private static string? ParseFirstUrl(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return null;
        var first = urls.Trim().Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        return string.IsNullOrEmpty(first) ? null : first;
    }
}