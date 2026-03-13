using System.Text.Json;
using System.Text.RegularExpressions;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Searches file contents in the workspace using text or regex patterns.
/// </summary>
public sealed class SearchFilesTool : IAgentTool
{
    private readonly Func<string> _getWorkspacePath;

    public string Name => "search_files";
    public string Description => "Search file contents in the workspace for a text or regex pattern. Returns matching lines with file paths and line numbers.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "query": {
                    "type": "string",
                    "description": "Search text or regex pattern"
                },
                "file_pattern": {
                    "type": "string",
                    "description": "File pattern to search in (e.g., '*.cs', '*.ts'). Default: all text files."
                },
                "is_regex": {
                    "type": "boolean",
                    "description": "If true, treat query as a regex. Default: false (literal text search)."
                },
                "max_results": {
                    "type": "integer",
                    "description": "Maximum number of matching lines to return. Default: 50."
                }
            },
            "required": ["query"]
        }
        """);

    public SearchFilesTool(Func<string> getWorkspacePath)
    {
        _getWorkspacePath = getWorkspacePath;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var query = args.GetProperty("query").GetString() ?? throw new ArgumentException("query is required");
        var filePattern = args.TryGetProperty("file_pattern", out var fp) ? fp.GetString() ?? "*" : "*";
        var isRegex = args.TryGetProperty("is_regex", out var ir) && ir.GetBoolean();
        var maxResults = args.TryGetProperty("max_results", out var m) ? m.GetInt32() : 50;

        var workspace = _getWorkspacePath();

        if (!Directory.Exists(workspace))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "Workspace not found" }));

        Regex? regex = null;
        if (isRegex)
        {
            try { regex = new Regex(query, RegexOptions.IgnoreCase | RegexOptions.Compiled); }
            catch (RegexParseException ex)
            {
                return Task.FromResult(JsonSerializer.Serialize(new { error = $"Invalid regex: {ex.Message}" }));
            }
        }

        var matches = new List<object>();
        var filesSearched = 0;

        foreach (var file in Directory.EnumerateFiles(workspace, filePattern, SearchOption.AllDirectories))
        {
            if (IsHiddenOrBuildPath(file, workspace))
                continue;

            cancellationToken.ThrowIfCancellationRequested();
            filesSearched++;

            try
            {
                var lines = File.ReadAllLines(file);
                for (int i = 0; i < lines.Length && matches.Count < maxResults; i++)
                {
                    bool isMatch = regex is not null
                        ? regex.IsMatch(lines[i])
                        : lines[i].Contains(query, StringComparison.OrdinalIgnoreCase);

                    if (isMatch)
                    {
                        matches.Add(new
                        {
                            file = Path.GetRelativePath(workspace, file).Replace('\\', '/'),
                            line = i + 1,
                            content = lines[i].TrimEnd()
                        });
                    }
                }
            }
            catch (Exception)
            {
                // Skip binary files and permission errors
            }

            if (matches.Count >= maxResults)
                break;
        }

        return Task.FromResult(JsonSerializer.Serialize(new
        {
            query,
            files_searched = filesSearched,
            match_count = matches.Count,
            truncated = matches.Count >= maxResults,
            matches
        }));
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
