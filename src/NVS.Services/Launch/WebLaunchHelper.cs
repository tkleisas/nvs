using NVS.Core.Models;

namespace NVS.Services.Launch;

/// <summary>
/// Helpers for deriving the browser URL from a launch profile and for
/// picking the profile to use given IDE state (selected override vs default).
/// </summary>
public static class WebLaunchHelper
{
    /// <summary>
    /// Combines <see cref="LaunchProfile.FirstApplicationUrl"/> with the
    /// <see cref="LaunchProfile.LaunchUrl"/> relative suffix. Returns null when
    /// the profile has no application URL or when the launchUrl is absolute
    /// (in which case the launchUrl is preferred verbatim).
    /// </summary>
    public static string? ComposeBrowserUrl(LaunchProfile? profile)
    {
        if (profile is null) return null;

        var app = profile.FirstApplicationUrl;
        var launch = profile.LaunchUrl;

        if (string.IsNullOrWhiteSpace(app) && string.IsNullOrWhiteSpace(launch))
            return null;

        if (string.IsNullOrWhiteSpace(launch))
            return app;

        if (string.IsNullOrWhiteSpace(app))
            return launch;

        // Absolute `launchUrl` overrides the `applicationUrl`.
        if (Uri.TryCreate(launch, UriKind.Absolute, out _))
            return launch;

        // Relative `launchUrl` (e.g. "/swagger", "index.html") joined to `applicationUrl`.
        var combined = app.TrimEnd('/') + (launch.StartsWith('/') ? string.Empty : "/") + launch.TrimStart('/');
        return combined;
    }
}