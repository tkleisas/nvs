using System.Text.Json;
using NVS.Core.Interfaces;
using NVS.Core.LLM;

namespace NVS.Services.LLM.Tools;

/// <summary>
/// Returns the git diff for a specific file or all changes.
/// </summary>
public sealed class GitDiffTool : IAgentTool
{
    private readonly Func<IGitService> _getGitService;

    public string Name => "git_diff";
    public string Description => "Get the git diff showing changes. Can diff unstaged or staged changes, optionally for a specific file.";

    public JsonElement ParameterSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
            "type": "object",
            "properties": {
                "path": {
                    "type": "string",
                    "description": "Relative file path to diff. Omit to diff all files."
                },
                "staged": {
                    "type": "boolean",
                    "description": "If true, show staged (index) changes. Defaults to false (unstaged)."
                }
            }
        }
        """);

    public GitDiffTool(Func<IGitService> getGitService)
    {
        _getGitService = getGitService;
    }

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var git = _getGitService();

        if (!git.IsRepository)
            return JsonSerializer.Serialize(new { error = "Not a git repository" });

        var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
        var path = args.TryGetProperty("path", out var p) ? p.GetString() : null;
        var staged = args.TryGetProperty("staged", out var s) && s.GetBoolean();

        try
        {
            var hunks = staged
                ? await git.GetStagedDiffAsync(path, cancellationToken)
                : await git.GetDiffAsync(path, cancellationToken);

            if (hunks.Count == 0)
                return JsonSerializer.Serialize(new { diff = "No changes", hunk_count = 0 });

            var diffText = string.Join("\n", hunks.Select(FormatHunk));

            return JsonSerializer.Serialize(new
            {
                diff = diffText,
                hunk_count = hunks.Count,
                staged,
                path = path ?? "(all files)"
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private static string FormatHunk(DiffHunk hunk)
    {
        var header = $"@@ -{hunk.OldStart},{hunk.OldCount} +{hunk.NewStart},{hunk.NewCount} @@";
        var lines = hunk.Lines.Select(l => l.Type switch
        {
            DiffLineType.Addition => $"+{l.Content}",
            DiffLineType.Deletion => $"-{l.Content}",
            _ => $" {l.Content}"
        });
        return header + "\n" + string.Join("\n", lines);
    }
}
