using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class NuGetToolViewModelTests
{
    private static NuGetToolViewModel CreateVm()
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
        return new NuGetToolViewModel(main);
    }

    [Fact]
    public void Constructor_ShouldSetDefaults()
    {
        var vm = CreateVm();

        vm.Id.Should().Be("NuGet");
        vm.Title.Should().Be("📦 NuGet");
        vm.SelectedTab.Should().Be("Browse");
        vm.IsBrowseTab.Should().BeTrue();
        vm.IsInstalledTab.Should().BeFalse();
        vm.IsUpdatesTab.Should().BeFalse();
        vm.IsLoading.Should().BeFalse();
        vm.StatusText.Should().Be("Ready");
        vm.SearchQuery.Should().BeEmpty();
        vm.IncludePrerelease.Should().BeFalse();
    }

    [Fact]
    public void SwitchTab_ShouldUpdateTabProperties()
    {
        var vm = CreateVm();

        vm.SwitchTabCommand.Execute("Installed");

        vm.SelectedTab.Should().Be("Installed");
        vm.IsBrowseTab.Should().BeFalse();
        vm.IsInstalledTab.Should().BeTrue();
        vm.IsUpdatesTab.Should().BeFalse();
    }

    [Fact]
    public void SwitchTab_ToUpdates_ShouldUpdateAllFlags()
    {
        var vm = CreateVm();

        vm.SwitchTabCommand.Execute("Updates");

        vm.IsBrowseTab.Should().BeFalse();
        vm.IsInstalledTab.Should().BeFalse();
        vm.IsUpdatesTab.Should().BeTrue();
    }

    [Fact]
    public void SwitchTab_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.SwitchTabCommand.Execute("Updates");

        changed.Should().Contain("SelectedTab");
        changed.Should().Contain("IsBrowseTab");
        changed.Should().Contain("IsInstalledTab");
        changed.Should().Contain("IsUpdatesTab");
    }

    [Fact]
    public void SearchQuery_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SearchQuery") raised = true; };

        vm.SearchQuery = "Newtonsoft";

        raised.Should().BeTrue();
        vm.SearchQuery.Should().Be("Newtonsoft");
    }

    [Fact]
    public void IncludePrerelease_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "IncludePrerelease") raised = true; };

        vm.IncludePrerelease = true;

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectedProjectPath_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SelectedProjectPath") raised = true; };

        vm.SelectedProjectPath = @"C:\test\project.csproj";

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectedSearchResult_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SelectedSearchResult") raised = true; };

        vm.SelectedSearchResult = new NVS.Core.Models.NuGet.NuGetPackageInfo
        {
            Id = "Test",
            Version = "1.0.0",
            Description = "Test package"
        };

        raised.Should().BeTrue();
    }

    [Fact]
    public void SelectedInstalledPackage_ShouldRaisePropertyChanged()
    {
        var vm = CreateVm();
        var raised = false;
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == "SelectedInstalledPackage") raised = true; };

        vm.SelectedInstalledPackage = new NVS.Core.Models.NuGet.InstalledPackage
        {
            Id = "Test",
            RequestedVersion = "1.0.0",
            ResolvedVersion = "1.0.0"
        };

        raised.Should().BeTrue();
    }

    [Fact]
    public void RefreshProjects_WithNoSolution_ShouldLeaveEmpty()
    {
        var vm = CreateVm();

        vm.RefreshProjects();

        vm.ProjectPaths.Should().BeEmpty();
    }

    [Fact]
    public void Collections_ShouldBeEmpty_Initially()
    {
        var vm = CreateVm();

        vm.SearchResults.Should().BeEmpty();
        vm.InstalledPackages.Should().BeEmpty();
        vm.OutdatedPackages.Should().BeEmpty();
        vm.ProjectPaths.Should().BeEmpty();
    }

    [Fact]
    public void CanClose_ShouldBeTrue()
    {
        var vm = CreateVm();
        vm.CanClose.Should().BeTrue();
    }

    [Fact]
    public void CanPin_ShouldBeTrue()
    {
        var vm = CreateVm();
        vm.CanPin.Should().BeTrue();
    }
}
