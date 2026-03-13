using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Lists files in the workspace matching a glob pattern.
/// </summary>
public sealed class ListFilesTool : IAgentTool
{
    private readonly Func<string> _getWorkspacePath;

    public string Name => "list_files";
    public string Description => "List files in the workspace. Use pattern to filter (e.g., '**/*.cs', 'src/**'). Returns file paths relative to workspace root.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "pattern": {
                    "type": "string",
                    "description": "Glob-like pattern to filter files. Default: all files. Examples: '**/*.cs', 'src/**/*.ts'"
                },
                "directory": {
                    "type": "string",
                    "description": "Subdirectory to search in (relative to workspace). Default: workspace root."
                },
                "max_results": {
                    "type": "integer",
                    "description": "Maximum number of results to return. Default: 200."
                }
            }
        }
        """);

    public ListFilesTool(Func<string> getWorkspacePath)
    {
        _getWorkspacePath = getWorkspacePath;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);

        var pattern = args.TryGetProperty("pattern", out var p) ? p.GetString() ?? "*" : "*";
        var subDir = args.TryGetProperty("directory", out var d) ? d.GetString() ?? "" : "";
        var maxResults = args.TryGetProperty("max_results", out var m) ? m.GetInt32() : 200;

        var workspace = _getWorkspacePath();
        var searchDir = string.IsNullOrEmpty(subDir)
            ? workspace
            : Path.Combine(workspace, subDir);

        if (!Directory.Exists(searchDir))
            return Task.FromResult(JsonSerializer.Serialize(new { error = $"Directory not found: {subDir}" }));

        // Extract the file pattern from the glob (last segment)
        var searchPattern = ExtractSearchPattern(pattern);

        var files = Directory.EnumerateFiles(searchDir, searchPattern, SearchOption.AllDirectories)
            .Where(f => !IsHiddenOrBuildPath(f, workspace))
            .Take(maxResults)
            .Select(f => Path.GetRelativePath(workspace, f).Replace('\\', '/'))
            .ToList();

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            directory = string.IsNullOrEmpty(subDir) ? "." : subDir,
            pattern,
            count = files.Count,
            truncated = files.Count >= maxResults,
            files
        }));
    }

    private static string ExtractSearchPattern(string pattern)
    {
        // Convert simple glob to Directory.EnumerateFiles pattern
        var fileName = Path.GetFileName(pattern);
        return string.IsNullOrEmpty(fileName) ? "*" : fileName;
    }

    private static bool IsHiddenOrBuildPath(string filePath, string workspace)
    {
        var relative = Path.GetRelativePath(workspace, filePath);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Any(p =>
            p.StartsWith('.') ||
            p.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            p.Equals("node_modules", StringComparison.OrdinalIgnoreCase));
    }
}
