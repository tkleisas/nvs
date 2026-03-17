namespace NVS.Behaviors;

/// <summary>
/// Finds matching bracket pairs in text using stack-based matching.
/// </summary>
public static class BracketMatcher
{
    private static readonly Dictionary<char, char> OpenToClose = new()
    {
        ['('] = ')', ['{'] = '}', ['['] = ']',
    };

    private static readonly Dictionary<char, char> CloseToOpen = new()
    {
        [')'] = '(', ['}'] = '{', [']'] = '[',
    };

    /// <summary>
    /// Given a caret offset, finds the matching bracket pair positions.
    /// Returns null if no bracket is at or adjacent to the caret.
    /// </summary>
    public static BracketPair? FindMatchingBracket(string text, int caretOffset)
    {
        if (string.IsNullOrEmpty(text) || caretOffset < 0 || caretOffset > text.Length)
            return null;

        // Check character at caret position
        if (caretOffset < text.Length)
        {
            var ch = text[caretOffset];
            if (OpenToClose.ContainsKey(ch))
                return FindClosing(text, caretOffset);
            if (CloseToOpen.ContainsKey(ch))
                return FindOpening(text, caretOffset);
        }

        // Check character before caret (common when caret is right after a bracket)
        if (caretOffset > 0)
        {
            var ch = text[caretOffset - 1];
            if (OpenToClose.ContainsKey(ch))
                return FindClosing(text, caretOffset - 1);
            if (CloseToOpen.ContainsKey(ch))
                return FindOpening(text, caretOffset - 1);
        }

        return null;
    }

    private static BracketPair? FindClosing(string text, int openPos)
    {
        var openChar = text[openPos];
        var closeChar = OpenToClose[openChar];
        var depth = 1;

        for (var i = openPos + 1; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == openChar) depth++;
            else if (ch == closeChar)
            {
                depth--;
                if (depth == 0)
                    return new BracketPair(openPos, i);
            }
        }

        return null;
    }

    private static BracketPair? FindOpening(string text, int closePos)
    {
        var closeChar = text[closePos];
        var openChar = CloseToOpen[closeChar];
        var depth = 1;

        for (var i = closePos - 1; i >= 0; i--)
        {
            var ch = text[i];
            if (ch == closeChar) depth++;
            else if (ch == openChar)
            {
                depth--;
                if (depth == 0)
                    return new BracketPair(i, closePos);
            }
        }

        return null;
    }
}

public record BracketPair(int OpenOffset, int CloseOffset);
