using System.Text;

namespace NVS.Core.LLM;

/// <summary>
/// Parses the plain-text segments of a chat message (code fences already split
/// out by <see cref="MarkdownCodeBlockParser"/>) into styled blocks and inline
/// spans, so the chat UI can render markdown without a rendering dependency.
/// Supports headings, bullet/numbered lists, block quotes, horizontal rules,
/// and inline bold/italic/code/strikethrough/links.
/// </summary>
public static class MarkdownTextParser
{
    public static IReadOnlyList<MarkdownBlock> Parse(string text)
    {
        var blocks = new List<MarkdownBlock>();
        if (string.IsNullOrEmpty(text))
            return blocks;

        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            blocks.Add(new MarkdownBlock
            {
                Type = MarkdownBlockType.Paragraph,
                Spans = ParseInlines(string.Join('\n', paragraph))
            });
            paragraph.Clear();
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.TrimStart();

            if (trimmed.Length == 0)
            {
                FlushParagraph();
                continue;
            }

            // Horizontal rule: --- *** ___
            if (trimmed.Length >= 3 && (trimmed.All(c => c == '-') || trimmed.All(c => c == '*') || trimmed.All(c => c == '_')))
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock { Type = MarkdownBlockType.HorizontalRule });
                continue;
            }

            // Heading: # .. ######
            if (trimmed.StartsWith('#'))
            {
                var level = 0;
                while (level < trimmed.Length && trimmed[level] == '#') level++;
                if (level <= 6 && level < trimmed.Length && trimmed[level] == ' ')
                {
                    FlushParagraph();
                    blocks.Add(new MarkdownBlock
                    {
                        Type = MarkdownBlockType.Heading,
                        HeadingLevel = level,
                        Spans = ParseInlines(trimmed[(level + 1)..].Trim())
                    });
                    continue;
                }
            }

            // Bullet list item: - x, * x, + x
            if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '+' && trimmed[1] == ' ')
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock
                {
                    Type = MarkdownBlockType.ListItem,
                    ListMarker = "•",
                    IndentLevel = (line.Length - trimmed.Length) / 2,
                    Spans = ParseInlines(trimmed[2..])
                });
                continue;
            }

            // Numbered list item: 1. x or 1) x
            var digits = 0;
            while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits])) digits++;
            if (digits > 0 && digits + 1 < trimmed.Length
                && trimmed[digits] is '.' or ')' && trimmed[digits + 1] == ' ')
            {
                FlushParagraph();
                blocks.Add(new MarkdownBlock
                {
                    Type = MarkdownBlockType.ListItem,
                    ListMarker = $"{trimmed[..digits]}.",
                    IndentLevel = (line.Length - trimmed.Length) / 2,
                    Spans = ParseInlines(trimmed[(digits + 2)..])
                });
                continue;
            }

            // Block quote: > x
            if (trimmed.StartsWith('>'))
            {
                FlushParagraph();
                var quoteText = trimmed.Length > 1 && trimmed[1] == ' ' ? trimmed[2..] : trimmed[1..];
                blocks.Add(new MarkdownBlock
                {
                    Type = MarkdownBlockType.Quote,
                    Spans = ParseInlines(quoteText)
                });
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph();
        return blocks;
    }

    /// <summary>
    /// Tokenizes inline markdown within one block. Styles are scoped to the
    /// block: unbalanced markers style to the end rather than leaking further.
    /// Single underscores are intentionally not emphasis so identifiers like
    /// snake_case survive untouched.
    /// </summary>
    internal static IReadOnlyList<MarkdownSpan> ParseInlines(string text)
    {
        var spans = new List<MarkdownSpan>();
        var sb = new StringBuilder();
        bool bold = false, italic = false, strike = false;

        void Flush()
        {
            if (sb.Length == 0) return;
            spans.Add(new MarkdownSpan { Text = sb.ToString(), Bold = bold, Italic = italic, Strikethrough = strike });
            sb.Clear();
        }

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            // Inline code: `...` (no styling parsed inside)
            if (c == '`')
            {
                var close = text.IndexOf('`', i + 1);
                if (close > i)
                {
                    Flush();
                    spans.Add(new MarkdownSpan { Text = text[(i + 1)..close], Code = true });
                    i = close;
                    continue;
                }
            }

            // Link: [text](url)
            if (c == '[')
            {
                var closeBracket = text.IndexOf(']', i + 1);
                if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                {
                    var closeParen = text.IndexOf(')', closeBracket + 2);
                    if (closeParen > closeBracket + 1)
                    {
                        Flush();
                        spans.Add(new MarkdownSpan
                        {
                            Text = text[(i + 1)..closeBracket],
                            LinkUrl = text[(closeBracket + 2)..closeParen],
                            Bold = bold,
                            Italic = italic
                        });
                        i = closeParen;
                        continue;
                    }
                }
            }

            // Bold: ** or __
            if (i + 1 < text.Length && ((c == '*' && text[i + 1] == '*') || (c == '_' && text[i + 1] == '_')))
            {
                Flush();
                bold = !bold;
                i++;
                continue;
            }

            // Strikethrough: ~~
            if (i + 1 < text.Length && c == '~' && text[i + 1] == '~')
            {
                Flush();
                strike = !strike;
                i++;
                continue;
            }

            // Italic: single *
            if (c == '*')
            {
                Flush();
                italic = !italic;
                continue;
            }

            sb.Append(c);
        }

        Flush();
        return spans;
    }
}

public enum MarkdownBlockType
{
    Paragraph,
    Heading,
    ListItem,
    Quote,
    HorizontalRule
}

public sealed record MarkdownBlock
{
    public required MarkdownBlockType Type { get; init; }

    /// <summary>1–6 for headings; 0 otherwise.</summary>
    public int HeadingLevel { get; init; }

    /// <summary>Rendered list marker ("•" or "1."), for list items.</summary>
    public string? ListMarker { get; init; }

    /// <summary>Nesting depth for list items (two leading spaces per level).</summary>
    public int IndentLevel { get; init; }

    public IReadOnlyList<MarkdownSpan> Spans { get; init; } = [];
}

public sealed record MarkdownSpan
{
    public required string Text { get; init; }
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Code { get; init; }
    public bool Strikethrough { get; init; }
    public string? LinkUrl { get; init; }
}
