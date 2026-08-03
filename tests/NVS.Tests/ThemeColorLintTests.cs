using System.Text.RegularExpressions;

namespace NVS.Tests;

/// <summary>
/// Guards the theme system: hardcoded hex colors in view XAML are what broke the
/// light theme. Views must use theme brushes ({DynamicResource ...Brush}); only
/// semantic colors that work on every theme are allowlisted.
/// </summary>
public partial class ThemeColorLintTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    // Semantic colors valid on all four themes: status/severity, icon, diff blends, overlays.
    private static readonly HashSet<string> AllowedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        "#F44747", "#FF6B68",                         // error reds
        "#4EC9B0", "#89D185", "#75BEFF",              // run/debug icon colors
        "#D19A66",                                    // approval border accent
        "#FFFFFF", "#80FFFFFF", "#20FFFFFF",          // whites + overlays
        "#80000000",                                  // palette backdrop
        "#5E2E2E", "#2E5E2E",                         // diff added/deleted pills
        "#15808080", "#20808080", "#40808080",        // alpha blends
        "#204EC9B0", "#304EC9B0", "#404EC9B0",
        "#20F44747", "#30F44747", "#40F44747",
    };

    [Fact]
    public void Views_DoNotUseNonThemeHardcodedColors()
    {
        var violations = new List<string>();

        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(RepoRoot, "src", "NVS"), "*.axaml", SearchOption.AllDirectories))
        {
            // Common.axaml defines the theme's fallback brushes — colors are expected there.
            if (file.EndsWith(Path.Combine("Styles", "Common.axaml"), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                foreach (Match match in HexColorRegex().Matches(line))
                {
                    if (!AllowedColors.Contains(match.Value))
                    {
                        violations.Add(
                            $"{Path.GetRelativePath(RepoRoot, file)}:{lineNumber}: {match.Value} " +
                            $"(use a {{DynamicResource ...Brush}} theme brush)");
                    }
                }
            }
        }

        violations.Should().BeEmpty(
            "hardcoded colors break theme switching — bind theme brushes instead:\n  "
            + string.Join("\n  ", violations));
    }

    [GeneratedRegex("#[0-9A-Fa-f]{8}\\b|#[0-9A-Fa-f]{6}\\b")]
    private static partial Regex HexColorRegex();

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "NVS", "MainWindow.axaml")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Repo root not found from " + AppContext.BaseDirectory);
    }
}
