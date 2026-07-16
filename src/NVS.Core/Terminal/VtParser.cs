using System.Text;

namespace NVS.Core.Terminal;

/// <summary>
/// Minimal VT100/xterm parser — UTF-8 input, dispatches:
///   • C0 controls (BS, HT, LF, VT, FF, CR, BEL)
///   • CSI sequences (ESC [ ... letter) — cursor moves, erase, scroll, SGR
///   • OSC strings (ESC ] ... BEL / ST) — dropped (no title support in v1)
///   • printable ASCII / UTF-8 continuation bytes
///
/// NOT supported: DCS / APC / PM, alternate screen, mouse tracking, DEC private modes,
/// bracketed paste, charset designation. All are recognized enough to skip cleanly.
/// Thread safety: NOT provided — single-threaded write side only.
/// </summary>
public sealed class VtParser
{
    private readonly TerminalScreen _screen;
    private readonly Decoder _decoder = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetDecoder();
    private readonly byte[] _utf8Buf = new byte[8];
    private int _utf8Len;

    private enum State
    {
        Ground,
        Esc,        // just saw ESC
        Csi,        // ESC [
        CsiParam,   // within CSI parameter bytes
        Osc,        // ESC ]
        OscString,  // OSC body until BEL/ST
        DcsString,  // ESC P ... ST — skipped
        StringST,   // waiting for ST after OSC/DCS
        EscIntermediate,
    }

    private State _state = State.Ground;
    private readonly List<int> _csiParams = [];
    private bool _csiPrivate; // '?', '>', '=' etc.
    private readonly StringBuilder _oscBuilder = new();
    private bool _oscEsc; // saw ESC within an OSC string → ST = ESC \

    public VtParser(TerminalScreen screen)
    {
        _screen = screen ?? throw new ArgumentNullException(nameof(screen));
    }

    public TerminalScreen Screen => _screen;

    /// <summary>Feeds a chunk of bytes decoded as UTF-8. Bytes spanning chunk boundaries are
    /// buffered internally so callers can pass arbitrarily-split chunks.</summary>
    public void Feed(ReadOnlySpan<byte> bytes) => FeedUtf8(bytes);

    /// <summary>Feeds a UTF-8 string directly (convenience for tests).</summary>
    public void Feed(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        FeedUtf8(bytes);
    }

