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
}
