using System.Text;

namespace NVS.Core.Terminal;

/// <summary>
/// A logical terminal screen — a mutable grid of <see cref="Cell"/>s plus a cursor and an
/// optional scrollback history ring. Implements only the data model; the <see cref="VtParser"/>
/// drives it. The grid grows rows as needed (line-wrap), but the column count is fixed at
/// construction (resizes reallocate). Thread safety is NOT provided — call from the parser's
/// owning thread only (the renderer marshals reads via <see cref="Snapshot"/>).
/// </summary>
public sealed class TerminalScreen
{
    private Cell[] _cells;
    private int _cols;
    private int _rows;
    private int _cursorRow;
    private int _cursorCol;
    private bool _wrapPending;     // SGR-style line-wrap deferred until next char (DEC VT220 like)

    // Scrollback ring of removed-from-top lines (newest at the end).
    private readonly List<Cell[]> _scrollback = [];
    private int _maxScrollback;

    public int Columns => _cols;
    public int Rows => _rows;
    public int CursorRow => _cursorRow;
    public int CursorCol => _cursorCol;
    public int ScrollbackCount => _scrollback.Count;
    public int MaxScrollback => _maxScrollback;

    /// <summary>Saved cursor (DECSC/DECRC, ESC 7 / ESC 8). Null until saved.</summary>
    public (int Row, int Col)? SavedCursor { get; set; }

    public TerminalScreen(int cols, int rows, int maxScrollback = 5000)
    {
        if (cols <= 0) throw new ArgumentOutOfRangeException(nameof(cols));
        if (rows <= 0) throw new ArgumentOutOfRangeException(nameof(rows));
        _cols = cols;
        _rows = rows;
        _maxScrollback = Math.Max(0, maxScrollback);
        _cells = new Cell[cols * rows];
        Clear();
    }

    /// <summary>Current rendition applied to newly written cells (SGR state).</summary>
    public Cell SgrState { get; set; } = Cell.Blank;

    /// <summary>Resizes the grid. Contents in the top-left are preserved; new cells are blank.
    /// Cursor is clamped to the new bounds.</summary>
    public void Resize(int newCols, int newRows)
    {
        if (newCols <= 0 || newRows <= 0) return;
        var newCells = new Cell[newCols * newRows];
        for (int r = 0; r < Math.Min(_rows, newRows); r++)
            for (int c = 0; c < Math.Min(_cols, newCols); c++)
                newCells[r * newCols + c] = _cells[r * _cols + c];
        // Fill new cells with blank (struct fields default to 0 — Char becomes '\0' which is NOT space; reset explicitly).
        for (int i = 0; i < newCells.Length; i++)
            if (newCells[i].Char == '\0')
                newCells[i] = Cell.Blank;

        _cols = newCols;
        _rows = newRows;
        _cells = newCells;
        _cursorRow = Math.Clamp(_cursorRow, 0, _rows - 1);
        _cursorCol = Math.Clamp(_cursorCol, 0, _cols - 1);
    }

    /// <summary>Clears the screen and scrollback. Resets cursor to (0,0).</summary>
    public void Clear()
    {
        for (int i = 0; i < _cells.Length; i++) _cells[i] = Cell.Blank;
        _scrollback.Clear();
        _cursorRow = 0;
        _cursorCol = 0;
        _wrapPending = false;
    }

    // ── Cursor movements ───────────────────────────────────────────

    public void MoveCursor(int row, int col)
    {
        _cursorRow = Math.Clamp(row, 0, _rows - 1);
        _cursorCol = Math.Clamp(col, 0, _cols - 1);
        _wrapPending = false;
    }

    public void MoveCursorRelative(int dRow, int dCol)
    {
        MoveCursor(_cursorRow + dRow, _cursorCol + dCol);
    }

    public void CursorUp(int n = 1)
    {
        MoveCursorRelative(-Math.Max(1, n), 0);
    }

    public void CursorDown(int n = 1)
    {
        MoveCursorRelative(Math.Max(1, n), 0);
    }

    public void CursorForward(int n = 1)
    {
        MoveCursorRelative(0, Math.Max(1, n));
    }

    public void CursorBack(int n = 1)
    {
        MoveCursorRelative(0, -Math.Max(1, n));
    }

    public void CursorNextLine(int n = 1)
    {
        MoveCursor(_cursorRow + Math.Max(1, n), 0);
    }

