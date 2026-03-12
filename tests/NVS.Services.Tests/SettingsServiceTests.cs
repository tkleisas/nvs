using FluentAssertions;
using NVS.Services.Settings;
using Xunit;

namespace NVS.Services.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly SettingsService _service;
    private readonly string _testSettingsPath;

    public SettingsServiceTests()
    {
        _service = new SettingsService();
        
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _testSettingsPath = Path.Combine(appDataPath, "NVS", "settings.json");
    }

    public void Dispose()
    {
        if (File.Exists(_testSettingsPath))
        {
            File.Delete(_testSettingsPath);
        }
    }

    [Fact]
    public void AppSettings_ShouldHaveDefaultValues()
    {
        _service.AppSettings.Theme.Should().Be("NVS Dark");
        _service.AppSettings.KeybindingPreset.Should().Be("vscode");
        _service.AppSettings.Locale.Should().Be("en-US");
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadSettings()
    {
        await _service.InitializeAsync();

        _service.AppSettings.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveAppSettingsAsync_ShouldSaveSettings()
    {
        var settings = new NVS.Core.Models.Settings.AppSettings
        {
            Theme = "Light",
            KeybindingPreset = "vim"
        };

        await _service.SaveAppSettingsAsync(settings);

        _service.AppSettings.Theme.Should().Be("Light");
        _service.AppSettings.KeybindingPreset.Should().Be("vim");
        File.Exists(_testSettingsPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadAppSettingsAsync_ShouldLoadSavedSettings()
    {
        var settings = new NVS.Core.Models.Settings.AppSettings
        {
            Theme = "Custom Theme"
        };
        await _service.SaveAppSettingsAsync(settings);

        var loaded = await _service.LoadAppSettingsAsync();

        loaded.Theme.Should().Be("Custom Theme");
    }

    [Fact]
    public void Get_WithExistingKey_ShouldReturnValue()
    {
        _service.Set("TestKey", "TestValue");

        var value = _service.Get("TestKey", "default");

        value.Should().Be("TestValue");
    }

    [Fact]
    public void Get_WithMissingKey_ShouldReturnDefault()
    {
        var value = _service.Get("NonExistent", "default");

        value.Should().Be("default");
    }

    [Fact]
    public async Task SaveWorkspaceSettingsAsync_ShouldCreateNvsDirectory()
    {
        using var tempDir = new TempDirectory();
        var workspaceSettings = new NVS.Core.Models.Settings.WorkspaceSettings();

        await _service.SaveWorkspaceSettingsAsync(tempDir.Path, workspaceSettings);

        var nvsPath = Path.Combine(tempDir.Path, ".nvs");
        Directory.Exists(nvsPath).Should().BeTrue();
    }

    [Fact]
    public async Task LoadWorkspaceSettingsAsync_ShouldLoadSettings()
    {
        using var tempDir = new TempDirectory();
        var settings = new NVS.Core.Models.Settings.WorkspaceSettings
        {
            Folders = ["src", "tests"]
        };
        await _service.SaveWorkspaceSettingsAsync(tempDir.Path, settings);

        var loaded = await _service.LoadWorkspaceSettingsAsync(tempDir.Path);

        loaded.Folders.Should().Contain("src");
        loaded.Folders.Should().Contain("tests");
    }

    [Fact]
    public void WindowSettings_ShouldHaveDefaults()
    {
        var ws = new NVS.Core.Models.Settings.WindowSettings();

        ws.Width.Should().Be(1200);
        ws.Height.Should().Be(800);
        ws.IsMaximized.Should().BeTrue();
        ws.X.Should().BeNull();
        ws.Y.Should().BeNull();
    }

    [Fact]
    public async Task WindowSettings_ShouldRoundTripThroughSaveLoad()
    {
        var settings = new NVS.Core.Models.Settings.AppSettings
        {
            Window = new NVS.Core.Models.Settings.WindowSettings
            {
                Width = 1400,
                Height = 900,
                X = 100,
                Y = 50,
                IsMaximized = false,
            }
        };

        await _service.SaveAppSettingsAsync(settings);
        var loaded = await _service.LoadAppSettingsAsync();

        loaded.Window.Width.Should().Be(1400);
        loaded.Window.Height.Should().Be(900);
        loaded.Window.X.Should().Be(100);
        loaded.Window.Y.Should().Be(50);
        loaded.Window.IsMaximized.Should().BeFalse();
    }

    [Fact]
    public async Task WindowSettings_ShouldDefaultWhenMissing()
    {
        // Save settings without window (simulates old config file)
        var settings = new NVS.Core.Models.Settings.AppSettings { Theme = "Test" };
        await _service.SaveAppSettingsAsync(settings);
        var loaded = await _service.LoadAppSettingsAsync();

        loaded.Window.Should().NotBeNull();
        loaded.Window.Width.Should().Be(1200);
        loaded.Window.IsMaximized.Should().BeTrue();
    }

    [Fact]
    public async Task DockLayoutSettings_ShouldRoundTrip()
    {
        var settings = new NVS.Core.Models.Settings.AppSettings
        {
            Dock = new NVS.Core.Models.Settings.DockLayoutSettings
            {
                LeftPanelProportion = 0.35,
                BottomPanelProportion = 0.40,
            }
        };

        await _service.SaveAppSettingsAsync(settings);
        var loaded = await _service.LoadAppSettingsAsync();

        loaded.Dock.LeftPanelProportion.Should().Be(0.35);
        loaded.Dock.BottomPanelProportion.Should().Be(0.40);
    }

    private class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
