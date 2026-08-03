using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class SearchReplaceTests : IDisposable
{
    private readonly string _dir;

    public SearchReplaceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "NvsSearchTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
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

    private (SearchViewModel Vm, string File) Setup(string content)
    {
        var file = Path.Combine(_dir, "sample.txt");
        File.WriteAllText(file, content);

        var fs = Substitute.For<IFileSystemService>();
        fs.ReadAllTextAsync(file, Arg.Any<CancellationToken>()).Returns(_ => Task.FromResult(File.ReadAllText(file)));

        var main = CreateMain();
        main.WorkspacePath = _dir;
        var vm = new SearchViewModel(fs, main);
        return (vm, file);
    }

    [Fact]
    public async Task ReplaceAll_Literal_ReplacesOccurrences()
    {
        var (vm, file) = Setup("foo\nbar foo\nbaz\n");
        vm.Query = "foo";
        vm.ReplaceText = "qux";
        vm.Results.Add(new FileSearchResult { FilePath = file, LineNumber = 1 });
        vm.Results.Add(new FileSearchResult { FilePath = file, LineNumber = 2 });
        vm.ConfirmReplaceAll = (_, _) => Task.FromResult(true);

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        File.ReadAllText(file).Should().Be("qux\nbar qux\nbaz\n");
    }

    [Fact]
    public async Task ReplaceAll_ConfirmDeclined_LeavesFileUntouched()
    {
        var (vm, file) = Setup("foo\n");
        vm.Query = "foo";
        vm.ReplaceText = "qux";
        vm.Results.Add(new FileSearchResult { FilePath = file, LineNumber = 1 });
        vm.ConfirmReplaceAll = (_, _) => Task.FromResult(false);

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        File.ReadAllText(file).Should().Be("foo\n");
    }

    [Fact]
    public async Task ReplaceAll_Regex_SupportsGroups()
    {
        var (vm, file) = Setup("name=alice;\nname=bob;\n");
        vm.Query = "name=(\\w+);";
        vm.UseRegex = true;
        vm.ReplaceText = "user:$1";
        vm.Results.Add(new FileSearchResult { FilePath = file, LineNumber = 1 });
        vm.ConfirmReplaceAll = (_, _) => Task.FromResult(true);

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        File.ReadAllText(file).Should().Be("user:alice\nuser:bob\n");
    }

    [Fact]
    public async Task ReplaceAll_WholeWord_DoesNotTouchSubstrings()
    {
        var (vm, file) = Setup("cat category cat\n");
        vm.Query = "cat";
        vm.WholeWord = true;
        vm.ReplaceText = "dog";
        vm.Results.Add(new FileSearchResult { FilePath = file, LineNumber = 1 });
        vm.ConfirmReplaceAll = (_, _) => Task.FromResult(true);

        await vm.ReplaceAllCommand.ExecuteAsync(null);

        File.ReadAllText(file).Should().Be("dog category dog\n");
    }
}
