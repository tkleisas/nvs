using System.Reflection;
using System.Runtime.InteropServices;

namespace NVS;

/// <summary>
/// Provides version and system information for the application.
/// </summary>
public static class AppVersionInfo
{
    private static readonly Assembly AppAssembly = typeof(AppVersionInfo).Assembly;

    /// <summary>
    /// Semantic version (e.g., "0.0.1").
    /// </summary>
    public static string Version { get; } =
        AppAssembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Full informational version including git hash (e.g., "0.0.1+abc1234").
    /// </summary>
    public static string InformationalVersion { get; } =
        AppAssembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? Version;

    /// <summary>
    /// Short git commit hash (e.g., "abc1234").
    /// </summary>
    public static string GitHash { get; } = ParseGitHash(InformationalVersion);

    /// <summary>
    /// .NET runtime version (e.g., ".NET 10.0.3").
    /// </summary>
    public static string RuntimeVersion { get; } =
        $".NET {RuntimeInformation.FrameworkDescription.Replace(".NET ", "")}";

    /// <summary>
    /// Operating system description (e.g., "Windows 11 (10.0.26100)").
    /// </summary>
    public static string OsDescription { get; } =
        RuntimeInformation.OSDescription;

    /// <summary>
    /// Process architecture (e.g., "X64", "Arm64").
    /// </summary>
    public static string Architecture { get; } =
        RuntimeInformation.ProcessArchitecture.ToString();

    /// <summary>
    /// Display string for About dialog (e.g., "0.0.1+abc1234").
    /// </summary>
    public static string DisplayVersion => InformationalVersion;

    private static string ParseGitHash(string informationalVersion)
    {
        var plusIndex = informationalVersion.IndexOf('+');
        return plusIndex >= 0 ? informationalVersion[(plusIndex + 1)..] : "unknown";
    }
}
