using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Returns git status: branch, changed/staged files, ahead/behind counts.
/// </summary>
public sealed class GitStatusTool : IAgentTool
{
    private readonly Func<IGitService> _getGitService;

    public string Name => "git_status";
    public string Description => "Get the current git status: branch name, changed files, staged files, ahead/behind counts.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {}
        }
        """);

    public GitStatusTool(Func<IGitService> getGitService)
    {
        _getGitService = getGitService;
    }

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var git = _getGitService();

        if (!git.IsRepository)
            return Task.FromResult(JsonSerializer.Serialize(new { error = "Not a git repository" }));

        var status = git.Status;

        var changed = status.Files
            .Where(f => !f.IsStaged)
            .Select(f => new { f.Path, status = f.Status.ToString() })
            .ToList();

        var staged = status.Files
            .Where(f => f.IsStaged)
            .Select(f => new { f.Path, status = f.Status.ToString() })
            .ToList();

        var result = JsonSerializer.Serialize(new
        {
            branch = git.CurrentBranch,
            is_repository = true,
            has_unstaged_changes = status.HasUnstagedChanges,
            has_staged_changes = status.HasStagedChanges,
            ahead = status.AheadCount,
            behind = status.BehindCount,
            changed_files = changed,
            staged_files = staged,
            changed_count = changed.Count,
            staged_count = staged.Count
        });

        return Task.FromResult(result);
    }
}
