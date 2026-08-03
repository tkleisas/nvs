using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class CommandPaletteViewModelTests
{
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

    [Fact]
    public void Ctor_CollectsMainAndSubViewModelCommands()
    {
        var palette = new CommandPaletteViewModel(CreateMain());

        palette.Items.Should().BeEmpty(); // not opened yet
        palette.Open();
        palette.Items.Select(i => i.Title).Should().Contain(t => t.Contains("Show Database Explorer"));
        palette.Items.Select(i => i.Title).Should().Contain(t => t.StartsWith("Git:"));
        palette.Items.Select(i => i.Title).Should().Contain(t => t.StartsWith("BuildRun:"));
    }

    [Fact]
    public void Query_FiltersByAllTerms()
    {
        var palette = new CommandPaletteViewModel(CreateMain());
        palette.Open();

        palette.Query = "show database";

        palette.Items.Should().NotBeEmpty();
        palette.Items.Should().OnlyContain(i =>
            i.Title.Contains("show", StringComparison.OrdinalIgnoreCase)
            && i.Title.Contains("database", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ExecuteSelected_InvokesCommandAndCloses()
    {
        var main = CreateMain();
        var palette = new CommandPaletteViewModel(main);
        palette.Open();
        palette.Query = "show search";
        palette.SelectedItem = palette.Items.FirstOrDefault(i => i.CommandName == "ShowSearchCommand");

        palette.ExecuteSelected();

        main.SidebarMode.Should().Be("Search");
        palette.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void MoveSelection_ClampsToBounds()
    {
        var palette = new CommandPaletteViewModel(CreateMain());
        palette.Open();

        palette.MoveSelection(-1);
        palette.SelectedItem.Should().Be(palette.Items.First());
        palette.MoveSelection(palette.Items.Count + 10);
        palette.SelectedItem.Should().Be(palette.Items.Last());
    }

    [Theory]
    [InlineData("ShowDatabaseExplorer", "Show Database Explorer")]
    [InlineData("NewFile", "New File")]
    [InlineData("OpenLlmSettings", "Open Llm Settings")]
    public void Humanize_SplitsCamelCase(string input, string expected)
    {
        CommandPaletteViewModel.Humanize(input).Should().Be(expected);
    }
}
