using NVS.Core.Models;

namespace NVS.Core.Interfaces;

/// <summary>
/// Provides launch-profile resolution for run/debug of applications.
/// The default implementation reads .NET launchSettings.json but the contract
/// is language-agnostic so providers for other runtimes can be added later.
/// </summary>
public interface ILaunchSettingsService
{
    /// <summary>
    /// Returns all launch profiles discovered for the given project, keyed by
    /// profile name. Returns an empty collection when no profiles are defined
    /// (e.g. missing launchSettings.json).
    /// </summary>
    IReadOnlyList<LaunchProfile> GetLaunchProfiles(ProjectModel project);

    /// <summary>
    /// Returns the launch profile to use by default — the first profile, or the
    /// named one when a project/classic .NET order is present, else null.
    /// </summary>
    LaunchProfile? GetDefaultLaunchProfile(ProjectModel project);

    /// <summary>
    /// Resolves a single named profile; null if not found.
    /// </summary>
    LaunchProfile? GetLaunchProfile(ProjectModel project, string name);
}