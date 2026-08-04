namespace NVS.Core.Models;

/// <summary>Whether a project file is copied to the build output directory.</summary>
public enum CopyToOutputMode
{
    /// <summary>Do not copy (MSBuild default; removes explicit metadata).</summary>
    Never,

    /// <summary><c>CopyToOutputDirectory=Always</c>.</summary>
    Always,

    /// <summary><c>CopyToOutputDirectory=PreserveNewest</c> ("Copy if newer").</summary>
    PreserveNewest,
}
