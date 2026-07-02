using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class MainViewModelSearchTests
{
    private static MainViewModel CreateViewModel(
        IFileSystemService? fileSystemService = null)
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fs = fileSystemService ?? Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fs);
        var git = Substitute.For<IGitService>();
        var terminal = Substitute.For<ITerminalService>();
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());

        return new MainViewModel(workspaceService, editorService, fs, editor, git, terminal, settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>());
    }

    [Fact]
    public void SidebarMode_DefaultsToExplorer()
    {
        var vm = CreateViewModel();
        vm.SidebarMode.Should().Be("Explorer");
        vm.IsSidebarShowingExplorer.Should().BeTrue();
        vm.IsSidebarShowingGit.Should().BeFalse();
        vm.IsSidebarShowingSearch.Should().BeFalse();
    }

    [Fact]
    public void ShowSearch_SetsSidebarModeToSearch()
    {
        var vm = CreateViewModel();
        vm.ShowSearchCommand.Execute(null);

        vm.SidebarMode.Should().Be("Search");
        vm.IsSidebarShowingSearch.Should().BeTrue();
        vm.IsSidebarShowingExplorer.Should().BeFalse();
        vm.IsSidebarShowingGit.Should().BeFalse();
    }

    [Fact]
    public void ShowExplorer_RestoresSidebarFromSearch()
    {
        var vm = CreateViewModel();
        vm.ShowSearchCommand.Execute(null);
        vm.ShowExplorerCommand.Execute(null);

        vm.SidebarMode.Should().Be("Explorer");
        vm.IsSidebarShowingExplorer.Should().BeTrue();
    }

    [Fact]
    public void ShowSourceControl_RestoresSidebarFromSearch()
    {
        var vm = CreateViewModel();
        vm.ShowSearchCommand.Execute(null);
        vm.ShowSourceControlCommand.Execute(null);

        vm.SidebarMode.Should().Be("Git");
        vm.IsSidebarShowingGit.Should().BeTrue();
    }

    [Fact]
    public void SidebarMode_RaisesPropertyChangedForAllModes()
    {
        var vm = CreateViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.SidebarMode = "Search";

        changed.Should().Contain("SidebarMode");
        changed.Should().Contain("IsSidebarShowingExplorer");
        changed.Should().Contain("IsSidebarShowingGit");
        changed.Should().Contain("IsSidebarShowingSearch");
    }

    [Fact]
    public void SearchQuery_CanBeSetAndGet()
    {
        var vm = CreateViewModel();
        vm.Search.Query = "hello";
        vm.Search.Query.Should().Be("hello");
    }

    [Fact]
    public async Task SearchFiles_WithNoWorkspace_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.Search.Query = "test";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFiles_WithEmptyQuery_DoesNothing()
    {
        var vm = CreateViewModel();
        vm.Search.Query = "";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task SearchFiles_FindsMatchingLines()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "/workspace/file1.cs", "/workspace/file2.txt" }));
        fs.ReadAllTextAsync("/workspace/file1.cs", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("line one\nfoo bar hello\nline three"));
        fs.ReadAllTextAsync("/workspace/file2.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("nothing here\nHELLO world\n"));

        var vm = CreateViewModel(fs);
        // Need a workspace open
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;
        vm.Search.Query = "hello";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().HaveCount(2);
        vm.Search.Results[0].RelativePath.Should().Contain("file1.cs");
        vm.Search.Results[0].LineNumber.Should().Be(2);
        vm.Search.Results[0].LineText.Should().Be("foo bar hello");
        vm.Search.Results[1].LineNumber.Should().Be(2);
    }

    [Fact]
    public async Task SearchFiles_SkipsBinaryFiles()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "/workspace/image.png", "/workspace/app.exe", "/workspace/readme.txt" }));
        fs.ReadAllTextAsync("/workspace/readme.txt", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("hello world"));

        var vm = CreateViewModel(fs);
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;
        vm.Search.Query = "hello";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().HaveCount(1);
        vm.Search.Results[0].RelativePath.Should().Contain("readme.txt");
    }

    [Fact]
    public async Task SearchFiles_IsCaseInsensitive()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "/workspace/test.cs" }));
        fs.ReadAllTextAsync("/workspace/test.cs", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("HELLO World\nhello world\nHeLlO world"));

        var vm = CreateViewModel(fs);
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;
        vm.Search.Query = "hello";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().HaveCount(3);
    }

    [Fact]
    public async Task SearchFiles_ClearsOldResults()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(
                new List<string> { "/workspace/test.cs" }));
        fs.ReadAllTextAsync("/workspace/test.cs", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("first search\nsecond search"));

        var vm = CreateViewModel(fs);
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;

        vm.Search.Query = "first";
        await vm.Search.SearchFilesCommand.ExecuteAsync(null);
        vm.Search.Results.Should().HaveCount(1);

        vm.Search.Query = "second";
        await vm.Search.SearchFilesCommand.ExecuteAsync(null);
        vm.Search.Results.Should().HaveCount(1);
        vm.Search.Results[0].LineText.Should().Be("second search");
    }

    [Fact]
    public async Task SearchFiles_SetsStatusMessage()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(new List<string>()));

        var vm = CreateViewModel(fs);
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;
        vm.Search.Query = "test";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("0 result(s)");
    }

    [Fact]
    public void SearchResult_Display_ShowsRelativePathAndLine()
    {
        var result = new FileSearchResult
        {
            FilePath = "/workspace/src/file.cs",
            RelativePath = "src/file.cs",
            LineNumber = 42,
            LineText = "var x = 1;",
        };

        result.Display.Should().Be("src/file.cs:42");
    }

    [Fact]
    public void IsBinaryExtension_DetectsCommonBinaryFormats()
    {
        SearchViewModel.IsBinaryExtension("test.exe").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.dll").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.png").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.so").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.o").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.a").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.dylib").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.class").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.pyc").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.wasm").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.sqlite").Should().BeTrue();
        SearchViewModel.IsBinaryExtension("test.pdf").Should().BeTrue();

        SearchViewModel.IsBinaryExtension("test.cs").Should().BeFalse();
        SearchViewModel.IsBinaryExtension("test.txt").Should().BeFalse();
        SearchViewModel.IsBinaryExtension("test.json").Should().BeFalse();
        SearchViewModel.IsBinaryExtension("test.xml").Should().BeFalse();
        SearchViewModel.IsBinaryExtension("test.md").Should().BeFalse();
    }

    [Theory]
    [InlineData("/workspace/.git/objects/abc123", true)]
    [InlineData("/workspace/bin/Debug/net10.0/app.dll", true)]
    [InlineData("/workspace/obj/Debug/net10.0/ref.dll", true)]
    [InlineData("/workspace/node_modules/lodash/index.js", true)]
    [InlineData("/workspace/__pycache__/mod.pyc", true)]
    [InlineData("/workspace/.vs/settings.json", true)]
    [InlineData("/workspace/src/Services/MyService.cs", false)]
    [InlineData("/workspace/tests/UnitTests.cs", false)]
    [InlineData("/workspace/README.md", false)]
    public void IsInExcludedDirectory_CorrectlyFilters(string path, bool expected)
    {
        SearchViewModel.IsInExcludedDirectory(path).Should().Be(expected);
    }

    [Fact]
    public async Task SearchFiles_SkipsExcludedDirectories()
    {
        var fs = Substitute.For<IFileSystemService>();
        fs.GetFilesAsync(Arg.Any<string>(), "*", true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<string>>(
                new List<string>
                {
                    "/workspace/bin/Debug/app.cs",
                    "/workspace/obj/ref.cs",
                    "/workspace/.git/config",
                    "/workspace/node_modules/pkg/index.js",
                    "/workspace/src/Program.cs",
                }));
        fs.ReadAllTextAsync("/workspace/src/Program.cs", Arg.Any<CancellationToken>())
            .Returns(Task.FromResult("hello world"));

        var vm = CreateViewModel(fs);
        vm.WorkspacePath = "/workspace";
        vm.IsWorkspaceOpen = true;
        vm.Search.Query = "hello";

        await vm.Search.SearchFilesCommand.ExecuteAsync(null);

        vm.Search.Results.Should().HaveCount(1);
        vm.Search.Results[0].RelativePath.Should().Contain("Program.cs");
    }
}
