using NVS.Core.Interfaces;

namespace NVS.Services.Git;

public sealed record DiffSideLine
{
    public int? LineNumber { get; init; }
    public string Content { get; init; } = "";
    public DiffSideLineType Type { get; init; }
}

public enum DiffSideLineType
{
    Context,
    Added,
    Deleted,
    Empty
}

public sealed record DiffSidePair
{
    public required DiffSideLine Left { get; init; }
    public required DiffSideLine Right { get; init; }
}

public sealed record FileDiff
{
    public required string FilePath { get; init; }
    public required IReadOnlyList<DiffSidePair> Lines { get; init; }
    public required IReadOnlyList<DiffHunk> Hunks { get; init; }
    public bool IsStaged { get; init; }
}

public static class DiffAligner
{
    /// <summary>
    /// Produces a full-file side-by-side view, interleaving unchanged lines between hunks.
    /// </summary>
    public static IReadOnlyList<DiffSidePair> AlignFullFile(
        string? oldContent,
        string? newContent,
        IReadOnlyList<DiffHunk> hunks)
    {
        var oldLines = SplitLines(oldContent);
        var newLines = SplitLines(newContent);

        if (hunks.Count == 0)
        {
            // No changes — show all lines as context
            var maxLines = Math.Max(oldLines.Length, newLines.Length);
            var pairs = new List<DiffSidePair>(maxLines);
            for (int i = 0; i < maxLines; i++)
            {
                pairs.Add(new DiffSidePair
                {
                    Left = i < oldLines.Length
                        ? new DiffSideLine { LineNumber = i + 1, Content = oldLines[i], Type = DiffSideLineType.Context }
                        : new DiffSideLine { Type = DiffSideLineType.Empty },
                    Right = i < newLines.Length
                        ? new DiffSideLine { LineNumber = i + 1, Content = newLines[i], Type = DiffSideLineType.Context }
                        : new DiffSideLine { Type = DiffSideLineType.Empty },
                });
            }
            return pairs;
        }

        var result = new List<DiffSidePair>();
        int oldPos = 0; // 0-based index into oldLines
        int newPos = 0; // 0-based index into newLines

        foreach (var hunk in hunks)
        {
            int hunkOldStart = Math.Max(0, hunk.OldStart - 1); // convert to 0-based, clamp
            int hunkNewStart = Math.Max(0, hunk.NewStart - 1);

            // Emit unchanged lines before this hunk
            while (oldPos < hunkOldStart && oldPos < oldLines.Length
                && newPos < hunkNewStart && newPos < newLines.Length)
            {
                result.Add(new DiffSidePair
                {
                    Left = new DiffSideLine { LineNumber = oldPos + 1, Content = oldLines[oldPos], Type = DiffSideLineType.Context },
                    Right = new DiffSideLine { LineNumber = newPos + 1, Content = newLines[newPos], Type = DiffSideLineType.Context },
                });
                oldPos++;
                newPos++;
            }

            // Emit the hunk lines using the existing alignment logic
            var hunkPairs = AlignHunk(hunk);
            result.AddRange(hunkPairs);

            // Advance positions past the hunk
            oldPos = hunkOldStart + hunk.OldCount;
            newPos = hunkNewStart + hunk.NewCount;
        }

        // Emit remaining unchanged lines after the last hunk
        while (oldPos < oldLines.Length && newPos < newLines.Length)
        {
            result.Add(new DiffSidePair
            {
                Left = new DiffSideLine { LineNumber = oldPos + 1, Content = oldLines[oldPos], Type = DiffSideLineType.Context },
                Right = new DiffSideLine { LineNumber = newPos + 1, Content = newLines[newPos], Type = DiffSideLineType.Context },
            });
            oldPos++;
            newPos++;
        }

        // Handle trailing lines in old only (shouldn't happen normally, but be safe)
        while (oldPos < oldLines.Length)
        {
            result.Add(new DiffSidePair
            {
                Left = new DiffSideLine { LineNumber = oldPos + 1, Content = oldLines[oldPos], Type = DiffSideLineType.Context },
                Right = new DiffSideLine { Type = DiffSideLineType.Empty },
            });
            oldPos++;
        }

        // Handle trailing lines in new only
        while (newPos < newLines.Length)
        {
            result.Add(new DiffSidePair
            {
                Left = new DiffSideLine { Type = DiffSideLineType.Empty },
                Right = new DiffSideLine { LineNumber = newPos + 1, Content = newLines[newPos], Type = DiffSideLineType.Context },
            });
            newPos++;
        }

        return result;
    }

    /// <summary>
    /// Aligns only the hunk lines (no surrounding context from the file).
    /// </summary>
    public static IReadOnlyList<DiffSidePair> AlignHunks(IReadOnlyList<DiffHunk> hunks)
    {
        var pairs = new List<DiffSidePair>();

        foreach (var hunk in hunks)
        {
            pairs.AddRange(AlignHunk(hunk));
        }

        return pairs;
    }

    public static IReadOnlyList<DiffSidePair> AlignHunk(DiffHunk hunk)
    {
        var pairs = new List<DiffSidePair>();
        var lines = hunk.Lines;
        int i = 0;

        while (i < lines.Count)
        {
            var line = lines[i];

            if (line.Type == DiffLineType.Context)
            {
                pairs.Add(new DiffSidePair
                {
                    Left = new DiffSideLine
                    {
                        LineNumber = line.OldLineNumber,
                        Content = line.Content,
                        Type = DiffSideLineType.Context,
                    },
                    Right = new DiffSideLine
                    {
                        LineNumber = line.NewLineNumber,
                        Content = line.Content,
                        Type = DiffSideLineType.Context,
                    },
                });
                i++;
            }
            else if (line.Type == DiffLineType.Deletion)
            {
                // Collect consecutive deletions
                var deletions = new List<DiffLine>();
                while (i < lines.Count && lines[i].Type == DiffLineType.Deletion)
                {
                    deletions.Add(lines[i]);
                    i++;
                }

                // Collect consecutive additions right after
                var additions = new List<DiffLine>();
                while (i < lines.Count && lines[i].Type == DiffLineType.Addition)
                {
                    additions.Add(lines[i]);
                    i++;
                }

                // Pair them side-by-side
                var maxCount = Math.Max(deletions.Count, additions.Count);
                for (int j = 0; j < maxCount; j++)
                {
                    var left = j < deletions.Count
                        ? new DiffSideLine
                        {
                            LineNumber = deletions[j].OldLineNumber,
                            Content = deletions[j].Content,
                            Type = DiffSideLineType.Deleted,
                        }
                        : new DiffSideLine { Type = DiffSideLineType.Empty };

                    var right = j < additions.Count
                        ? new DiffSideLine
                        {
                            LineNumber = additions[j].NewLineNumber,
                            Content = additions[j].Content,
                            Type = DiffSideLineType.Added,
                        }
                        : new DiffSideLine { Type = DiffSideLineType.Empty };

                    pairs.Add(new DiffSidePair { Left = left, Right = right });
                }
            }
            else if (line.Type == DiffLineType.Addition)
            {
                // Addition without preceding deletion
                pairs.Add(new DiffSidePair
                {
                    Left = new DiffSideLine { Type = DiffSideLineType.Empty },
                    Right = new DiffSideLine
                    {
                        LineNumber = line.NewLineNumber,
                        Content = line.Content,
                        Type = DiffSideLineType.Added,
                    },
                });
                i++;
            }
        }

        return pairs;
    }

    private static string[] SplitLines(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        return content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
    }
}
