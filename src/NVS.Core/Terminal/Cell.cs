namespace NVS.Core.Terminal;

/// <summary>
/// One terminal cell: a codepoint plus its rendition (SGR) state. Reference semantics
/// would force per-cell allocation; we keep cells as structs so a screen grid is one
/// flat array. Mutable by design — the VT parser writes into it.
/// </summary>
public struct Cell
{
    /// <summary>The character to display. A space (' ') represents an empty cell.</summary>
    public char Char;

    /// <summary>Foreground color index into <see cref="TerminalColors"/>, or a packed ARGB when high bit set.</summary>
    public int Foreground;

    /// <summary>Background color index, or packed ARGB when high bit set.</summary>
    public int Background;

    /// <summary>SGR rendition flags: bold, italic, underline, inverse, etc.</summary>
    public RenditionFlags Rendition;

    public static Cell Blank => new() { Char = ' ', Foreground = DefaultForeground, Background = DefaultBackground, Rendition = RenditionFlags.None };

    /// <summary> Sentinel meaning "use the default foreground" (terminal 'default' color).</summary>
    public const int DefaultForeground = -1;
    public const int DefaultBackground = -2;

    /// <summary>High bit of the int distinguishes indexed colors (0..255) from packed ARGB.</summary>
    public const int PackedFlag = unchecked((int)0x80000000);
}

[Flags]
public enum RenditionFlags : byte
{
    None = 0,
    Bold = 1 << 0,
    Italic = 1 << 1,
    Underline = 1 << 2,
    Inverse = 1 << 3,
    Strikethrough = 1 << 4,
    /// <summary>SGR 1 followed by SGR 22; some terminals render faint instead. Treat as bold.</summary>
    Faint = 1 << 5,
}