namespace NVS.Core.Models.Settings;

public sealed record DockLayoutSettings
{
    public double LeftPanelProportion { get; init; } = 0.22;
    public double BottomPanelProportion { get; init; } = 0.25;
}
