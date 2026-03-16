using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;

namespace NVS.Services.Prerequisites;

public sealed class PrerequisiteService : IPrerequisiteService
{
    private static readonly Dictionary<Language, PrerequisiteDefinition> s_prerequisites = new()
    {
        [Language.CSharp] = new("dotnet", ".NET SDK", "Install from https://dotnet.microsoft.com/download"),
        [Language.Java] = new("java", "Java JDK", "Install from https://adoptium.net or https://jdk.java.net"),
        [Language.Php] = new("php", "PHP", "Install from https://www.php.net/downloads"),
        [Language.Python] = new("python", "Python", "Install from https://www.python.org/downloads"),
        [Language.JavaScript] = new("node", "Node.js", "Install from https://nodejs.org"),
        [Language.TypeScript] = new("node", "Node.js", "Install from https://nodejs.org"),
        [Language.Rust] = new("rustc", "Rust", "Install from https://rustup.rs"),
        [Language.Go] = new("go", "Go", "Install from https://go.dev/dl"),
        [Language.Cpp] = new("gcc", "C/C++ Compiler", "Install GCC from https://gcc.gnu.org, Clang, or MSVC"),
        [Language.C] = new("gcc", "C/C++ Compiler", "Install GCC from https://gcc.gnu.org, Clang, or MSVC"),
    };

    public Task<IReadOnlyList<PrerequisiteInfo>> CheckPrerequisitesAsync(
        IEnumerable<Language> languages,
        CancellationToken cancellationToken = default)
    {
        var checked_ = new HashSet<string>();
        var missing = new List<PrerequisiteInfo>();

        foreach (var language in languages)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!s_prerequisites.TryGetValue(language, out var def))
                continue;

            // Skip if already checked this binary (e.g. JS + TS both need node)
            if (!checked_.Add(def.BinaryName))
                continue;

            if (FindBinaryOnPath(def.BinaryName) is null)
            {
                missing.Add(new PrerequisiteInfo
                {
                    Language = language,
                    BinaryName = def.BinaryName,
                    DisplayName = def.DisplayName,
                    InstallHint = def.InstallHint,
                });
            }
        }

        return Task.FromResult<IReadOnlyList<PrerequisiteInfo>>(missing);
    }

    internal static string? FindBinaryOnPath(string binaryName)
    {
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var searchDirs = new List<string>(pathVar.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (!OperatingSystem.IsWindows())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(home))
            {
                searchDirs.Add(Path.Combine(home, ".dotnet", "tools"));
                searchDirs.Add(Path.Combine(home, ".local", "bin"));
                searchDirs.Add(Path.Combine(home, ".cargo", "bin"));
                searchDirs.Add(Path.Combine(home, "go", "bin"));
            }
        }

        var extensions = OperatingSystem.IsWindows()
            ? new[] { ".exe", ".cmd", ".bat", "" }
            : new[] { "" };

        foreach (var dir in searchDirs)
        {
            if (string.IsNullOrEmpty(dir))
                continue;

            foreach (var ext in extensions)
            {
                var fullPath = Path.Combine(dir, binaryName + ext);
                if (File.Exists(fullPath))
                    return fullPath;
            }
        }

        return null;
    }

    internal static IReadOnlyDictionary<Language, PrerequisiteDefinition> GetPrerequisiteDefinitions()
        => s_prerequisites;

    internal sealed record PrerequisiteDefinition(
        string BinaryName,
        string DisplayName,
        string InstallHint);
}
