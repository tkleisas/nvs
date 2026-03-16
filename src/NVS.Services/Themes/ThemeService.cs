using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Services.Themes;

public sealed class ThemeService : IThemeService
{
    private readonly ISettingsService _settingsService;
    private AppTheme _currentTheme;

    public ThemeService(ISettingsService settingsService)
    {
        _settingsService = settingsService;

        var themeName = _settingsService.AppSettings.Theme;
        _currentTheme = BuiltInThemes.All.FirstOrDefault(t => t.Name == themeName)
            ?? BuiltInThemes.NvsDark;
    }

    public AppTheme CurrentTheme => _currentTheme;

    public IReadOnlyList<AppTheme> AvailableThemes => BuiltInThemes.All;

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public async Task ApplyThemeAsync(string themeName, CancellationToken cancellationToken = default)
    {
        var theme = BuiltInThemes.All.FirstOrDefault(t => t.Name == themeName);
        if (theme is null)
            return;

        var oldTheme = _currentTheme.Name;
        _currentTheme = theme;

        var settings = _settingsService.AppSettings with { Theme = themeName };
        await _settingsService.SaveAppSettingsAsync(settings, cancellationToken).ConfigureAwait(false);

        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs
        {
            OldTheme = oldTheme,
            NewTheme = themeName,
        });
    }
}
