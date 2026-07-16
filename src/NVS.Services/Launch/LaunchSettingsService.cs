using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using Serilog;

namespace NVS.Services.Launch;

/// <summary>
/// Reads .NET <c>launchSettings.json</c> and exposes it via <see cref="ILaunchSettingsService"/>.
/// The contract is language-agnostic so other languages can later supply their own provider
/// (Java/Maven application startup, PHP built-in server, etc.).
/// </summary>
public sealed class LaunchSettingsService : ILaunchSettingsService
{
    public IReadOnlyList<LaunchProfile> GetLaunchProfiles(ProjectModel project)
        => ParseLaunchSettings(project).Profiles;

    public LaunchProfile? GetDefaultLaunchProfile(ProjectModel project)
        => ParseLaunchSettings(project).DefaultProfile;

    public LaunchProfile? GetLaunchProfile(ProjectModel project, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return ParseLaunchSettings(project).Profiles.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.Ordinal));
    }

    private static ParsedLaunchSettings ParseLaunchSettings(ProjectModel project)
    {
        var dir = project.ProjectDirectory;
        if (string.IsNullOrEmpty(dir))
            return ParsedLaunchSettings.Empty;

        var path = Path.Combine(dir, "Properties", "launchSettings.json");
        if (!File.Exists(path))
            return ParsedLaunchSettings.Empty;

        try
        {
            var text = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(text);
            var profiles = new List<LaunchProfile>();

            if (doc.RootElement.TryGetProperty("profiles", out var profilesEl)
                && profilesEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in profilesEl.EnumerateObject())
                {
                    var profile = ParseProfile(prop.Name, prop.Value);
                    if (profile is not null) profiles.Add(profile);
                }
            }

            var defaultProfile = ResolveDefault(profiles);
            return new ParsedLaunchSettings(profiles, defaultProfile);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse launchSettings.json at {Path}", path);
            return ParsedLaunchSettings.Empty;
        }
    }

    private static LaunchProfile? ParseProfile(string name, JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        string? commandName = GetString(el, "commandName");
        string? applicationUrl = GetString(el, "applicationUrl");
        string? launchUrl = GetString(el, "launchUrl");
        string? commandLineArgs = GetString(el, "commandLineArgs");

        bool launchBrowser = true;
        if (el.TryGetProperty("launchBrowser", out var lb))
        {
            if (lb.ValueKind == JsonValueKind.False) launchBrowser = false;
            else if (lb.ValueKind == JsonValueKind.True) launchBrowser = true;
        }

        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (el.TryGetProperty("environmentVariables", out var ev) && ev.ValueKind == JsonValueKind.Object)
        {
            foreach (var e in ev.EnumerateObject())
            {
                if (e.Value.ValueKind == JsonValueKind.String)
                    env[e.Name] = e.Value.GetString() ?? string.Empty;
            }
        }

        return new LaunchProfile
        {
            Name = name,
            CommandName = commandName,
            ApplicationUrl = applicationUrl,
            LaunchUrl = launchUrl,
            LaunchBrowser = launchBrowser,
            EnvironmentVariables = env,
            CommandLineArgs = commandLineArgs
        };
    }

    private static string? GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static LaunchProfile? ResolveDefault(IReadOnlyList<LaunchProfile> profiles)
    {
        if (profiles.Count == 0) return null;
        // Prefer the first Project-launch profile (the modern default http/https entries);
        // fall back to any profile.
        return profiles.FirstOrDefault(p => p.IsProjectLaunch) ?? profiles[0];
    }

    private sealed record ParsedLaunchSettings(IReadOnlyList<LaunchProfile> Profiles, LaunchProfile? DefaultProfile)
    {
        public static ParsedLaunchSettings Empty => new(Array.Empty<LaunchProfile>(), null);
    }
}