using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
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

        // Must run on UI thread to update Avalonia resources
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(theme));
            return;
        }

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
        SetBrush(app, "SuccessBrush", c.SuccessColor);
        SetBrush(app, "ErrorBrush", c.ErrorColor);

        // Fluent system keys used by the embedded components (SQLiteExplorer,
        // ApiClient) and stock Avalonia dialogs — map them onto the same palette
        // so hosted panels don't clash with the NVS theme.        SetBrush(app, "SystemControlBackgroundChromeMediumBrush", c.MenuBackground);
        SetBrush(app, "SystemControlBackgroundChromeMediumLowBrush", c.MenuBackground);
        SetBrush(app, "SystemControlBackgroundChromeLowBrush", c.ToolPanelBackground);
        SetBrush(app, "SystemControlBackgroundBaseLowBrush", c.InputBackground);
        SetBrush(app, "SystemControlBackgroundBaseMediumLowBrush", c.InputBackground);
        SetBrush(app, "SystemControlForegroundBaseHighBrush", c.SidebarForeground);
        SetBrush(app, "SystemControlForegroundBaseMediumBrush", c.SidebarForeground);
        SetBrush(app, "SystemControlForegroundBaseMediumLowBrush", c.BorderColor);
        SetBrush(app, "SystemControlForegroundBaseLowBrush", c.TabInactiveForeground);
        SetColor(app, "SystemAccentColor", c.AccentColor);
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

    private static void SetColor(Application app, string key, string hexColor)
    {
        if (Color.TryParse(hexColor, out var color))
        {
            app.Resources[key] = color;
        }
    }
}
