using System.Text;

namespace NVS.Services.LLM;

/// <summary>
/// Prompt builder and reply cleaner for AI-generated git commit messages
/// (conventional-commit style: short imperative subject, optional bullets).
/// </summary>
public static class CommitMessagePrompts
{
    /// <summary>Builds the system/user prompt pair for a staged diff.</summary>
    public static (string System, string User) Build(string patch, IEnumerable<string> filePaths)
    {
        const string system = """
            You write excellent git commit messages.
            Rules:
            - First line: a short imperative subject, at most 72 characters (e.g. "Add login form validation").
            - Optionally: one blank line, then 1–3 short bullet points starting with "- " for non-obvious changes.
            - Reply with ONLY the commit message text: no quotes, no backticks, no markdown fences, no commentary.
            """;

        var files = string.Join(", ", filePaths.Take(20));
        var user = new StringBuilder()
            .Append("Files changed: ").AppendLine(files)
            .AppendLine()
            .AppendLine("Staged diff:")
            .Append(patch)
            .ToString();

        return (system, user);
    }

    /// <summary>
    /// Cleans an LLM reply down to the bare commit message: trims whitespace,
    /// drops surrounding markdown fences and wrapping quotes.
    /// </summary>
    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = text.Trim();

        // Strip a surrounding markdown fence (```...```)
        if (result.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = result.IndexOf('\n');
            var lastFence = result.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && lastFence > firstNewline)
            {
                result = result[(firstNewline + 1)..lastFence].Trim();
            }
        }

        // Strip wrapping quotes
        if (result.Length >= 2
            && ((result[0] == '"' && result[^1] == '"') || (result[0] == '\'' && result[^1] == '\'')))
        {
            result = result[1..^1].Trim();
        }

        return result;
    }
}
