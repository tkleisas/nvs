using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Runs `dotnet build` on the workspace and returns the result.
/// </summary>
public sealed class RunBuildTool : IAgentTool
{
    private readonly Func<IBuildService> _getBuildService;
    private readonly Func<string> _getWorkspacePath;

    public string Name => "run_build";
    public string Description => "Build the current project/solution using 'dotnet build'. Returns success/failure, errors, and warnings.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "configuration": {
                    "type": "string",
                    "description": "Build configuration (Debug or Release). Defaults to Debug.",
                    "enum": ["Debug", "Release"]
                }
            }
        }
        """);

    public RunBuildTool(Func<IBuildService> getBuildService, Func<string> getWorkspacePath)
    {
        _getBuildService = getBuildService;
        _getWorkspacePath = getWorkspacePath;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var buildService = _getBuildService();
        var workspace = _getWorkspacePath();

        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var config = args.TryGetProperty("configuration", out var c) ? c.GetString() ?? "Debug" : "Debug";

        var task = new BuildTask
        {
            Name = "build",
            Command = "dotnet",
            Args = ["build", "--configuration", config, "--no-restore"],
            WorkingDirectory = workspace
        };

        try
        {
            var result = await buildService.RunTaskAsync(task, cancellationToken);

            return JsonSerializer.Serialize(new
            {
                success = result.Success,
                exit_code = result.ExitCode,
                duration_ms = (int)result.Duration.TotalMilliseconds,
                errors = result.Errors.Select(e => new { e.Message, e.FilePath, e.Line, e.Column }),
                warnings = result.Warnings.Select(w => new { w.Message, w.FilePath, w.Line, w.Column }),
                error_count = result.Errors.Count,
                warning_count = result.Warnings.Count
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
