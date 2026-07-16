using NVS.Core.Terminal;

namespace NVS.Core.Tests;

public sealed class VtParserTests
{
    private static (VtParser Parser, TerminalScreen Screen) Create(int cols = 80, int rows = 24, int maxScrollback = 5000)
    {
        var screen = new TerminalScreen(cols, rows, maxScrollback);
        return (new VtParser(screen), screen);
    }

    [Fact]
    public void PlainText_WritesToScreen()
    {
        var (p, s) = Create();
        p.Feed("hello");
        s.DumpText().Split('\n')[0][..5].Should().Be("hello");
    }

    [Fact]
    public void Newline_MovesToNextLine()
    {
        var (p, s) = Create(10, 5);
        p.Feed("abc\r\ndef");
        var lines = s.DumpText().Split("\n");
        lines[0].TrimEnd().Should().Be("abc");
        lines[1].TrimEnd().Should().Be("def");
    }

    [Fact]
    public void CarriageReturn_OverwritesLine()
    {
        var (p, s) = Create(10, 5);
        p.Feed("hello\rX");
        s.DumpText().Split('\n')[0][..5].TrimEnd().Should().Be("Xello");
    }

    [Fact]
    public void CursorPositionAbsolute_MovesCursor()
    {
        var (p, s) = Create(10, 5);
        p.Feed("\x1b[2;3H"); // 1-based row 2→0-indexed row 1; col 3→col 2
        p.Feed("OK");
        s.CursorRow.Should().Be(1);
        s.CursorCol.Should().Be(4);
        var lines = s.DumpText().Split("\n");
        lines[1].Substring(2, 2).Should().Be("OK");
    }

    [Fact]
    public void CursorUpDownLeftRight_MovesCorrectly()
    {
        var (p, s) = Create(10, 5);
        p.Feed("\x1b[3;5H\x1b[1A"); // row3→idx2, col5→idx4; CUU 1→row 1 col 4
        s.CursorRow.Should().Be(1);
        s.CursorCol.Should().Be(4);
        p.Feed("\x1b[1B"); // down 1 → row 2
        s.CursorRow.Should().Be(2);
        p.Feed("\x1b[2C"); // forward 2 → col 6
        s.CursorCol.Should().Be(6);
        p.Feed("\x1b[1D"); // back 1 → col 5
        s.CursorCol.Should().Be(5);
    }

    [Fact]
    public void EraseFromCursorToEnd()
    {
        var (p, s) = Create(20, 3);
        p.Feed("ABCDEFGHIJ");
        p.Feed("\x1b[1;5H\x1b[0J"); // move to col 5, erase to end
        var lines = s.DumpText().Split("\n");
        lines[0][..4].Should().Be("ABCD");
        lines[0][5].Should().Be(' '); // Cell.Blank uses space, not \0
    }

    [Fact]
    public void EraseWholeScreen()
    {
        var (p, s) = Create(10, 3);
        p.Feed("text\x1b[2J");
        s.DumpText().Trim().Should().Be(string.Empty);
    }

    [Fact]
    public void Scroll_LongOutputScrollsUpward()
    {
        var (p, s) = Create(10, 3);
        p.Feed("line1\r\nline2\r\nline3\r\nline4\r\n");
        var lines = s.DumpText().Split("\n");
        lines[0].TrimEnd().Should().EndWith("3");
        lines[1].TrimEnd().Should().EndWith("4");
    }

    [Fact]
    public void Scrollback_CapturesScrolledLines()
    {
        var (p, s) = Create(10, 3, maxScrollback: 50);
        p.Feed("line1\r\nline2\r\nline3\r\nline4\r\n");
        s.ScrollbackCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void SgrReset_ClearsRendition()
    {
        var (p, s) = Create();
        p.Feed("\x1b[1;31m"); // bold + red fg
        s.SgrState.Rendition.Should().HaveFlag(RenditionFlags.Bold);
        s.SgrState.Foreground.Should().Be(1);
        p.Feed("\x1b[0m"); // reset
        s.SgrState.Rendition.Should().Be(RenditionFlags.None);
        s.SgrState.Foreground.Should().Be(Cell.DefaultForeground);
    }

    [Fact]
    public void SgrTrueColor_SetsPackedColor()
    {
        var (p, s) = Create();
        p.Feed("\x1b[38;2;255;128;0m"); // orange fg
        s.SgrState.Foreground.Should().Be(unchecked(Cell.PackedFlag | 0xFF) << 24 | (255 << 16) | (128 << 8) | 0);
    }

    [Fact]
    public void Tab_MovesToNextMultipleOf8()
    {
        var (p, s) = Create(80, 3);
        p.Feed("a\tb");
        var line = s.DumpText().Split('\n')[0];
        line[0].Should().Be('a');
        line[8].Should().Be('b');
    }

    [Fact]
    public void Backspace_MovesLeftDroppingChar()
    {
        var (p, s) = Create(10, 3);
        p.Feed("ab\bc");
        var line = s.DumpText().Split('\n')[0];
        line[0..2].Should().Be("ac"); // BS moves left, 'c' overwrites 'b'
    }

    [Fact]
    public void Utf8Multibyte_StoredCorrectly()
    {
        var (p, s) = Create(10, 3);
        p.Feed("cafe\u0301"); // café
        var line = s.DumpText().Split('\n')[0];
        line[4].Should().Be((char)0x301); // combining acute accent
    }

    [Fact]
    public void EraseLine_FromCursorToEnd()
    {
        var (p, s) = Create(10, 3);
        p.Feed("ABCDEFGHIJ");
        p.Feed("\x1b[1;5H\x1b[0K"); // col 5, erase to end of line
        var line = s.DumpText().Split('\n')[0];
        line[..4].Should().Be("ABCD");
        line[5].Should().Be(' ');
    }

    [Fact]
    public void InsertLines_ShiftsDown()
    {
        var (p, s) = Create(10, 6);
        p.Feed("row0\r\nrow1\r\nrow2\r\n");
        p.Feed("\x1b[1;1H\x1b[3L"); // insert 3 lines at top
        s.DumpText().Split('\n')[0].TrimEnd().Should().Be(string.Empty, "top 3 lines inserted as blank");
    }
}