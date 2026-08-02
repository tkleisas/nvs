using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class DatabaseExplorerToolViewModelTests
{
    private static MainViewModel CreateMainVm()
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fs = Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fs);
        var git = Substitute.For<IGitService>();
        var terminal = Substitute.For<ITerminalService>();
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        return new MainViewModel(workspaceService, editorService, fs, editor, git, terminal, settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>());
    }

    [Fact]
    public void Constructor_HidesEmbeddedMenu_ForHostMenuIntegration()
    {
        var vm = new DatabaseExplorerToolViewModel(CreateMainVm());

        vm.DatabaseViewModel.IsMenuVisible.Should().BeFalse();
    }

    [Fact]
    public void Constructor_NoWorkspace_ReportsStayInAppData()
    {
        var vm = new DatabaseExplorerToolViewModel(CreateMainVm());

        vm.DatabaseViewModel.ReportStoreDirectory.Should().BeNull();
    }

    [Fact]
    public void WorkspaceOpened_ReportsMoveToWorkspaceNvsFolder()
    {
        var main = CreateMainVm();
        var vm = new DatabaseExplorerToolViewModel(main);

        main.WorkspacePath = Path.Combine("C:", "ws");
        main.IsWorkspaceOpen = true;

        vm.DatabaseViewModel.ReportStoreDirectory.Should().Be(Path.Combine(main.WorkspacePath, ".nvs"));
    }

    [Fact]
    public void Constructor_IsADocument_ClosableNotPinnable()
    {
        var vm = new DatabaseExplorerToolViewModel(CreateMainVm());

        vm.CanClose.Should().BeTrue();
        vm.CanPin.Should().BeFalse();
    }
}
