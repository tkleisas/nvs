using System.Text.Json;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Writes or creates a file in the workspace.
/// </summary>
public sealed class WriteFileTool : IAgentTool
{
    private readonly Func<string> _getWorkspacePath;

    public string Name => "write_file";
    public string Description => "Write content to a file. Creates the file and any parent directories if they don't exist. Overwrites existing content.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative or absolute path for the file"
                },
                "content": {
                    "type": "string",
                    "description": "Full file content to write"
                }
            },
            "required": ["path", "content"]
        }
        """);

    public WriteFileTool(Func<string> getWorkspacePath)
    {
        _getWorkspacePath = getWorkspacePath;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var path = args.GetProperty("path").GetString() ?? throw new ArgumentException("path is required");
        var content = args.GetProperty("content").GetString() ?? string.Empty;

        var fullPath = ResolvePath(path);

        // Security: ensure path is within workspace
        var workspace = _getWorkspacePath();
        var normalizedFull = Path.GetFullPath(fullPath);
        var normalizedWorkspace = Path.GetFullPath(workspace);
        if (!normalizedFull.StartsWith(normalizedWorkspace, StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { error = "Cannot write files outside the workspace" });

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var existed = File.Exists(fullPath);
        await File.WriteAllTextAsync(fullPath, content, cancellationToken);

        return JsonSerializer.Serialize(new
        {
            path,
            action = existed ? "updated" : "created",
            bytes_written = content.Length
        });
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.Combine(_getWorkspacePath(), path);
    }
}