    public void CursorPrevLine(int n = 1)
    {
        MoveCursor(_cursorRow - Math.Max(1, n), 0);
    }

    public void CursorColumn(int col) => MoveCursor(_cursorRow, col);
    public void CursorRowAbsolute(int row) => MoveCursor(row, _cursorCol);

    // ── Erase ──────────────────────────────────────────────────────

    /// <summary>Erase in display: 0=cursor-to-end, 1=start-to-cursor, 2=whole screen.</summary>
    public void EraseDisplay(int mode)
    {
        switch (mode)
        {
            case 0:
                EraseSpan(_cursorRow, _cursorCol, _rows - 1, _cols - 1);
                break;
            case 1:
                EraseSpan(0, 0, _cursorRow, _cursorCol);
                break;
            case 2:
            case 3: // 3 = scrollback erase — treat like full clear
                for (int i = 0; i < _cells.Length; i++) _cells[i] = Cell.Blank;
                if (mode == 3) _scrollback.Clear();
                break;
        }
    }

    /// <summary>Erase in line: 0=cursor-to-end, 1=start-to-cursor, 2=whole line.</summary>
    public void EraseLine(int mode)
    {
        switch (mode)
        {
            case 0: EraseRowSpan(_cursorRow, _cursorCol, _cols - 1); break;
            case 1: EraseRowSpan(_cursorRow, 0, _cursorCol); break;
            case 2: EraseRowSpan(_cursorRow, 0, _cols - 1); break;
        }
    }

    private void EraseSpan(int r0, int c0, int r1, int c1)
    {
        if (r0 < 0 || r0 >= _rows) return;
        if (r1 < 0 || r1 >= _rows) return;
        if (r0 > r1) return;
        if (r0 == r1) { EraseRowSpan(r0, c0, c1); return; }
        EraseRowSpan(r0, c0, _cols - 1);
        for (int r = r0 + 1; r < r1; r++) EraseRowSpan(r, 0, _cols - 1);
        EraseRowSpan(r1, 0, c1);
    }

    private void EraseRowSpan(int row, int c0, int c1)
    {
        c0 = Math.Max(0, c0);
        c1 = Math.Min(_cols - 1, c1);
        for (int c = c0; c <= c1; c++) _cells[row * _cols + c] = Cell.Blank;
    }

    // ── Scrolling ──────────────────────────────────────────────────

    /// <summary>Scroll the contents up by n lines (new blank rows at the bottom). The
    /// removed lines are appended to scrollback.</summary>
    public void ScrollUp(int n = 1)
    {
        n = Math.Max(1, n);
        if (n >= _rows)
        {
            for (int i = 0; i < n && _rows > 0; i++) AppendScrollbackRow(RowCopy(0));
            Clear();
            return;
        }
        for (int i = 0; i < n; i++) AppendScrollbackRow(RowCopy(i));
        Array.Copy(_cells, n * _cols, _cells, 0, (_rows - n) * _cols);
        for (int r = _rows - n; r < _rows; r++) EraseRowSpan(r, 0, _cols - 1);
    }

    /// <summary>Scroll the contents down by n lines (new blank rows at the top).</summary>
    public void ScrollDown(int n = 1)
    {
        n = Math.Max(1, Math.Min(n, _rows));
        Array.Copy(_cells, 0, _cells, n * _cols, (_rows - n) * _cols);
        for (int r = 0; r < n; r++) EraseRowSpan(r, 0, _cols - 1);
    }

    // ── Insert / delete within rows (IL/DL/ICH/DCH —— terminal editing CSI) ─

    /// <summary>Inserts n blank lines at the cursor row, scrolling the rest down.</summary>
    public void InsertLines(int n = 1)
    {
        n = Math.Max(1, Math.Min(n, _rows - _cursorRow));
        if (n <= 0 || _cursorRow >= _rows) return;
        // Shift rows [cursorRow, rows - n - 1] down by n.
        for (int r = _rows - 1; r >= _cursorRow + n; r--)
            Array.Copy(_cells, (r - n) * _cols, _cells, r * _cols, _cols);
        for (int r = _cursorRow; r < _cursorRow + n && r < _rows; r++) EraseRowSpan(r, 0, _cols - 1);
    }

