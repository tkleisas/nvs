namespace NVS.Behaviors;

/// <summary>
/// Manages multiple cursor positions for multi-caret editing.
/// Pure logic class — testable without Avalonia dependencies.
/// </summary>
public sealed class MultiCursorState
{
    private readonly List<int> _cursors = [];

    /// <summary>Read-only view of secondary cursor offsets (sorted ascending).</summary>
    public IReadOnlyList<int> Cursors => _cursors.AsReadOnly();

    /// <summary>Whether multi-cursor mode is active.</summary>
    public bool IsActive => _cursors.Count > 0;

    /// <summary>Adds a secondary cursor at the given offset if not already present.</summary>
    public void AddCursor(int offset)
    {
        if (!_cursors.Contains(offset))
        {
            _cursors.Add(offset);
            _cursors.Sort();
        }
    }

    /// <summary>Removes a specific cursor.</summary>
    public void RemoveCursor(int offset) => _cursors.Remove(offset);

    /// <summary>Clears all secondary cursors (Escape).</summary>
    public void Clear() => _cursors.Clear();

    /// <summary>
    /// Adds a cursor one line above or below the primary caret.
    /// Uses the line offset lookup to translate line/column to offset.
    /// </summary>
    public int? AddCursorFromLine(int primaryOffset, int targetLineStartOffset, int targetLineLength, int columnInLine)
    {
        var clampedColumn = Math.Min(columnInLine, targetLineLength);
        var newOffset = targetLineStartOffset + clampedColumn;

        if (newOffset == primaryOffset || _cursors.Contains(newOffset))
            return null;

        _cursors.Add(newOffset);
        _cursors.Sort();
        return newOffset;
    }

    /// <summary>
    /// Adjusts all cursor positions after text was inserted at the primary cursor.
    /// The primary cursor offset has already been updated by AvaloniaEdit.
    /// Returns edit operations (offset, text) for secondary cursors, ordered high-to-low.
    /// </summary>
    public IReadOnlyList<(int Offset, string Text)> GetInsertEdits(int primaryOldOffset, string insertedText)
    {
        var edits = new List<(int Offset, string Text)>();
        int insertLength = insertedText.Length;

        // Adjust secondary cursor positions for the primary insert
        for (int i = 0; i < _cursors.Count; i++)
        {
            if (_cursors[i] >= primaryOldOffset)
                _cursors[i] += insertLength;
        }

        // Generate edits for each secondary cursor (high-to-low to preserve offsets)
        for (int i = _cursors.Count - 1; i >= 0; i--)
        {
            edits.Add((_cursors[i], insertedText));
        }

        return edits;
    }

    /// <summary>
    /// Adjusts all cursor positions after secondary edits were applied.
    /// Call after applying all secondary inserts.
    /// </summary>
    public void AdjustAfterSecondaryInserts(string insertedText)
    {
        int insertLength = insertedText.Length;

        // Each secondary cursor that had an insert shifts all cursors after it
        // Since we applied high-to-low, we need to adjust from low-to-high
        for (int i = 0; i < _cursors.Count; i++)
        {
            // Each cursor at index i has i cursors before it that also inserted text
            _cursors[i] += i * insertLength;
        }
    }

    /// <summary>
    /// Returns delete operations for backspace at all secondary cursors.
    /// Returns offsets where one character should be removed, ordered high-to-low.
    /// </summary>
    public IReadOnlyList<int> GetBackspaceEdits(int primaryOldOffset)
    {
        var edits = new List<int>();

        // Adjust for primary backspace (removed 1 char before primaryOldOffset)
        for (int i = 0; i < _cursors.Count; i++)
        {
            if (_cursors[i] > primaryOldOffset)
                _cursors[i] -= 1;
        }

        // Return secondary cursor positions for backspace (high-to-low)
        for (int i = _cursors.Count - 1; i >= 0; i--)
        {
            if (_cursors[i] > 0)
                edits.Add(_cursors[i]);
        }

        return edits;
    }

    /// <summary>
    /// Adjusts secondary cursors after backspace operations were applied.
    /// </summary>
    public void AdjustAfterSecondaryBackspaces()
    {
        // Each secondary backspace removes 1 char, shifting cursors after it
        for (int i = 0; i < _cursors.Count; i++)
        {
            _cursors[i] -= i;
        }

        // Remove any cursors that ended up at invalid positions
        _cursors.RemoveAll(c => c < 0);
    }

    /// <summary>
    /// Finds all occurrences of the given text in the document and returns their offsets.
    /// Used for Ctrl+D (select next occurrence).
    /// </summary>
    public static List<int> FindAllOccurrences(string documentText, string searchText, StringComparison comparison = StringComparison.Ordinal)
    {
        var results = new List<int>();
        if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(documentText))
            return results;

        int index = 0;
        while ((index = documentText.IndexOf(searchText, index, comparison)) != -1)
        {
            results.Add(index);
            index += searchText.Length;
        }

        return results;
    }

    /// <summary>
    /// Finds the next occurrence of searchText after the given offset.
    /// Returns -1 if not found. Wraps around to beginning.
    /// </summary>
    public static int FindNextOccurrence(string documentText, string searchText, int afterOffset, StringComparison comparison = StringComparison.Ordinal)
    {
        if (string.IsNullOrEmpty(searchText) || string.IsNullOrEmpty(documentText))
            return -1;

        // Search after current offset
        int index = documentText.IndexOf(searchText, afterOffset, comparison);
        if (index != -1)
            return index;

        // Wrap around
        index = documentText.IndexOf(searchText, 0, comparison);
        return index;
    }
}
