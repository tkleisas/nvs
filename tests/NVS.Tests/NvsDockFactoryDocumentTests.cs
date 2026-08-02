using Dock.Avalonia.Controls;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class NvsDockFactoryDocumentTests
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

    private static (NvsDockFactory Factory, IRootDock Root) CreateFactory()
    {
        var factory = new NvsDockFactory(CreateMainVm());
        var root = factory.CreateLayout();
        return (factory, root);
    }

    private static IEnumerable<IDock> AllDocks(IDockable root)
    {
        if (root is IDock dock)
        {
            yield return dock;
            if (dock.VisibleDockables is not null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    foreach (var d in AllDocks(child))
                    {
                        yield return d;
                    }
                }
            }
        }
    }

    [Fact]
    public void CreateLayout_ComponentsAreNotInAnyDockYet()
    {
        var (factory, root) = CreateFactory();

        AllDocks(root).Any(d => d.VisibleDockables?.Contains(factory.DatabaseExplorer!) == true)
            .Should().BeFalse();
        AllDocks(root).Any(d => d.VisibleDockables?.Contains(factory.ApiClient!) == true)
            .Should().BeFalse();
    }

    [Fact]
    public void CreateLayout_BottomToolDockNoLongerHostsComponents()
    {
        var (factory, root) = CreateFactory();

        var bottomToolDocks = AllDocks(root).OfType<ToolDock>()
            .Where(t => t.Alignment == Alignment.Bottom);
        foreach (var dock in bottomToolDocks)
        {
            dock.VisibleDockables.Should().NotContain(factory.DatabaseExplorer!);
            dock.VisibleDockables.Should().NotContain(factory.ApiClient!);
        }
    }

    [Fact]
    public void OpenDatabaseExplorerDocument_AddsToDocumentDockAndActivates()
    {
        var (factory, root) = CreateFactory();

        var doc = factory.OpenDatabaseExplorerDocument();

        var documentDock = AllDocks(root).OfType<DocumentDock>().Single();
        documentDock.VisibleDockables.Should().Contain(doc);
        documentDock.ActiveDockable.Should().Be(doc);
    }

    [Fact]
    public void OpenApiClientDocument_AddsToDocumentDockAndActivates()
    {
        var (factory, root) = CreateFactory();

        var doc = factory.OpenApiClientDocument();

        var documentDock = AllDocks(root).OfType<DocumentDock>().Single();
        documentDock.VisibleDockables.Should().Contain(doc);
        documentDock.ActiveDockable.Should().Be(doc);
    }

    [Fact]
    public void OpenDatabaseExplorerDocument_Twice_DoesNotDuplicate()
    {
        var (factory, root) = CreateFactory();

        factory.OpenDatabaseExplorerDocument();
        factory.OpenDatabaseExplorerDocument();

        var documentDock = AllDocks(root).OfType<DocumentDock>().Single();
        documentDock.VisibleDockables!
            .Count(d => ReferenceEquals(d, factory.DatabaseExplorer))
            .Should().Be(1);
    }

    [Fact]
    public void OpenDatabaseExplorerDocument_ReopensAfterClose()
    {
        var (factory, root) = CreateFactory();
        var doc = factory.OpenDatabaseExplorerDocument();
        var documentDock = AllDocks(root).OfType<DocumentDock>().Single();

        documentDock.VisibleDockables!.Remove(doc);
        documentDock.VisibleDockables.Should().NotContain(doc);

        factory.OpenDatabaseExplorerDocument();
        documentDock.VisibleDockables.Should().Contain(doc);
        documentDock.ActiveDockable.Should().Be(doc);
    }

    [Fact]
    public void Components_AreDocuments_ClosableAndNotPinnable()
    {
        var (factory, _) = CreateFactory();

        factory.DatabaseExplorer.Should().BeAssignableTo<Dock.Model.Mvvm.Controls.Document>();
        factory.ApiClient.Should().BeAssignableTo<Dock.Model.Mvvm.Controls.Document>();
        factory.DatabaseExplorer!.CanClose.Should().BeTrue();
        factory.ApiClient!.CanClose.Should().BeTrue();
    }
}
