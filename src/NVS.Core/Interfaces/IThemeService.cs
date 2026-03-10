namespace NVS.Core.Interfaces;

public interface IThemeService
{
    string CurrentTheme { get; }
    IReadOnlyList<string> AvailableThemes { get; }
    
    Task LoadThemesAsync(string themesDirectory, CancellationToken cancellationToken = default);
    Task SetThemeAsync(string themeName, CancellationToken cancellationToken = default);
    Task ImportThemeAsync(string themePath, CancellationToken cancellationToken = default);
    
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
}

public sealed class ThemeChangedEventArgs : EventArgs
{
    public required string OldTheme { get; init; }
    public required string NewTheme { get; init; }
}
