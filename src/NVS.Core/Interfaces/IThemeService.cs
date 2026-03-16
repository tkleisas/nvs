using NVS.Core.Models.Settings;

namespace NVS.Core.Interfaces;

public interface IThemeService
{
    AppTheme CurrentTheme { get; }
    IReadOnlyList<AppTheme> AvailableThemes { get; }

    Task ApplyThemeAsync(string themeName, CancellationToken cancellationToken = default);

    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;
}

public sealed class ThemeChangedEventArgs : EventArgs
{
    public required string OldTheme { get; init; }
    public required string NewTheme { get; init; }
}
