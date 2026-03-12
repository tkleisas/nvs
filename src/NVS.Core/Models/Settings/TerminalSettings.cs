namespace NVS.Core.Models.Settings;

public sealed record TerminalSettings
{
    public string FontFamily { get; init; } = "Cascadia Mono,Cascadia Code,Consolas,Courier New,monospace";
    public int FontSize { get; init; } = 14;
    public int BufferSize { get; init; } = 5000;
}
