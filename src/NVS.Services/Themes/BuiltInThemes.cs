using NVS.Core.Models.Settings;

namespace NVS.Services.Themes;

internal static class BuiltInThemes
{
    public static AppTheme NvsDark { get; } = new()
    {
        Name = "NVS Dark",
        ThemeVariant = "Dark",
        Colors = new ThemeColors
        {
            EditorBackground = "#1E1E1E",
            EditorForeground = "#D4D4D4",
            EditorLineHighlight = "#2A2D2E",
            EditorSelectionBackground = "#264F78",
            SidebarBackground = "#252526",
            SidebarForeground = "#CCCCCC",
            StatusBarBackground = "#007ACC",
            StatusBarForeground = "#FFFFFF",
            ToolPanelBackground = "#1E1E1E",
            ToolPanelForeground = "#CCCCCC",
            AccentColor = "#007ACC",
            BorderColor = "#3C3C3C",
            ButtonBackground = "#0E639C",
            ButtonForeground = "#FFFFFF",
            InputBackground = "#3C3C3C",
            InputForeground = "#CCCCCC",
            InputBorder = "#3C3C3C",
            TabActiveBackground = "#1E1E1E",
            TabInactiveBackground = "#2D2D2D",
            TabActiveForeground = "#FFFFFF",
            TabInactiveForeground = "#969696",
            MenuBackground = "#252526",
            MenuForeground = "#CCCCCC",
            InfoBarInfoBackground = "#007ACC",
            InfoBarWarningBackground = "#CC6600",
            InfoBarErrorBackground = "#CC0000",
            InfoBarForeground = "#FFFFFF",
            ScrollBarBackground = "#1E1E1E",
            ScrollBarThumb = "#424242",
            LineNumberForeground = "#858585",
        },
    };

    public static AppTheme NvsLight { get; } = new()
    {
        Name = "NVS Light",
        ThemeVariant = "Light",
        Colors = new ThemeColors
        {
            EditorBackground = "#FFFFFF",
            EditorForeground = "#1E1E1E",
            EditorLineHighlight = "#F0F0F0",
            EditorSelectionBackground = "#ADD6FF",
            SidebarBackground = "#F3F3F3",
            SidebarForeground = "#333333",
            StatusBarBackground = "#007ACC",
            StatusBarForeground = "#FFFFFF",
            ToolPanelBackground = "#F3F3F3",
            ToolPanelForeground = "#333333",
            AccentColor = "#007ACC",
            BorderColor = "#CECECE",
            ButtonBackground = "#007ACC",
            ButtonForeground = "#FFFFFF",
            InputBackground = "#FFFFFF",
            InputForeground = "#333333",
            InputBorder = "#CECECE",
            TabActiveBackground = "#FFFFFF",
            TabInactiveBackground = "#ECECEC",
            TabActiveForeground = "#333333",
            TabInactiveForeground = "#717171",
            MenuBackground = "#F3F3F3",
            MenuForeground = "#333333",
            InfoBarInfoBackground = "#007ACC",
            InfoBarWarningBackground = "#E8A200",
            InfoBarErrorBackground = "#E51400",
            InfoBarForeground = "#FFFFFF",
            ScrollBarBackground = "#F3F3F3",
            ScrollBarThumb = "#C1C1C1",
            LineNumberForeground = "#6E7681",
        },
    };

    public static AppTheme Monokai { get; } = new()
    {
        Name = "Monokai",
        ThemeVariant = "Dark",
        Colors = new ThemeColors
        {
            EditorBackground = "#272822",
            EditorForeground = "#F8F8F2",
            EditorLineHighlight = "#3E3D32",
            EditorSelectionBackground = "#49483E",
            SidebarBackground = "#1E1F1C",
            SidebarForeground = "#D4D4D4",
            StatusBarBackground = "#414339",
            StatusBarForeground = "#F8F8F2",
            ToolPanelBackground = "#1E1F1C",
            ToolPanelForeground = "#D4D4D4",
            AccentColor = "#A6E22E",
            BorderColor = "#414339",
            ButtonBackground = "#75715E",
            ButtonForeground = "#F8F8F2",
            InputBackground = "#414339",
            InputForeground = "#F8F8F2",
            InputBorder = "#75715E",
            TabActiveBackground = "#272822",
            TabInactiveBackground = "#1E1F1C",
            TabActiveForeground = "#F8F8F2",
            TabInactiveForeground = "#75715E",
            MenuBackground = "#1E1F1C",
            MenuForeground = "#F8F8F2",
            InfoBarInfoBackground = "#66D9EF",
            InfoBarWarningBackground = "#E6DB74",
            InfoBarErrorBackground = "#F92672",
            InfoBarForeground = "#272822",
            ScrollBarBackground = "#272822",
            ScrollBarThumb = "#414339",
            LineNumberForeground = "#90908A",
        },
    };

    public static AppTheme SolarizedDark { get; } = new()
    {
        Name = "Solarized Dark",
        ThemeVariant = "Dark",
        Colors = new ThemeColors
        {
            EditorBackground = "#002B36",
            EditorForeground = "#839496",
            EditorLineHighlight = "#073642",
            EditorSelectionBackground = "#073642",
            SidebarBackground = "#00212B",
            SidebarForeground = "#93A1A1",
            StatusBarBackground = "#073642",
            StatusBarForeground = "#93A1A1",
            ToolPanelBackground = "#00212B",
            ToolPanelForeground = "#93A1A1",
            AccentColor = "#268BD2",
            BorderColor = "#073642",
            ButtonBackground = "#268BD2",
            ButtonForeground = "#FDF6E3",
            InputBackground = "#073642",
            InputForeground = "#93A1A1",
            InputBorder = "#586E75",
            TabActiveBackground = "#002B36",
            TabInactiveBackground = "#00212B",
            TabActiveForeground = "#93A1A1",
            TabInactiveForeground = "#586E75",
            MenuBackground = "#00212B",
            MenuForeground = "#93A1A1",
            InfoBarInfoBackground = "#268BD2",
            InfoBarWarningBackground = "#B58900",
            InfoBarErrorBackground = "#DC322F",
            InfoBarForeground = "#FDF6E3",
            ScrollBarBackground = "#002B36",
            ScrollBarThumb = "#073642",
            LineNumberForeground = "#586E75",
        },
    };

    public static IReadOnlyList<AppTheme> All { get; } =
    [
        NvsDark,
        NvsLight,
        Monokai,
        SolarizedDark,
    ];
}
