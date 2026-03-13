using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Reads file contents from the workspace with optional line range.
/// </summary>
public sealed class ReadFileTool : IAgentTool
{
    private readonly Func<string> _getWorkspacePath;

    public string Name => "read_file";
    public string Description => "Read the contents of a file from the workspace. Optionally specify line_start and line_end to read a range.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative or absolute path to the file"
                },
                "line_start": {
                    "type": "integer",
                    "description": "First line to read (1-based, inclusive). Omit to read from start."
                },
                "line_end": {
                    "type": "integer",
                    "description": "Last line to read (1-based, inclusive). Omit to read to end."
                }
            },
            "required": ["path"]
        }
        """);

    public ReadFileTool(Func<string> getWorkspacePath)
    {
        _getWorkspacePath = getWorkspacePath;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var path = args.GetProperty("path").GetString() ?? throw new ArgumentException("path is required");

        var fullPath = ResolvePath(path);

        if (!File.Exists(fullPath))
            return JsonSerializer.Serialize(new { error = $"File not found: {path}" });

        var lines = await File.ReadAllLinesAsync(fullPath, cancellationToken);

        int start = args.TryGetProperty("line_start", out var ls) ? ls.GetInt32() - 1 : 0;
        int end = args.TryGetProperty("line_end", out var le) ? le.GetInt32() : lines.Length;

        start = Math.Max(0, start);
        end = Math.Min(lines.Length, end);

        var selectedLines = lines[start..end];
        var numberedLines = selectedLines
            .Select((line, i) => $"{start + i + 1}. {line}");

        return JsonSerializer.Serialize(new
        {
            path,
            total_lines = lines.Length,
            line_start = start + 1,
            line_end = end,
            content = string.Join('\n', numberedLines)
        });
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(_getWorkspacePath(), path);
    }
}
