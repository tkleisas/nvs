using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class BuildOutputToolViewModelTests
{
    [Fact]
    public void AppendOutput_BatchesLinesUntilFlush()
    {
        var vm = new BuildOutputToolViewModel(CreateMain());

        vm.AppendOutput("line 1", false);
        vm.AppendOutput("line 2", true);

        vm.OutputLines.Should().BeEmpty("lines are batched before the coalesced UI flush");

        vm.FlushPending();

        vm.OutputLines.Select(l => l.Text).Should().Equal("line 1", "line 2");
        vm.OutputLines[1].IsError.Should().BeTrue();
    }

    [Fact]
    public void ClearOutput_DropsPendingLines()
    {
        var vm = new BuildOutputToolViewModel(CreateMain());

        vm.AppendOutput("stale", false);
        vm.ClearOutput();
        vm.FlushPending();

        vm.OutputLines.Should().BeEmpty();
    }

    private static MainViewModel CreateMain()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());
        return new MainViewModel(
            Substitute.For<IWorkspaceService>(),
            Substitute.For<IEditorService>(),
            Substitute.For<IFileSystemService>(),
            new EditorViewModel(Substitute.For<IEditorService>(), Substitute.For<IFileSystemService>()),
            Substitute.For<IGitService>(),
            Substitute.For<ITerminalService>(),
            settings,
            Substitute.For<ISolutionService>(),
            Substitute.For<IBuildService>());
    }
}
