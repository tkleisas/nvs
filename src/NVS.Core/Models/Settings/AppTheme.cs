namespace NVS.Core.Models.Settings;

public sealed record AppTheme
{
    public required string Name { get; init; }
    public required string ThemeVariant { get; init; } // "Dark" or "Light"
    public required ThemeColors Colors { get; init; }
}