    private void FeedUtf8(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes) FeedByte(b);
    }

    private void FeedByte(byte b)
    {
        // Fast path: ground state, ASCII printable.
        if (_state == State.Ground && b >= 0x20 && b < 0x7F)
        {
            _screen.PutChar((char)b);
            return;
        }

        // UTF-8 multibyte buffering only matters in Ground state. Inside control sequences
        // we treat bytes as US-ASCII for dispatch (matches DEC VT behavior).
        if (_state == State.Ground && b >= 0x80)
        {
            if (!FeedUtf8Byte(b)) return;
            return;
        }

        TransitionByte(b);
    }

    private bool FeedUtf8Byte(byte b)
    {
        _utf8Buf[_utf8Len++] = b;
        // Determine whether we've collected a complete UTF-8 sequence by counting leading ones.
        int expected = _utf8Buf[0] switch
        {
            0xFE => -1, // invalid
            < 0x80 => 1, // unexpected in this branch
            < 0xC0 => -1,
            < 0xE0 => 2,
            < 0xF0 => 3,
            < 0xF8 => 4,
            _ => -1,
        } - 1; // bytes left after the leader

        if (expected < 0) { _utf8Len = 0; return false; }
        if (_utf8Len < 1 + expected) return false; // need more

        // Decode the buffered sequence.
        try
        {
            var s = Encoding.UTF8.GetString(_utf8Buf, 0, _utf8Len);
            foreach (var ch in s) _screen.PutChar(ch);
        }
        catch { /* corrupt sequence — drop */ }
        _utf8Len = 0;
        return true;
    }

    private void TransitionByte(byte b)
    {
        switch (_state)
        {
            case State.Ground:
                GroundByte(b);
                break;
            case State.Esc:
                EscByte(b);
                break;
            case State.Csi:
            case State.CsiParam:
                CsiByte(b);
                break;
            case State.Osc:
                if (b == 0x07) { _state = State.Ground; _oscBuilder.Clear(); }
                else { _oscBuilder.Append((char)b); _state = State.OscString; }
                break;
            case State.OscString:
                OscStringByte(b);
                break;
            case State.DcsString:
                if (b == 0x1B) _oscEsc = true;
                else if (_oscEsc && b == 0x5C) { _oscEsc = false; _state = State.Ground; }
                else _oscEsc = false;
                break;
            case State.EscIntermediate:
                if (b >= 0x20 && b < 0x40) { /* swallow */ }
                else if (b >= 0x40 && b < 0x7F) { _state = State.Ground; }
                else _state = State.Ground;
                break;
            default:
                _state = State.Ground;
                break;
        }
    }

    private void GroundByte(byte b)
    {
        switch (b)
        {
            case 0x07: break;               // BEL — v1 ignores (no audio)
            case 0x08: _screen.Backspace(); break;
            case 0x09: _screen.Tab(); break;
            case 0x0A: case 0x0B: case 0x0C: _screen.LineFeed(); break; // LF, VT, FF all move down
            case 0x0D: _screen.CarriageReturn(); break;
            case 0x1B: _state = State.Esc; break; // ESC
            default:
                if (b < 0x20) { /* other C0 ignored */ }
                else if (b < 0x7F) _screen.PutChar((char)b);
                else _screen.PutChar((char)b); // 7-bit high — happens in passthrough mode (rare)
                break;
        }
    }

    private void EscByte(byte b)
    {
        switch (b)
        {
            case (byte)'[':
                _csiParams.Clear();
                _csiPrivate = false;
                _state = State.Csi;
                break;
            case (byte)']': _oscBuilder.Clear(); _state = State.Osc; break;
            case (byte)'P': _oscEsc = false; _state = State.DcsString; break;
            case (byte)'M': _screen.ScrollDown(1); _state = State.Ground; break; // RI — reverse index
            case (byte)'D': _screen.LineFeed(); _state = State.Ground; break;     // IND — index
            case (byte)'E': _screen.CarriageReturn(); _screen.LineFeed(); _state = State.Ground; break; // NEL
            case (byte)'7': _screen.SavedCursor = (_screen.CursorRow, _screen.CursorCol); _state = State.Ground; break; // DECSC
            case (byte)'8': if (_screen.SavedCursor is { } c) _screen.MoveCursor(c.Row, c.Col); _state = State.Ground; break; // DECRC
            case (byte)'=': case (byte)'>': _state = State.Ground; break; // keypad mode — ignore
            case (byte)'#': _state = State.EscIntermediate; break;
            case (byte)'(': case (byte)')': case (byte)'*': case (byte)'+':
                _state = State.EscIntermediate; break; // charset designation — swallow next byte
            default:
                if (b >= 0x20 && b < 0x40) _state = State.EscIntermediate;
                else _state = State.Ground;
                break;
        }
    }

    private void CsiByte(byte b)
    {
        // CSI bytes come in three regions after the leading '[':
        //   0x20-0x2F  parameter/intermediate delimiter '?', '>', etc., or ';'
        //   same region also hosts intermediate bytes
        //   0x40-0x7E  final byte → dispatch
        if (b == (byte)'?' || b == (byte)'>' || b == (byte)'=' || b == (byte)'<')
        {
            _csiPrivate = true;
            return;
        }
        if (b == (byte)';')
        {
            // Empty between ;s is treated as 0 (matches xterm).
            _csiParams.Add(_csiParams.Count == 0 || ((_csiParams.Count) & 1) == 1 && false ? 0 : 0);
            // Simpler: the dispatch converts ";", empty slots to 0 — leave placeholder 0.
            return;
        }
        if (b is >= (byte)'0' and <= (byte)'9')
        {
            int digit = b - '0';
            if (_csiParams.Count == 0) _csiParams.Add(digit);
            else _csiParams[^1] = _csiParams[^1] * 10 + digit;
            return;
        }
        if (b >= 0x20 && b <= 0x2F)
        {
            // intermediate bytes — ignored in v1 (no charset switches etc.)
            return;
        }
        if (b >= 0x40 && b < 0x7F)
        {
            DispatchCsi((char)b);
            _state = State.Ground;
_csiParams.Clear();
                _csiPrivate = false;
                return;
        }
        // Unexpected — reset.
        _state = State.Ground;
        _csiParams.Clear();
    }

    private void DispatchCsi(char final)
    {
        if (_csiPrivate)
        {
            // DEC private modes (e.g. ?25h/l cursor visibility) — ignored in v1 except ?25h/l.
            // Just consume to keep the stream clean; no behavior change.
            return;
        }

        var p = _csiParams;
        int P0() => p.Count > 0 ? p[0] : 0;
        int P(int i, int def) => i < p.Count && p[i] != 0 ? p[i] : def;

        switch (final)
        {
            case 'A': _screen.CursorUp(P(0, 1)); break;
            case 'B':
            case 'e': _screen.CursorDown(P(0, 1)); break;
            case 'C': _screen.CursorForward(P(0, 1)); break;
            case 'D': _screen.CursorBack(P(0, 1)); break;
            case 'F': _screen.CursorPrevLine(P(0, 1)); break;
            case 'E': _screen.CursorNextLine(P(0, 1)); break;
            case 'G': case '`': _screen.CursorColumn(P(0, 1) - 1); break;
            case 'd': _screen.CursorRowAbsolute(P(0, 1) - 1); break;
            case 'H': case 'f': _screen.MoveCursor(P(0, 1) - 1, P(1, 1) - 1); break;
            case 'J': _screen.EraseDisplay(P0()); break;
            case 'K': _screen.EraseLine(P0()); break;
            case 'S': _screen.ScrollUp(P(0, 1)); break;
            case 'T': _screen.ScrollDown(P(0, 1)); break;
            case 'm': _screen.ApplySgr(p); break;
            case 'L': _screen.InsertLines(P(0, 1)); break;
            case 'M': _screen.DeleteLines(P(0, 1)); break;
            case 'P': _screen.DeleteChars(P(0, 1)); break;
            case '@': _screen.InsertChars(P(0, 1)); break;
            case 'X': _screen.EraseChars(P(0, 1)); break;
            case 'r':
                // Set scrolling region (DECSTBM) — v1 ignores (full-screen scroll only).
                break;
            case 'h': case 'l':
                // Mode set/reset — ignored in v1.
                break;
            case 'n':
                // Device status report — not answered in v1 (renderer not interactive).
                break;
            default: break; // unknown final byte — drop
        }
    }

    private void OscStringByte(byte b)
    {
        if (b == 0x07) // BEL terminates OSC
        {
            _state = State.Ground;
            _oscBuilder.Clear();
            return;
        }
        if (b == 0x1B) { _oscEsc = true; return; }
        if (_oscEsc && b == 0x5C) { _oscEsc = false; _state = State.Ground; _oscBuilder.Clear(); return; }
        if (_oscEsc) { _oscEsc = false; }
        _oscBuilder.Append((char)b);
    }
}