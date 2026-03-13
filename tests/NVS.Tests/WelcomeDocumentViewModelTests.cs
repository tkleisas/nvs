using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class WelcomeDocumentViewModelTests
{
    private static WelcomeDocumentViewModel CreateVm()
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fs = Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fs);
        var git = Substitute.For<IGitService>();
        var terminal = Substitute.For<ITerminalService>();
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        var main = new MainViewModel(workspaceService, editorService, fs, editor, git, terminal, settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>());
        return new WelcomeDocumentViewModel(main);
    }

    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var vm = CreateVm();

        vm.Id.Should().Be("Welcome");
        vm.Title.Should().Be("Welcome");
        vm.CanClose.Should().BeTrue();
    }

    [Fact]
    public void Main_ShouldBeSet()
    {
        var vm = CreateVm();
        vm.Main.Should().NotBeNull();
    }
}
