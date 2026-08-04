namespace NVS.Core.Models;

/// <summary>
/// Self-hosting support: when the IDE runs from the opened solution's own build
/// output, normal builds fail — the running process locks its own binaries
/// (MSB3026/MSB3021 copy errors). Building to a stable shadow output directory
/// sidesteps the locks; debug and run flows then use the shadow binaries.
/// </summary>
public static class SelfHostHelper
{
    /// <summary>
    /// True when the running IDE executable lives under the target solution/project
    /// directory — meaning builds against it would hit locked output files.
    /// </summary>
    public static bool IsSelfHosted(string targetPath) =>
        IsSelfHosted(targetPath, Environment.ProcessPath);

    /// <summary>Testable core of <see cref="IsSelfHosted(string)"/> with an explicit process path.</summary>
    public static bool IsSelfHosted(string targetPath, string? processPath)
    {
        var targetDirectory = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (processPath is null || targetDirectory is null)
        {
            return false;
        }

        var processDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        return processDirectory is not null
            && processDirectory.StartsWith(targetDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Stable shadow output directory for a solution, under the temp dir. Stable so
    /// debug/test flows can locate the binaries a previous build produced.
    /// </summary>
    public static string GetShadowDirectory(string solutionPath)
    {
        var name = Path.GetFileNameWithoutExtension(solutionPath);
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "solution";
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }

        return Path.Combine(Path.GetTempPath(), "nvs-shadow", name);
    }

    /// <summary>MSBuild argument redirecting all project output to the shadow directory.</summary>
    public static string ShadowOutDirArgument(string solutionPath) =>
        $"-p:OutDir={GetShadowDirectory(solutionPath)}{Path.DirectorySeparatorChar}";
}
