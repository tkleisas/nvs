using NVS.Core.Enums;
using NVS.Core.Interfaces;
using NVS.Core.Models;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class MainViewModelPrerequisiteTests
{
    private static MainViewModel CreateViewModel(
        IPrerequisiteService? prerequisiteService = null,
        ILanguageService? languageService = null)
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fs = Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fs);
        var git = Substitute.For<IGitService>();
        var terminal = Substitute.For<ITerminalService>();
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());

        return new MainViewModel(
            workspaceService, editorService, fs, editor, git, terminal, settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>(),
            prerequisiteService: prerequisiteService,
            languageService: languageService);
    }

    [Fact]
    public void InfoBarItems_DefaultsToEmpty()
    {
        var vm = CreateViewModel();
        vm.InfoBarItems.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithNoServices_ShouldNotThrow()
    {
        var vm = CreateViewModel();

        await vm.CheckPrerequisitesAsync(@"C:\temp\test");

        vm.InfoBarItems.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithMissingPrerequisites_ShouldAddInfoBars()
    {
        var prereqService = Substitute.For<IPrerequisiteService>();
        var langService = Substitute.For<ILanguageService>();

        prereqService.CheckPrerequisitesAsync(Arg.Any<IEnumerable<Language>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PrerequisiteInfo>>(new[]
            {
                new PrerequisiteInfo
                {
                    Language = Language.Java,
                    BinaryName = "java",
                    DisplayName = "Java JDK",
                    InstallHint = "Install from https://adoptium.net"
                },
                new PrerequisiteInfo
                {
                    Language = Language.Php,
                    BinaryName = "php",
                    DisplayName = "PHP",
                    InstallHint = "Install from https://www.php.net"
                }
            }));

        var vm = CreateViewModel(prereqService, langService);

        // Create a temp directory with some files
        var tempDir = Path.Combine(Path.GetTempPath(), "nvs-prereq-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "App.java"), "class App {}");
            await File.WriteAllTextAsync(Path.Combine(tempDir, "index.php"), "<?php");

            langService.DetectLanguage("file.java").Returns(Language.Java);
            langService.DetectLanguage("file.php").Returns(Language.Php);

            await vm.CheckPrerequisitesAsync(tempDir);

            vm.InfoBarItems.Should().HaveCount(2);
            vm.InfoBarItems[0].Severity.Should().Be(InfoBarSeverity.Warning);
            vm.InfoBarItems[0].Message.Should().Contain("Java JDK");
            vm.InfoBarItems[1].Message.Should().Contain("PHP");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_WithNoMissing_ShouldNotAddInfoBars()
    {
        var prereqService = Substitute.For<IPrerequisiteService>();
        var langService = Substitute.For<ILanguageService>();

        prereqService.CheckPrerequisitesAsync(Arg.Any<IEnumerable<Language>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PrerequisiteInfo>>([]));

        var vm = CreateViewModel(prereqService, langService);

        var tempDir = Path.Combine(Path.GetTempPath(), "nvs-prereq-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "Program.cs"), "");
            langService.DetectLanguage("file.cs").Returns(Language.CSharp);

            await vm.CheckPrerequisitesAsync(tempDir);

            vm.InfoBarItems.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public async Task CheckPrerequisitesAsync_InfoBarDismiss_ShouldRemoveFromCollection()
    {
        var prereqService = Substitute.For<IPrerequisiteService>();
        var langService = Substitute.For<ILanguageService>();

        prereqService.CheckPrerequisitesAsync(Arg.Any<IEnumerable<Language>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<PrerequisiteInfo>>(new[]
            {
                new PrerequisiteInfo
                {
                    Language = Language.Java,
                    BinaryName = "java",
                    DisplayName = "Java JDK",
                    InstallHint = "Install from https://adoptium.net"
                }
            }));

        var vm = CreateViewModel(prereqService, langService);

        var tempDir = Path.Combine(Path.GetTempPath(), "nvs-prereq-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(tempDir, "App.java"), "");
            langService.DetectLanguage("file.java").Returns(Language.Java);

            await vm.CheckPrerequisitesAsync(tempDir);
            vm.InfoBarItems.Should().HaveCount(1);

            // Dismiss the info bar
            vm.InfoBarItems[0].DismissCommand.Execute(null);

            vm.InfoBarItems.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectWorkspaceLanguages_WithEmptyDir_ShouldReturnEmpty()
    {
        var langService = Substitute.For<ILanguageService>();
        var vm = CreateViewModel(languageService: langService);

        var tempDir = Path.Combine(Path.GetTempPath(), "nvs-lang-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var result = vm.DetectWorkspaceLanguages(tempDir);
            result.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectWorkspaceLanguages_WithFiles_ShouldDetectLanguages()
    {
        var langService = Substitute.For<ILanguageService>();
        langService.DetectLanguage("file.cs").Returns(Language.CSharp);
        langService.DetectLanguage("file.java").Returns(Language.Java);
        langService.DetectLanguage("file.txt").Returns(Language.Unknown);

        var vm = CreateViewModel(languageService: langService);

        var tempDir = Path.Combine(Path.GetTempPath(), "nvs-lang-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Program.cs"), "");
            File.WriteAllText(Path.Combine(tempDir, "App.java"), "");
            File.WriteAllText(Path.Combine(tempDir, "readme.txt"), "");

            var result = vm.DetectWorkspaceLanguages(tempDir);

            result.Should().Contain(Language.CSharp);
            result.Should().Contain(Language.Java);
            result.Should().NotContain(Language.Unknown);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DetectWorkspaceLanguages_WithNoLanguageService_ShouldReturnEmpty()
    {
        var vm = CreateViewModel();

        var result = vm.DetectWorkspaceLanguages(@"C:\temp\nonexistent");

        result.Should().BeEmpty();
    }
}
