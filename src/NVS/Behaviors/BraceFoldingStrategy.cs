using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace NVS.Behaviors;

/// <summary>
/// Folding strategy for C-style languages that fold on matching brace pairs { }.
/// Also handles #region/#endregion directives.
/// </summary>
public sealed class BraceFoldingStrategy
{
    public IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var openBraceStack = new Stack<int>();
        var text = document.Text;

        int regionStart = -1;
        string? regionName = null;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            // Check for #region / #endregion
            if (ch == '#' && i < text.Length - 6)
            {
                var remaining = text.AsSpan(i);
                if (remaining.StartsWith("#region"))
                {
                    regionStart = i;
                    var lineEnd = text.IndexOf('\n', i);
                    regionName = lineEnd > i
                        ? text[(i + 7)..(lineEnd)].Trim().TrimEnd('\r')
                        : "region";
                }
                else if (remaining.StartsWith("#endregion") && regionStart >= 0)
                {
                    var lineEnd = text.IndexOf('\n', i);
                    var endOffset = lineEnd > 0 ? lineEnd : text.Length;
                    foldings.Add(new NewFolding(regionStart, endOffset) { Name = regionName ?? "region" });
                    regionStart = -1;
                    regionName = null;
                }
            }

            // Skip strings and comments to avoid false brace matches
            if (ch == '/' && i + 1 < text.Length)
            {
                if (text[i + 1] == '/')
                {
                    // Line comment — skip to end of line
                    var eol = text.IndexOf('\n', i);
                    i = eol >= 0 ? eol : text.Length - 1;
                    continue;
                }
                if (text[i + 1] == '*')
                {
                    // Block comment — skip to */
                    var endComment = text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = endComment >= 0 ? endComment + 1 : text.Length - 1;
                    continue;
                }
            }

            if (ch == '"' || ch == '\'')
            {
                i = SkipString(text, i, ch);
                continue;
            }

            if (ch == '{')
            {
                openBraceStack.Push(i);
            }
            else if (ch == '}' && openBraceStack.Count > 0)
            {
                var openOffset = openBraceStack.Pop();
                // Only fold if the braces span multiple lines
                var openLine = document.GetLineByOffset(openOffset).LineNumber;
                var closeLine = document.GetLineByOffset(i).LineNumber;
                if (closeLine > openLine)
                {
                    foldings.Add(new NewFolding(openOffset, i + 1) { Name = "..." });
                }
            }
        }

        foldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return foldings;
    }

    private static int SkipString(string text, int startIndex, char quoteChar)
    {
        for (var i = startIndex + 1; i < text.Length; i++)
        {
            if (text[i] == '\\')
            {
                i++; // skip escaped character
                continue;
            }
            if (text[i] == quoteChar)
                return i;
            if (text[i] == '\n')
                return i; // unterminated string, stop at newline
        }
        return text.Length - 1;
    }
}
