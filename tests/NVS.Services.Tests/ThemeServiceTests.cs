using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.Services.Themes;

namespace NVS.Services.Tests;

public sealed class ThemeServiceTests
{
    private readonly ISettingsService _settingsService;
    private readonly ThemeService _service;

    public ThemeServiceTests()
    {
        _settingsService = Substitute.For<ISettingsService>();
        _settingsService.AppSettings.Returns(new AppSettings { Theme = "NVS Dark" });
        _service = new ThemeService(_settingsService);
    }

    #region BuiltInThemes

    [Fact]
    public void BuiltInThemes_ShouldHaveFourThemes()
    {
        BuiltInThemes.All.Should().HaveCount(4);
    }

    [Theory]
    [InlineData("NVS Dark", "Dark")]
    [InlineData("NVS Light", "Light")]
    [InlineData("Monokai", "Dark")]
    [InlineData("Solarized Dark", "Dark")]
    public void BuiltInThemes_ShouldHaveExpectedNamesAndVariants(string name, string variant)
    {
        var theme = BuiltInThemes.All.FirstOrDefault(t => t.Name == name);

        theme.Should().NotBeNull();
        theme!.ThemeVariant.Should().Be(variant);
    }

    [Fact]
    public void BuiltInThemes_AllColorsShouldBeValidHex()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            var colors = theme.Colors;
            AssertValidHex(colors.EditorBackground, $"{theme.Name}.EditorBackground");
            AssertValidHex(colors.EditorForeground, $"{theme.Name}.EditorForeground");
            AssertValidHex(colors.EditorLineHighlight, $"{theme.Name}.EditorLineHighlight");
            AssertValidHex(colors.EditorSelectionBackground, $"{theme.Name}.EditorSelectionBackground");
            AssertValidHex(colors.SidebarBackground, $"{theme.Name}.SidebarBackground");
            AssertValidHex(colors.SidebarForeground, $"{theme.Name}.SidebarForeground");
            AssertValidHex(colors.StatusBarBackground, $"{theme.Name}.StatusBarBackground");
            AssertValidHex(colors.StatusBarForeground, $"{theme.Name}.StatusBarForeground");
            AssertValidHex(colors.ToolPanelBackground, $"{theme.Name}.ToolPanelBackground");
            AssertValidHex(colors.ToolPanelForeground, $"{theme.Name}.ToolPanelForeground");
            AssertValidHex(colors.AccentColor, $"{theme.Name}.AccentColor");
            AssertValidHex(colors.BorderColor, $"{theme.Name}.BorderColor");
            AssertValidHex(colors.ButtonBackground, $"{theme.Name}.ButtonBackground");
            AssertValidHex(colors.ButtonForeground, $"{theme.Name}.ButtonForeground");
            AssertValidHex(colors.InputBackground, $"{theme.Name}.InputBackground");
            AssertValidHex(colors.InputForeground, $"{theme.Name}.InputForeground");
            AssertValidHex(colors.InputBorder, $"{theme.Name}.InputBorder");
            AssertValidHex(colors.TabActiveBackground, $"{theme.Name}.TabActiveBackground");
            AssertValidHex(colors.TabInactiveBackground, $"{theme.Name}.TabInactiveBackground");
            AssertValidHex(colors.TabActiveForeground, $"{theme.Name}.TabActiveForeground");
            AssertValidHex(colors.TabInactiveForeground, $"{theme.Name}.TabInactiveForeground");
            AssertValidHex(colors.MenuBackground, $"{theme.Name}.MenuBackground");
            AssertValidHex(colors.MenuForeground, $"{theme.Name}.MenuForeground");
            AssertValidHex(colors.InfoBarInfoBackground, $"{theme.Name}.InfoBarInfoBackground");
            AssertValidHex(colors.InfoBarWarningBackground, $"{theme.Name}.InfoBarWarningBackground");
            AssertValidHex(colors.InfoBarErrorBackground, $"{theme.Name}.InfoBarErrorBackground");
            AssertValidHex(colors.InfoBarForeground, $"{theme.Name}.InfoBarForeground");
            AssertValidHex(colors.ScrollBarBackground, $"{theme.Name}.ScrollBarBackground");
            AssertValidHex(colors.ScrollBarThumb, $"{theme.Name}.ScrollBarThumb");
            AssertValidHex(colors.LineNumberForeground, $"{theme.Name}.LineNumberForeground");
        }
    }

    [Fact]
    public void BuiltInThemes_AllShouldHaveDistinctForegroundAndBackground()
    {
        foreach (var theme in BuiltInThemes.All)
        {
            theme.Colors.EditorBackground.Should().NotBe(theme.Colors.EditorForeground,
                because: $"{theme.Name} editor fg/bg should differ");
            theme.Colors.SidebarBackground.Should().NotBe(theme.Colors.SidebarForeground,
                because: $"{theme.Name} sidebar fg/bg should differ");
            theme.Colors.StatusBarBackground.Should().NotBe(theme.Colors.StatusBarForeground,
                because: $"{theme.Name} status bar fg/bg should differ");
        }
    }

    #endregion

    #region ThemeService

    [Fact]
    public void CurrentTheme_ShouldDefaultToNvsDark()
    {
        _service.CurrentTheme.Name.Should().Be("NVS Dark");
    }

    [Fact]
    public void CurrentTheme_WithUnknownSetting_ShouldFallbackToNvsDark()
    {
        _settingsService.AppSettings.Returns(new AppSettings { Theme = "Nonexistent Theme" });
        var service = new ThemeService(_settingsService);

        service.CurrentTheme.Name.Should().Be("NVS Dark");
    }

    [Fact]
    public void AvailableThemes_ShouldReturnAllBuiltInThemes()
    {
        _service.AvailableThemes.Should().HaveCount(4);
        _service.AvailableThemes.Select(t => t.Name).Should().Contain("NVS Dark");
        _service.AvailableThemes.Select(t => t.Name).Should().Contain("NVS Light");
        _service.AvailableThemes.Select(t => t.Name).Should().Contain("Monokai");
        _service.AvailableThemes.Select(t => t.Name).Should().Contain("Solarized Dark");
    }

    [Fact]
    public async Task ApplyThemeAsync_WithValidTheme_ShouldUpdateCurrentTheme()
    {
        await _service.ApplyThemeAsync("Monokai");

        _service.CurrentTheme.Name.Should().Be("Monokai");
    }

    [Fact]
    public async Task ApplyThemeAsync_WithValidTheme_ShouldSaveSettings()
    {
        await _service.ApplyThemeAsync("NVS Light");

        await _settingsService.Received(1).SaveAppSettingsAsync(
            Arg.Is<AppSettings>(s => s.Theme == "NVS Light"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyThemeAsync_WithValidTheme_ShouldFireThemeChangedEvent()
    {
        ThemeChangedEventArgs? eventArgs = null;
        _service.ThemeChanged += (_, args) => eventArgs = args;

        await _service.ApplyThemeAsync("Solarized Dark");

        eventArgs.Should().NotBeNull();
        eventArgs!.OldTheme.Should().Be("NVS Dark");
        eventArgs.NewTheme.Should().Be("Solarized Dark");
    }

    [Fact]
    public async Task ApplyThemeAsync_WithInvalidTheme_ShouldNotChange()
    {
        await _service.ApplyThemeAsync("This Theme Does Not Exist");

        _service.CurrentTheme.Name.Should().Be("NVS Dark");
        await _settingsService.DidNotReceive().SaveAppSettingsAsync(
            Arg.Any<AppSettings>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyThemeAsync_WithInvalidTheme_ShouldNotFireEvent()
    {
        var eventFired = false;
        _service.ThemeChanged += (_, _) => eventFired = true;

        await _service.ApplyThemeAsync("Nope");

        eventFired.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyThemeAsync_SwitchingMultipleTimes_ShouldTrackCorrectly()
    {
        await _service.ApplyThemeAsync("Monokai");
        _service.CurrentTheme.Name.Should().Be("Monokai");

        await _service.ApplyThemeAsync("NVS Light");
        _service.CurrentTheme.Name.Should().Be("NVS Light");

        await _service.ApplyThemeAsync("NVS Dark");
        _service.CurrentTheme.Name.Should().Be("NVS Dark");
    }

    [Fact]
    public async Task ApplyThemeAsync_SecondSwitch_ShouldReportCorrectOldTheme()
    {
        await _service.ApplyThemeAsync("Monokai");

        ThemeChangedEventArgs? eventArgs = null;
        _service.ThemeChanged += (_, args) => eventArgs = args;

        await _service.ApplyThemeAsync("NVS Light");

        eventArgs.Should().NotBeNull();
        eventArgs!.OldTheme.Should().Be("Monokai");
        eventArgs.NewTheme.Should().Be("NVS Light");
    }

    [Fact]
    public void Constructor_WithLightThemeSetting_ShouldLoadLight()
    {
        _settingsService.AppSettings.Returns(new AppSettings { Theme = "NVS Light" });
        var service = new ThemeService(_settingsService);

        service.CurrentTheme.Name.Should().Be("NVS Light");
        service.CurrentTheme.ThemeVariant.Should().Be("Light");
    }

    #endregion

    #region Helpers

    private static void AssertValidHex(string color, string propertyName)
    {
        color.Should().StartWith("#", because: $"{propertyName} should be a hex color");
        color.Should().HaveLength(7, because: $"{propertyName} should be #RRGGBB format");
        color[1..].Should().MatchRegex("^[0-9A-Fa-f]{6}$",
            because: $"{propertyName} should contain valid hex digits");
    }

    #endregion
}
