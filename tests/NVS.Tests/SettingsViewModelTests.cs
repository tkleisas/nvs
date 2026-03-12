using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public sealed class SettingsViewModelTests
{
    private static ISettingsService CreateSettingsService(AppSettings? settings = null)
    {
        var svc = Substitute.For<ISettingsService>();
        svc.AppSettings.Returns(settings ?? new AppSettings());
        return svc;
    }

    private static ILanguageServerManager CreateServerManager(
        IReadOnlyList<LanguageServerDefinition>? servers = null)
    {
        var mgr = Substitute.For<ILanguageServerManager>();
        mgr.GetAvailableServers().Returns(servers ?? [
            new LanguageServerDefinition
            {
                Id = "test-ls",
                Name = "Test LS",
                Description = "A test server",
                License = "MIT",
                Languages = [Language.CSharp],
                BinaryName = "test-ls",
                InstallMethod = InstallMethod.Npm,
                InstallCommand = "npm",
                InstallPackage = "test-ls",
            }
        ]);
        mgr.CheckServerStatusAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(LanguageServerStatus.NotInstalled);
        return mgr;
    }

    [Fact]
    public void Constructor_ShouldInitializeSections()
    {
        var vm = new SettingsViewModel(CreateSettingsService(), CreateServerManager());

        vm.Sections.Should().HaveCount(5);
        vm.Sections.Should().Contain("General");
        vm.Sections.Should().Contain("Editor");
        vm.Sections.Should().Contain("Terminal");
        vm.Sections.Should().Contain("Language Servers");
        vm.Sections.Should().Contain("LLM");
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadSettingsValues()
    {
        var settings = new AppSettings
        {
            RestorePreviousSession = false,
            CheckUpdatesOnStartup = true,
            Editor = new EditorSettings { FontSize = 16, TabSize = 2, WordWrap = true },
            Terminal = new TerminalSettings { FontFamily = "Consolas", FontSize = 18, BufferSize = 10000 },
            Llm = new LlmSettings { Model = "gpt-4", MaxTokens = 4096 },
        };

        var vm = new SettingsViewModel(CreateSettingsService(settings), CreateServerManager());
        await vm.InitializeAsync();

        vm.RestorePreviousSession.Should().BeFalse();
        vm.CheckUpdatesOnStartup.Should().BeTrue();
        vm.FontSize.Should().Be(16);
        vm.TabSize.Should().Be(2);
        vm.WordWrap.Should().BeTrue();
        vm.TerminalFontFamily.Should().Be("Consolas");
        vm.TerminalFontSize.Should().Be(18);
        vm.TerminalBufferSize.Should().Be(10000);
        vm.LlmModel.Should().Be("gpt-4");
        vm.LlmMaxTokens.Should().Be(4096);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadLanguageServers()
    {
        var vm = new SettingsViewModel(CreateSettingsService(), CreateServerManager());
        await vm.InitializeAsync();

        vm.LanguageServers.Should().HaveCount(1);
        vm.LanguageServers[0].Definition.Id.Should().Be("test-ls");
        vm.LanguageServers[0].Status.Should().Be(LanguageServerStatus.NotInstalled);
    }

    [Fact]
    public async Task InitializeAsync_ShouldApplyUserConfig()
    {
        var settings = new AppSettings
        {
            LanguageServers = new Dictionary<string, LanguageServerUserConfig>
            {
                ["test-ls"] = new() { Enabled = false, CustomCommand = "/usr/bin/test-ls" },
            }
        };

        var vm = new SettingsViewModel(CreateSettingsService(settings), CreateServerManager());
        await vm.InitializeAsync();

        vm.LanguageServers[0].IsEnabled.Should().BeFalse();
        vm.LanguageServers[0].CustomCommand.Should().Be("/usr/bin/test-ls");
    }

    [Fact]
    public void SelectedSectionIndex_ShouldControlVisibility()
    {
        var vm = new SettingsViewModel(CreateSettingsService(), CreateServerManager());

        vm.SelectedSectionIndex = 0;
        vm.IsGeneralVisible.Should().BeTrue();
        vm.IsEditorVisible.Should().BeFalse();

        vm.SelectedSectionIndex = 1;
        vm.IsEditorVisible.Should().BeTrue();
        vm.IsGeneralVisible.Should().BeFalse();

        vm.SelectedSectionIndex = 2;
        vm.IsTerminalVisible.Should().BeTrue();

        vm.SelectedSectionIndex = 3;
        vm.IsLanguageServersVisible.Should().BeTrue();

        vm.SelectedSectionIndex = 4;
        vm.IsLlmVisible.Should().BeTrue();
    }

    [Fact]
    public async Task SaveCommand_ShouldPersistSettings()
    {
        var settingsService = CreateSettingsService();
        var vm = new SettingsViewModel(settingsService, CreateServerManager());
        await vm.InitializeAsync();

        vm.FontSize = 18;
        vm.TabSize = 8;
        vm.RestorePreviousSession = false;
        vm.LlmModel = "llama";

        await vm.SaveCommand.ExecuteAsync(null);

        await settingsService.Received(1).SaveAppSettingsAsync(
            Arg.Is<AppSettings>(s =>
                s.Editor.FontSize == 18 &&
                s.Editor.TabSize == 8 &&
                s.RestorePreviousSession == false &&
                s.Llm.Model == "llama"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_ShouldIncludeLanguageServerConfigs()
    {
        var settingsService = CreateSettingsService();
        var vm = new SettingsViewModel(settingsService, CreateServerManager());
        await vm.InitializeAsync();

        vm.LanguageServers[0].IsEnabled = false;
        vm.LanguageServers[0].CustomCommand = "/custom/path";

        await vm.SaveCommand.ExecuteAsync(null);

        await settingsService.Received(1).SaveAppSettingsAsync(
            Arg.Is<AppSettings>(s =>
                s.LanguageServers.ContainsKey("test-ls") &&
                s.LanguageServers["test-ls"].Enabled == false &&
                s.LanguageServers["test-ls"].CustomCommand == "/custom/path"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveCommand_ShouldSetIsSaved()
    {
        var vm = new SettingsViewModel(CreateSettingsService(), CreateServerManager());
        await vm.InitializeAsync();

        vm.IsSaved.Should().BeFalse();
        await vm.SaveCommand.ExecuteAsync(null);
        vm.IsSaved.Should().BeTrue();
    }

    [Fact]
    public async Task InstallServerCommand_ShouldCallManager()
    {
        var mgr = CreateServerManager();
        mgr.InstallServerAsync("test-ls", Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var vm = new SettingsViewModel(CreateSettingsService(), mgr);
        await vm.InitializeAsync();

        var item = vm.LanguageServers[0];
        await vm.InstallServerCommand.ExecuteAsync(item);

        await mgr.Received(1).InstallServerAsync("test-ls",
            Arg.Any<IProgress<string>>(),
            Arg.Any<CancellationToken>());
        item.Status.Should().Be(LanguageServerStatus.Installed);
    }

    [Fact]
    public async Task InstallServerCommand_WhenFails_ShouldKeepNotInstalledStatus()
    {
        var mgr = CreateServerManager();
        mgr.InstallServerAsync("test-ls", Arg.Any<IProgress<string>>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var vm = new SettingsViewModel(CreateSettingsService(), mgr);
        await vm.InitializeAsync();

        var item = vm.LanguageServers[0];
        await vm.InstallServerCommand.ExecuteAsync(item);

        item.Status.Should().Be(LanguageServerStatus.NotInstalled);
    }

    [Fact]
    public async Task InitializeAsync_ShouldLoadTerminalDefaults()
    {
        var vm = new SettingsViewModel(CreateSettingsService(), CreateServerManager());
        await vm.InitializeAsync();

        vm.TerminalFontFamily.Should().Contain("Cascadia");
        vm.TerminalFontSize.Should().Be(14);
        vm.TerminalBufferSize.Should().Be(5000);
    }

    [Fact]
    public async Task SaveCommand_ShouldPersistTerminalSettings()
    {
        var settingsService = CreateSettingsService();
        var vm = new SettingsViewModel(settingsService, CreateServerManager());
        await vm.InitializeAsync();

        vm.TerminalFontFamily = "Fira Code";
        vm.TerminalFontSize = 16;
        vm.TerminalBufferSize = 8000;

        await vm.SaveCommand.ExecuteAsync(null);

        await settingsService.Received(1).SaveAppSettingsAsync(
            Arg.Is<AppSettings>(s =>
                s.Terminal.FontFamily == "Fira Code" &&
                s.Terminal.FontSize == 16 &&
                s.Terminal.BufferSize == 8000),
            Arg.Any<CancellationToken>());
    }
}

public sealed class LanguageServerItemViewModelTests
{
    private static LanguageServerDefinition CreateDefinition() => new()
    {
        Id = "test-ls",
        Name = "Test Server",
        Description = "A test server",
        License = "MIT",
        Languages = [Language.CSharp, Language.Cpp],
        BinaryName = "test-ls",
        InstallMethod = InstallMethod.Npm,
        InstallCommand = "npm",
        InstallPackage = "test-ls",
        HomepageUrl = "https://example.com",
    };

    [Fact]
    public void Constructor_ShouldSetLanguageDisplay()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition());

        item.LanguageDisplay.Should().Contain("CSharp");
        item.LanguageDisplay.Should().Contain("Cpp");
    }

    [Fact]
    public void Constructor_ShouldSetInstallMethodDisplay()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition());

        item.InstallMethodDisplay.Should().Be("npm");
    }

    [Theory]
    [InlineData(LanguageServerStatus.Installed, "✓")]
    [InlineData(LanguageServerStatus.NotInstalled, "✗")]
    [InlineData(LanguageServerStatus.Unknown, "?")]
    public void StatusIcon_ShouldReflectStatus(LanguageServerStatus status, string expectedIcon)
    {
        var item = new LanguageServerItemViewModel(CreateDefinition()) { Status = status };

        item.StatusIcon.Should().Be(expectedIcon);
    }

    [Fact]
    public void CanInstall_ShouldBeTrueWhenNotInstalledAndNotInstalling()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition())
        {
            Status = LanguageServerStatus.NotInstalled,
            IsInstalling = false,
        };

        item.CanInstall.Should().BeTrue();
    }

    [Fact]
    public void CanInstall_ShouldBeFalseWhenInstalled()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition())
        {
            Status = LanguageServerStatus.Installed,
        };

        item.CanInstall.Should().BeFalse();
    }

    [Fact]
    public void CanInstall_ShouldBeFalseWhileInstalling()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition())
        {
            Status = LanguageServerStatus.NotInstalled,
            IsInstalling = true,
        };

        item.CanInstall.Should().BeFalse();
    }

    [Fact]
    public void PropertyChanged_ShouldFireForStatus()
    {
        var item = new LanguageServerItemViewModel(CreateDefinition());
        var changedProperties = new List<string>();
        item.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        item.Status = LanguageServerStatus.Installed;

        changedProperties.Should().Contain(nameof(LanguageServerItemViewModel.Status));
        changedProperties.Should().Contain(nameof(LanguageServerItemViewModel.StatusIcon));
        changedProperties.Should().Contain(nameof(LanguageServerItemViewModel.CanInstall));
    }
}
