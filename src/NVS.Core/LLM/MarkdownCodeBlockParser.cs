namespace NVS.Core.LLM;

/// <summary>
/// Parses chat message content into segments of plain text and code blocks.
/// Handles triple-backtick fenced code blocks with optional language tags.
/// </summary>
public static class MarkdownCodeBlockParser
{
    /// <summary>Parse message content into ordered segments.</summary>
    public static IReadOnlyList<ContentSegment> Parse(string content)
    {
        if (string.IsNullOrEmpty(content))
            return [];

        var segments = new List<ContentSegment>();
        var lines = content.Split('\n');
        var textBuffer = new List<string>();
        var codeBuffer = new List<string>();
        string? codeLanguage = null;
        bool inCodeBlock = false;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (!inCodeBlock && line.TrimStart().StartsWith("```"))
            {
                // Flush accumulated text
                if (textBuffer.Count > 0)
                {
                    segments.Add(new ContentSegment
                    {
                        Type = SegmentType.Text,
                        Content = string.Join('\n', textBuffer)
                    });
                    textBuffer.Clear();
                }

                // Extract language tag
                var trimmed = line.TrimStart();
                codeLanguage = trimmed.Length > 3 ? trimmed[3..].Trim() : null;
                if (string.IsNullOrEmpty(codeLanguage)) codeLanguage = null;
                inCodeBlock = true;
                codeBuffer.Clear();
            }
            else if (inCodeBlock && line.TrimStart().StartsWith("```"))
            {
                // End of code block
                segments.Add(new ContentSegment
                {
                    Type = SegmentType.Code,
                    Content = string.Join('\n', codeBuffer),
                    Language = codeLanguage
                });
                codeBuffer.Clear();
                codeLanguage = null;
                inCodeBlock = false;
            }
            else if (inCodeBlock)
            {
                codeBuffer.Add(line);
            }
            else
            {
                textBuffer.Add(line);
            }
        }

        // Flush remaining
        if (inCodeBlock && codeBuffer.Count > 0)
        {
            // Unclosed code block — treat as code anyway (streaming may cut off)
            segments.Add(new ContentSegment
            {
                Type = SegmentType.Code,
                Content = string.Join('\n', codeBuffer),
                Language = codeLanguage
            });
        }
        else if (textBuffer.Count > 0)
        {
            segments.Add(new ContentSegment
            {
                Type = SegmentType.Text,
                Content = string.Join('\n', textBuffer)
            });
        }

        return segments;
    }
}

public enum SegmentType
{
    Text,
    Code
}

public sealed record ContentSegment
{
    public required SegmentType Type { get; init; }
    public required string Content { get; init; }
    public string? Language { get; init; }
}
