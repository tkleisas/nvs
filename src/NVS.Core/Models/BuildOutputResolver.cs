using NVS.Core.Models.Settings;

namespace NVS.Core.Models;

/// <summary>
/// The single rule every build/run/debug/test flow consults to decide where build
/// output goes: standard per-project <c>bin</c> layout, an auto shadow directory
/// when self-hosting, or a user-configured custom directory. Centralizing this
/// keeps "self-hosted" from being a special case scattered across the IDE.
/// </summary>
public static class BuildOutputResolver
{
    /// <summary>
    /// Resolves the effective output directory for builds of the given solution,
    /// or <c>null</c> for the standard per-project <c>bin</c> layout.
    /// </summary>
    public static string? ResolveOutputDirectory(string solutionPath, BuildSettings settings)
    {
        return settings.OutputMode switch
        {
            BuildOutputMode.Default => null,
            BuildOutputMode.Custom => ResolveCustom(solutionPath, settings),
            _ => SelfHostHelper.IsSelfHosted(solutionPath)
                ? SelfHostHelper.GetShadowDirectory(solutionPath)
                : null,
        };
    }

    /// <summary>
    /// The MSBuild argument redirecting output (<c>-p:OutDir=...</c>), or <c>null</c>
    /// when building to standard locations.
    /// </summary>
    public static string? ResolveOutDirArgument(string solutionPath, BuildSettings settings)
    {
        var directory = ResolveOutputDirectory(solutionPath, settings);
        return directory is null
            ? null
            : $"-p:OutDir={directory}{Path.DirectorySeparatorChar}";
    }

    private static string ResolveCustom(string solutionPath, BuildSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CustomOutputDirectory))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.CustomOutputDirectory));
        }

        // Custom mode without a path: still redirected, to a stable temp location.
        return SelfHostHelper.GetShadowDirectory(solutionPath);
    }
}
