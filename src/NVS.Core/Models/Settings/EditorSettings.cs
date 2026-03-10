namespace NVS.Core.Models.Settings;

public sealed record EditorSettings
{
    public int FontSize { get; init; } = 14;
    public string FontFamily { get; init; } = "JetBrains Mono";
    public int TabSize { get; init; } = 4;
    public bool InsertSpaces { get; init; } = true;
    public bool WordWrap { get; init; } = false;
    public bool LineNumbers { get; init; } = true;
    public bool HighlightCurrentLine { get; init; } = true;
    public bool ShowWhitespace { get; init; } = false;
    public int MinimapEnabled { get; init; } = 1;
    public bool AutoSave { get; init; } = true;
    public int AutoSaveDelay { get; init; } = 1000;
}
