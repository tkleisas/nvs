namespace NVS.Core.Models.Settings;

/// <summary>Where solution builds put their output.</summary>
public enum BuildOutputMode
{
    /// <summary>Standard per-project layout: <c>bin/&lt;Configuration&gt;/&lt;TFM&gt;</c>.</summary>
    Default,

    /// <summary>Standard layout, except when the IDE runs from the opened solution
    /// (self-hosting) — then output goes to a shadow directory to avoid file locks.</summary>
    Auto,

    /// <summary>Always build to <see cref="BuildSettings.CustomOutputDirectory"/>
    /// (or a temp shadow directory when no path is set).</summary>
    Custom,
}

/// <summary>Build-related user settings.</summary>
public sealed record BuildSettings
{
    /// <summary>Output mode for solution builds. Default: <see cref="BuildOutputMode.Auto"/>.</summary>
    public BuildOutputMode OutputMode { get; init; } = BuildOutputMode.Auto;

    /// <summary>
    /// Directory used when <see cref="OutputMode"/> is <see cref="BuildOutputMode.Custom"/>.
    /// Environment variables are expanded. Empty falls back to a temp shadow directory.
    /// </summary>
    public string? CustomOutputDirectory { get; init; }
}
