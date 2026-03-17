using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace NVS.Behaviors;

/// <summary>
/// Folding strategy for indentation-based languages (Python, YAML).
/// Creates fold regions when indentation increases, ending when it returns
/// to the original level.
/// </summary>
public sealed class IndentFoldingStrategy
{
    private readonly int _minimumLines;

    public IndentFoldingStrategy(int minimumLines = 2)
    {
        _minimumLines = minimumLines;
    }

    public IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var lineCount = document.LineCount;
        var indentStack = new Stack<(int indent, int startOffset, int startLine)>();

        for (var lineNum = 1; lineNum <= lineCount; lineNum++)
        {
            var line = document.GetLineByNumber(lineNum);
            var lineText = document.GetText(line.Offset, line.Length);

            // Skip empty/whitespace-only lines
            if (string.IsNullOrWhiteSpace(lineText))
                continue;

            var indent = GetIndentLevel(lineText);

            // Close all blocks with deeper or equal indentation
            while (indentStack.Count > 0 && indentStack.Peek().indent >= indent)
            {
                var block = indentStack.Pop();
                var prevLine = document.GetLineByNumber(lineNum - 1);
                // Find the last non-empty line before this one
                var endOffset = FindLastNonEmptyLineEnd(document, block.startLine + 1, lineNum - 1);
                if (endOffset > block.startOffset && (lineNum - 1 - block.startLine) >= _minimumLines)
                {
                    foldings.Add(new NewFolding(block.startOffset, endOffset) { Name = "..." });
                }
            }

            // If this line ends with colon (Python) or starts a new block, push it
            var trimmed = lineText.TrimEnd();
            if (trimmed.EndsWith(':') || (lineNum < lineCount && NextNonEmptyLineIndent(document, lineNum) > indent))
            {
                var nextIndent = NextNonEmptyLineIndent(document, lineNum);
                if (nextIndent > indent)
                {
                    indentStack.Push((indent, line.Offset, lineNum));
                }
            }
        }

        // Close remaining open blocks
        while (indentStack.Count > 0)
        {
            var block = indentStack.Pop();
            var endOffset = FindLastNonEmptyLineEnd(document, block.startLine + 1, lineCount);
            if (endOffset > block.startOffset && (lineCount - block.startLine) >= _minimumLines)
            {
                foldings.Add(new NewFolding(block.startOffset, endOffset) { Name = "..." });
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static int GetIndentLevel(string line)
    {
        var count = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') count++;
            else if (ch == '\t') count += 4;
            else break;
        }
        return count;
    }

    private static int NextNonEmptyLineIndent(TextDocument document, int currentLineNum)
    {
        for (var i = currentLineNum + 1; i <= document.LineCount; i++)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (!string.IsNullOrWhiteSpace(text))
                return GetIndentLevel(text);
        }
        return 0;
    }

    private static int FindLastNonEmptyLineEnd(TextDocument document, int fromLine, int toLine)
    {
        for (var i = toLine; i >= fromLine; i--)
        {
            var line = document.GetLineByNumber(i);
            var text = document.GetText(line.Offset, line.Length);
            if (!string.IsNullOrWhiteSpace(text))
                return line.EndOffset;
        }
        return document.GetLineByNumber(fromLine).EndOffset;
    }
}