    /// <summary>Deletes n lines at the cursor row, scrolling subsequent lines up.</summary>
    public void DeleteLines(int n = 1)
    {
        n = Math.Max(1, Math.Min(n, _rows - _cursorRow));
        if (n <= 0 || _cursorRow >= _rows) return;
        for (int r = _cursorRow; r < _rows - n; r++)
            Array.Copy(_cells, (r + n) * _cols, _cells, r * _cols, _cols);
        for (int r = _rows - n; r < _rows; r++) EraseRowSpan(r, 0, _cols - 1);
    }

    /// <summary>Inserts n blank chars at the cursor, shifting the rest of the row right.</summary>
    public void InsertChars(int n = 1)
    {
        n = Math.Max(1, n);
        int rowStart = _cursorRow * _cols;
        int max = _cols - _cursorCol;
        if (n >= max) { EraseRowSpan(_cursorRow, _cursorCol, _cols - 1); return; }
        for (int c = _cols - 1; c >= _cursorCol + n; c--)
            _cells[rowStart + c] = _cells[rowStart + c - n];
        for (int c = _cursorCol; c < _cursorCol + n; c++) _cells[rowStart + c] = Cell.Blank;
    }

    /// <summary>Deletes n chars at the cursor, shifting the rest of the row left.</summary>
    public void DeleteChars(int n = 1)
    {
        n = Math.Max(1, n);
        int rowStart = _cursorRow * _cols;
        int max = _cols - _cursorCol;
        if (n >= max) { EraseRowSpan(_cursorRow, _cursorCol, _cols - 1); return; }
        for (int c = _cursorCol; c < _cols - n; c++)
            _cells[rowStart + c] = _cells[rowStart + c + n];
        for (int c = _cols - n; c < _cols; c++) _cells[rowStart + c] = Cell.Blank;
    }

    /// <summary>Erase n chars at the cursor (cells become blank; nothing shifts).</summary>
    public void EraseChars(int n = 1)
    {
        n = Math.Max(1, n);
        int rowStart = _cursorRow * _cols;
        int end = Math.Min(_cols, _cursorCol + n);
        for (int c = _cursorCol; c < end; c++) _cells[rowStart + c] = Cell.Blank;
    }

    private void AppendScrollbackRow(Cell[] row)
    {
        if (_maxScrollback == 0) return;
        _scrollback.Add(row);
        if (_scrollback.Count > _maxScrollback)
            _scrollback.RemoveRange(0, _scrollback.Count - _maxScrollback);
    }

    private Cell[] RowCopy(int row)
    {
        var arr = new Cell[_cols];
        Array.Copy(_cells, row * _cols, arr, 0, _cols);
        return arr;
    }

    // ── Writing ────────────────────────────────────────────────────

    /// <summary>
    /// Writes a single decoded character at the cursor using <see cref="SgrState"/>. Handles
    /// line-wrap (deferred) and scroll-on-last-line per DEC VT220 semantics.
    /// </summary>
    public void PutChar(char ch)
    {
        if (_wrapPending)
        {
            _cursorCol = 0;
            _cursorRow++;
            _wrapPending = false;
            if (_cursorRow >= _rows)
            {
                _cursorRow = _rows - 1;
                ScrollUp(1);
            }
        }

        var cell = SgrState;
        cell.Char = ch;
        _cells[_cursorRow * _cols + _cursorCol] = cell;

        _cursorCol++;
        if (_cursorCol >= _cols)
        {
            _cursorCol = _cols - 1;
            _wrapPending = true;
        }
    }

    /// <summary>Carriage return — cursor to column 0, same row.</summary>
    public void CarriageReturn()
    {
        _cursorCol = 0;
        _wrapPending = false;
    }

    /// <summary>Line feed — move down, scroll if at the bottom. Does NOT do CR (DEC LF vs NEL).</summary>
    public void LineFeed()
    {
        if (_cursorRow == _rows - 1) ScrollUp(1);
        else _cursorRow++;
        _wrapPending = false;
    }

    /// <summary>Backspace — cursor one column left, no-op at column 0.</summary>
    public void Backspace()
    {
        if (_cursorCol > 0) _cursorCol--;
        _wrapPending = false;
    }

