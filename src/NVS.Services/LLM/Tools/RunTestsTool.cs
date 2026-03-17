using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Runs `dotnet test` on the workspace and returns the result.
/// </summary>
public sealed class RunTestsTool : IAgentTool
{
    private readonly Func<IBuildService> _getBuildService;
    private readonly Func<string> _getWorkspacePath;

    public string Name => "run_tests";
    public string Description => "Run tests using 'dotnet test'. Optionally filter by test name. Returns pass/fail counts and error details.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "filter": {
                    "type": "string",
                    "description": "Test filter expression (e.g. 'FullyQualifiedName~MyTest'). Omit to run all tests."
                },
                "project": {
                    "type": "string",
                    "description": "Relative path to a specific test project. Omit to test all projects."
                }
            }
        }
        """);

    public RunTestsTool(Func<IBuildService> getBuildService, Func<string> getWorkspacePath)
    {
        _getBuildService = getBuildService;
        _getWorkspacePath = getWorkspacePath;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var buildService = _getBuildService();
        var workspace = _getWorkspacePath();

        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);

        var testArgs = new List<string> { "test", "--no-restore" };

        if (args.TryGetProperty("project", out var proj))
        {
            var projectPath = proj.GetString();
            if (!string.IsNullOrEmpty(projectPath))
                testArgs.Add(projectPath);
        }

        if (args.TryGetProperty("filter", out var filter))
        {
            var filterStr = filter.GetString();
            if (!string.IsNullOrEmpty(filterStr))
            {
                testArgs.Add("--filter");
                testArgs.Add(filterStr);
            }
        }

        var task = new BuildTask
        {
            Name = "test",
            Command = "dotnet",
            Args = testArgs,
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
                errors = result.Errors.Select(e => new { e.Message, e.FilePath, e.Line }),
                error_count = result.Errors.Count
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }
}
