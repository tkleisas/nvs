using System.Text;

namespace NVS.Services.LLM;

/// <summary>One row in an inline-edit diff preview.</summary>
public sealed record DiffRow(DiffRowKind Kind, int? OldLine, int? NewLine, string Text)
{
    /// <summary>Whether this row is a deletion (drives the red style class).</summary>
    public bool IsDeleted => Kind == DiffRowKind.Deleted;

    /// <summary>Whether this row is an addition (drives the green style class).</summary>
    public bool IsAdded => Kind == DiffRowKind.Added;

    /// <summary>Gutter prefix for the row.</summary>
    public string Prefix => Kind switch
    {
        DiffRowKind.Added => "+ ",
        DiffRowKind.Deleted => "- ",
        _ => "  ",
    };
}

public enum DiffRowKind
{
    Context,
    Deleted,
    Added,
}

/// <summary>
/// Minimal LCS-based line differ for inline AI edit previews (old text → proposed text).
/// No libgit dependency; enough for the small snippets inline chat deals with.
/// </summary>
public static class SimpleDiffer
{
    public static IReadOnlyList<DiffRow> Diff(string oldText, string newText, int contextLines = 2)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        // LCS table
        var lcs = new int[oldLines.Length + 1, newLines.Length + 1];
        for (var i = oldLines.Length - 1; i >= 0; i--)
        {
            for (var j = newLines.Length - 1; j >= 0; j--)
            {
                lcs[i, j] = oldLines[i] == newLines[j]
                    ? lcs[i + 1, j + 1] + 1
                    : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
            }
        }

        var rows = new List<DiffRow>();
        var (x, y) = (0, 0);
        while (x < oldLines.Length && y < newLines.Length)
        {
            if (oldLines[x] == newLines[y])
            {
                rows.Add(new DiffRow(DiffRowKind.Context, x + 1, y + 1, oldLines[x]));
                x++;
                y++;
            }
            else if (lcs[x + 1, y] >= lcs[x, y + 1])
            {
                rows.Add(new DiffRow(DiffRowKind.Deleted, x + 1, null, oldLines[x]));
                x++;
            }
            else
            {
                rows.Add(new DiffRow(DiffRowKind.Added, null, y + 1, newLines[y]));
                y++;
            }
        }

        while (x < oldLines.Length)
        {
            rows.Add(new DiffRow(DiffRowKind.Deleted, x + 1, null, oldLines[x]));
            x++;
        }
        while (y < newLines.Length)
        {
            rows.Add(new DiffRow(DiffRowKind.Added, null, y + 1, newLines[y]));
            y++;
        }

        return TrimContext(rows, contextLines);
    }

    /// <summary>Keeps only <paramref name="contextLines"/> of context around each change block.</summary>
    private static IReadOnlyList<DiffRow> TrimContext(List<DiffRow> rows, int contextLines)
    {
        var keep = new bool[rows.Count];
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind is DiffRowKind.Context) continue;
            for (var j = Math.Max(0, i - contextLines); j <= Math.Min(rows.Count - 1, i + contextLines); j++)
            {
                keep[j] = true;
            }
        }

        var result = new List<DiffRow>();
        var gapShown = false;
        for (var i = 0; i < rows.Count; i++)
        {
            if (keep[i])
            {
                result.Add(rows[i]);
                gapShown = false;
            }
            else if (!gapShown)
            {
                result.Add(new DiffRow(DiffRowKind.Context, null, null, "…"));
                gapShown = true;
            }
        }

        return result;
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Split('\n');
}

/// <summary>Prompt builder for inline AI code edits (Ctrl+I).</summary>
public static class InlineEditPrompts
{
    /// <summary>
    /// Builds the system/user prompt for an inline edit instruction.
    /// <paramref name="hasContext">false when generating fresh code to insert at the caret.</paramref>
    /// </summary>
    public static (string System, string User) Build(string instruction, string context, string language, bool hasContext)
    {
        var system = $"""
            You are an expert {language} programmer embedded in a code editor.
            The user gives you an instruction and a code snippet.
            Reply with ONLY the resulting code — no explanations, no markdown fences, no commentary.
            Preserve the snippet's indentation style.
            """;

        var user = hasContext
            ? $"Instruction: {instruction}\n\nCode:\n{context}"
            : $"Instruction: {instruction}\n\nGenerate {language} code for the instruction. It will be inserted at the caret.";

        return (system, user);
    }

    /// <summary>Extracts code from an LLM reply: first fenced block if present, else the trimmed text.</summary>
    public static string ExtractCode(string llmText)
    {
        if (string.IsNullOrWhiteSpace(llmText))
        {
            return string.Empty;
        }

        var text = llmText;
        var fenceStart = text.IndexOf("```", StringComparison.Ordinal);
        if (fenceStart >= 0)
        {
            var contentStart = text.IndexOf('\n', fenceStart);
            if (contentStart >= 0)
            {
                var fenceEnd = text.IndexOf("```", contentStart, StringComparison.Ordinal);
                if (fenceEnd > contentStart)
                {
                    return text[(contentStart + 1)..fenceEnd].TrimEnd('\n');
                }
            }
        }

        return text.Trim();
    }
}
