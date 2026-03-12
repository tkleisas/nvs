namespace NVS.Core.Models.Settings;

public sealed record WindowSettings
{
    public double Width { get; init; } = 1200;
    public double Height { get; init; } = 800;
    public double? X { get; init; }
    public double? Y { get; init; }
    public bool IsMaximized { get; init; } = true;
}