    /// <summary>Tab — advance to the next multiple of 8, wrapping if needed.</summary>
    public void Tab()
    {
        int next = (_cursorCol / 8 + 1) * 8;
        if (next >= _cols) { _cursorCol = _cols - 1; _wrapPending = true; }
        else _cursorCol = next;
    }

    // ── SGR ────────────────────────────────────────────────────────

    /// <summary>Applies a single SGR parameter list to <see cref="SgrState"/>. Handles
    /// the common cases (reset, bold/italic/underline/inverse, foreground/background by
    /// 16/256/truecolor). Unsupported codes are ignored.</summary>
    public void ApplySgr(IReadOnlyList<int> p)
    {
        if (p.Count == 0) { SgrState = SgrState with { Rendition = RenditionFlags.None, Foreground = Cell.DefaultForeground, Background = Cell.DefaultBackground }; return; }

        var state = SgrState;
        var flags = state.Rendition;
        int fg = state.Foreground, bg = state.Background;

        for (int i = 0; i < p.Count; i++)
        {
            int code = p[i];
            switch (code)
            {
                case 0:  flags = RenditionFlags.None; fg = Cell.DefaultForeground; bg = Cell.DefaultBackground; break;
                case 1:  flags |= RenditionFlags.Bold; break;
                case 2:  flags |= RenditionFlags.Faint; break;
                case 3:  flags |= RenditionFlags.Italic; break;
                case 4:  flags |= RenditionFlags.Underline; break;
                case 7:  flags |= RenditionFlags.Inverse; break;
                case 9:  flags |= RenditionFlags.Strikethrough; break;
                case 22: flags &= ~(RenditionFlags.Bold | RenditionFlags.Faint); break;
                case 23: flags &= ~RenditionFlags.Italic; break;
                case 24: flags &= ~RenditionFlags.Underline; break;
                case 27: flags &= ~RenditionFlags.Inverse; break;
                case 29: flags &= ~RenditionFlags.Strikethrough; break;
                case 39: fg = Cell.DefaultForeground; break;
                case 49: bg = Cell.DefaultBackground; break;
                case >= 30 and <= 37:  fg = code - 30; break;
                case >= 40 and <= 47:  bg = code - 40; break;
                case >= 90 and <= 97:   fg = code - 90 + 8; break;
                case >= 100 and <= 107: bg = code - 100 + 8; break;
                case 38:
                case 48:
                    if (i + 1 < p.Count)
                    {
                        int mode = p[i + 1];
                        if (mode == 5 && i + 2 < p.Count) // 256-color
                        {
                            if (code == 38) fg = p[i + 2] & 0xFF; else bg = p[i + 2] & 0xFF;
                            i += 2;
                        }
                        else if (mode == 2 && i + 4 < p.Count) // truecolor r g b
                        {
                            int r = p[i + 2] & 0xFF, g = p[i + 3] & 0xFF, b = p[i + 4] & 0xFF;
                            int packed = Cell.PackedFlag | (0xFF << 24) | (r << 16) | (g << 8) | b;
                            if (code == 38) fg = packed; else bg = packed;
                            i += 4;
                        }
                    }
                    break;
            }
        }

        SgrState = state with { Rendition = flags, Foreground = fg, Background = bg };
    }

    // ── Snapshot for rendering ─────────────────────────────────────

    /// <summary>
    /// Returns a *copy* of the visible grid (no scrollback) so the renderer can read it
    /// without holding a lock. Caller-disposed. Cell structs copy by value.
    /// </summary>
    public Cell[] Snapshot()
    {
        var snap = new Cell[_cells.Length];
        Array.Copy(_cells, snap, _cells.Length);
        return snap;
    }

    /// <summary>Returns up to <paramref name="maxLines"/> scrollback rows (oldest first).</summary>
    public IReadOnlyList<Cell[]> ScrollbackSnapshot(int maxLines)
    {
        if (_scrollback.Count == 0 || maxLines <= 0) return Array.Empty<Cell[]>();
        int start = Math.Max(0, _scrollback.Count - maxLines);
        var arr = new Cell[_scrollback.Count - start][];
        for (int i = 0; i < arr.Length; i++) arr[i] = _scrollback[start + i];
        return arr;
    }

    /// <summary>Dumps the visible grid as plain text — for tests and debug.</summary>
    public string DumpText()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < _rows; r++)
        {
            for (int c = 0; c < _cols; c++) sb.Append(_cells[r * _cols + c].Char);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}