namespace NVS.Core.Models.Settings;

public sealed record ThemeColors
{
    public required string EditorBackground { get; init; }
    public required string EditorForeground { get; init; }
    public required string EditorLineHighlight { get; init; }
    public required string EditorSelectionBackground { get; init; }
    public required string SidebarBackground { get; init; }
    public required string SidebarForeground { get; init; }
    public required string StatusBarBackground { get; init; }
    public required string StatusBarForeground { get; init; }
    public required string ToolPanelBackground { get; init; }
    public required string ToolPanelForeground { get; init; }
    public required string AccentColor { get; init; }
    public required string BorderColor { get; init; }
    public required string ButtonBackground { get; init; }
    public required string ButtonForeground { get; init; }
    public required string InputBackground { get; init; }
    public required string InputForeground { get; init; }
    public required string InputBorder { get; init; }
    public required string TabActiveBackground { get; init; }
    public required string TabInactiveBackground { get; init; }
    public required string TabActiveForeground { get; init; }
    public required string TabInactiveForeground { get; init; }
    public required string MenuBackground { get; init; }
    public required string MenuForeground { get; init; }
    public required string InfoBarInfoBackground { get; init; }
    public required string InfoBarWarningBackground { get; init; }
    public required string InfoBarErrorBackground { get; init; }
    public required string InfoBarForeground { get; init; }
    public required string ScrollBarBackground { get; init; }
    public required string ScrollBarThumb { get; init; }
    public required string LineNumberForeground { get; init; }
}
