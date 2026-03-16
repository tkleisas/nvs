using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;

namespace NVS.Helpers;

internal static class ThemeResourceApplier
{
    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app is null)
            return;

        var variant = theme.ThemeVariant == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
        app.RequestedThemeVariant = variant;

        var c = theme.Colors;
        SetBrush(app, "AppBackgroundBrush", c.EditorBackground);
        SetBrush(app, "EditorBackgroundBrush", c.EditorBackground);
        SetBrush(app, "EditorForegroundBrush", c.EditorForeground);
        SetBrush(app, "SidebarBackgroundBrush", c.SidebarBackground);
        SetBrush(app, "SidebarForegroundBrush", c.SidebarForeground);
        SetBrush(app, "StatusBarBackgroundBrush", c.StatusBarBackground);
        SetBrush(app, "StatusBarForegroundBrush", c.StatusBarForeground);
        SetBrush(app, "AccentBrush", c.AccentColor);
        SetBrush(app, "MenuBackgroundBrush", c.MenuBackground);
        SetBrush(app, "MenuForegroundBrush", c.MenuForeground);
        SetBrush(app, "ToolPanelBackgroundBrush", c.ToolPanelBackground);
        SetBrush(app, "ToolPanelForegroundBrush", c.ToolPanelForeground);
        SetBrush(app, "BorderBrush", c.BorderColor);
        SetBrush(app, "InputBackgroundBrush", c.InputBackground);
        SetBrush(app, "InputForegroundBrush", c.InputForeground);
        SetBrush(app, "TextForegroundBrush", c.SidebarForeground);
        SetBrush(app, "TextSecondaryForegroundBrush", c.TabInactiveForeground);
        SetBrush(app, "ButtonBackgroundBrush", c.ButtonBackground);
        SetBrush(app, "ButtonForegroundBrush", c.ButtonForeground);
        SetBrush(app, "InfoBarInfoBackgroundBrush", c.InfoBarInfoBackground);
        SetBrush(app, "InfoBarWarningBackgroundBrush", c.InfoBarWarningBackground);
        SetBrush(app, "InfoBarErrorBackgroundBrush", c.InfoBarErrorBackground);
        SetBrush(app, "InfoBarForegroundBrush", c.InfoBarForeground);
    }

    public static void WireThemeService(IThemeService themeService)
    {
        Apply(themeService.CurrentTheme);
        themeService.ThemeChanged += (_, _) => Apply(themeService.CurrentTheme);
    }

    private static void SetBrush(Application app, string key, string hexColor)
    {
        if (Color.TryParse(hexColor, out var color))
        {
            app.Resources[key] = new SolidColorBrush(color);
        }
    }
}
