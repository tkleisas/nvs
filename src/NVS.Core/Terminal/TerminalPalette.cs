namespace NVS.Core.Terminal;

/// <summary>
/// Minimal SGR palette — the 16 base ANSI colors (0–15) plus the 6×6×6 cube (16–231) and
/// grayscale ramp (232–255). The terminal's current SGR state uses an index into this
/// table (0..255), the special <see cref="Cell.DefaultForeground"/>/<see cref="Cell.DefaultBackground"/>
/// sentinels, or a packed ARGB int (with <see cref="Cell.PackedFlag"/> set).
/// </summary>
public static class TerminalPalette
{
    // Standard 16 colors (low-intensity + high-intensity variants).
    private static readonly uint[] s_base =
    [
        0xFF000000, // 0 black
        0xFFCD0000, // 1 red
        0xFF00CD00, // 2 green
        0xFFCDCD00,// 3 yellow
        0xFF0000EE,// 4 blue
        0xFFCD00CD,// 5 magenta
        0xFF00CDCD,// 6 cyan
        0xFFE5E5E5,// 7 white (light gray)
        0xFF7F7F7F,// 8 bright black (dark gray)
        0xFFFF0000,// 9 bright red
        0xFF00FF00,// 10 bright green
        0xFFFFFF00,// 11 bright yellow
        0xFF5C5CFF,// 12 bright blue
        0xFFFF00FF,// 13 bright magenta
        0xFF00FFFF,// 14 bright cyan
        0xFFFFFFFF,// 15 bright white
    ];

    private static readonly uint[] s_cube = BuildCube();
    private static readonly uint[] s_gray = BuildGray();

    private static readonly uint[] s_all = BuildAll();

    public static IReadOnlyList<uint> Colors => s_all;

    /// <summary>Resolves an SGR index (0..255) to a packed 0xAARRGGBB color.</summary>
    public static uint Resolve(int index)
    {
        if (index < 0 || index >= s_all.Length) return 0xFFFFFFFF;
        return s_all[index];
    }

    private static uint[] BuildCube()
    {
        var arr = new uint[216]; // 6*6*6
        var steps = new byte[] { 0, 95, 135, 175, 215, 255 };
        for (int r = 0; r < 6; r++)
        for (int g = 0; g < 6; g++)
        for (int b = 0; b < 6; b++)
        {
            var idx = 16 + (r * 36) + (g * 6) + b;
            arr[idx - 16] = 0xFF000000 | ((uint)steps[r] << 16) | ((uint)steps[g] << 8) | steps[b];
        }
        return arr;
    }

    private static uint[] BuildGray()
    {
        var arr = new uint[24]; // 232..255
        for (int i = 0; i < 24; i++)
        {
            var v = 8 + (i * 10);
            if (v > 238) v = 238;
            arr[i] = 0xFF000000 | ((uint)v << 16) | ((uint)v << 8) | (uint)v;
        }
        return arr;
    }

    private static uint[] BuildAll()
    {
        var arr = new uint[256];
        Array.Copy(s_base, 0, arr, 0, 16);
        Array.Copy(s_cube, 0, arr, 16, 216);
        Array.Copy(s_gray, 0, arr, 232, 24);
        return arr;
    }
}