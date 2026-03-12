using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;

namespace NVS.Tests;

public class MainViewModelGitTerminalTests
{
    private static MainViewModel CreateViewModel(
        IGitService? gitService = null,
        ITerminalService? terminalService = null)
    {
        var workspaceService = Substitute.For<IWorkspaceService>();
        var editorService = Substitute.For<IEditorService>();
        var fileSystemService = Substitute.For<IFileSystemService>();
        var editor = new EditorViewModel(editorService, fileSystemService);
        var git = gitService ?? Substitute.For<IGitService>();
        var terminal = terminalService ?? Substitute.For<ITerminalService>();
        var settings = Substitute.For<ISettingsService>();
        settings.AppSettings.Returns(new AppSettings());

        return new MainViewModel(workspaceService, editorService, fileSystemService, editor, git, terminal, settings,
            Substitute.For<ISolutionService>(), Substitute.For<IBuildService>());
    }

    // --- Sidebar switching ---

    [Fact]
    public void IsSidebarShowingGit_DefaultsFalse()
    {
        var vm = CreateViewModel();
        vm.IsSidebarShowingGit.Should().BeFalse();
        vm.IsSidebarShowingExplorer.Should().BeTrue();
    }

    [Fact]
    public void ShowSourceControlCommand_SwitchesToGitPanel()
    {
        var vm = CreateViewModel();

        vm.ShowSourceControlCommand.Execute(null);

        vm.IsSidebarShowingGit.Should().BeTrue();
        vm.IsSidebarShowingExplorer.Should().BeFalse();
    }

    [Fact]
    public void ShowExplorerCommand_SwitchesToExplorer()
    {
        var vm = CreateViewModel();
        vm.ShowSourceControlCommand.Execute(null);

        vm.ShowExplorerCommand.Execute(null);

        vm.IsSidebarShowingGit.Should().BeFalse();
        vm.IsSidebarShowingExplorer.Should().BeTrue();
    }

    [Fact]
    public void ToggleSidebar_FlipsBetweenExplorerAndGit()
    {
        var vm = CreateViewModel();

        vm.ToggleSidebarCommand.Execute(null);
        vm.IsSidebarShowingGit.Should().BeTrue();

        vm.ToggleSidebarCommand.Execute(null);
        vm.IsSidebarShowingGit.Should().BeFalse();
    }

    // --- Git properties ---

    [Fact]
    public void CurrentBranch_DefaultsToEmpty()
    {
        var vm = CreateViewModel();
        vm.CurrentBranch.Should().BeEmpty();
    }

    [Fact]
    public void CommitMessage_CanBeSet()
    {
        var vm = CreateViewModel();
        vm.CommitMessage = "test message";
        vm.CommitMessage.Should().Be("test message");
    }

    [Fact]
    public async Task GitCommitCommand_WithEmptyMessage_DoesNotCommit()
    {
        var git = Substitute.For<IGitService>();
        var vm = CreateViewModel(gitService: git);
        vm.CommitMessage = "";

        await vm.GitCommitCommand.ExecuteAsync(null);

        await git.DidNotReceive().CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitCommitCommand_WithMessage_CommitsAndClears()
    {
        var git = Substitute.For<IGitService>();
        git.CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(new CommitResult { Success = true, CommitHash = "abc1234567890" });
        git.CurrentBranch.Returns("main");
        git.Status.Returns(new RepositoryStatus());

        var vm = CreateViewModel(gitService: git);
        vm.CommitMessage = "feat: add feature";

        await vm.GitCommitCommand.ExecuteAsync(null);

        await git.Received(1).CommitAsync("feat: add feature", Arg.Any<CancellationToken>());
        vm.CommitMessage.Should().BeEmpty();
        vm.StatusMessage.Should().Contain("abc1234");
    }

    [Fact]
    public async Task GitCommitCommand_OnFailure_ShowsError()
    {
        var git = Substitute.For<IGitService>();
        git.CommitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
           .Returns(new CommitResult { Success = false, ErrorMessage = "nothing to commit" });
        git.Status.Returns(new RepositoryStatus());

        var vm = CreateViewModel(gitService: git);
        vm.CommitMessage = "attempt";

        await vm.GitCommitCommand.ExecuteAsync(null);

        vm.StatusMessage.Should().Contain("nothing to commit");
        vm.CommitMessage.Should().Be("attempt"); // not cleared on failure
    }

    [Fact]
    public async Task GitStageAllCommand_CallsStageAllAsync()
    {
        var git = Substitute.For<IGitService>();
        git.Status.Returns(new RepositoryStatus());
        var vm = CreateViewModel(gitService: git);

        await vm.GitStageAllCommand.ExecuteAsync(null);

        await git.Received(1).StageAllAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitStageFileCommand_WithFile_CallsStageAsync()
    {
        var git = Substitute.For<IGitService>();
        git.Status.Returns(new RepositoryStatus());
        var vm = CreateViewModel(gitService: git);
        var file = new GitFileStatus { Path = "test.cs", Status = FileStatus.Modified };

        await vm.GitStageFileCommand.ExecuteAsync(file);

        await git.Received(1).StageAsync("test.cs", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GitUnstageFileCommand_WithFile_CallsUnstageAsync()
    {
        var git = Substitute.For<IGitService>();
        git.Status.Returns(new RepositoryStatus());
        var vm = CreateViewModel(gitService: git);
        var file = new GitFileStatus { Path = "test.cs", Status = FileStatus.Modified, IsStaged = true };

        await vm.GitUnstageFileCommand.ExecuteAsync(file);

        await git.Received(1).UnstageAsync("test.cs", Arg.Any<CancellationToken>());
    }

    // --- Terminal ---

    [Fact]
    public void IsTerminalVisible_DefaultsFalse()
    {
        var vm = CreateViewModel();
        vm.IsTerminalVisible.Should().BeFalse();
    }

    [Fact]
    public void ToggleTerminal_SetsVisible()
    {
        var terminal = Substitute.For<ITerminalService>();
        terminal.ActiveTerminal.Returns((ITerminalInstance?)null);
        var vm = CreateViewModel(terminalService: terminal);

        vm.ToggleTerminalCommand.Execute(null);

        vm.IsTerminalVisible.Should().BeTrue();
    }

    [Fact]
    public void ToggleTerminal_Twice_HidesTerminal()
    {
        var terminal = Substitute.For<ITerminalService>();
        terminal.ActiveTerminal.Returns((ITerminalInstance?)null);
        var vm = CreateViewModel(terminalService: terminal);

        vm.ToggleTerminalCommand.Execute(null);
        vm.ToggleTerminalCommand.Execute(null);

        vm.IsTerminalVisible.Should().BeFalse();
    }

    [Fact]
    public void TerminalInput_CanBeSet()
    {
        var vm = CreateViewModel();
        vm.TerminalInput = "ls -la";
        vm.TerminalInput.Should().Be("ls -la");
    }

    [Fact]
    public void SendTerminalInput_WithEmptyInput_DoesNothing()
    {
        var terminal = Substitute.For<ITerminalService>();
        var vm = CreateViewModel(terminalService: terminal);
        vm.TerminalInput = "";

        vm.SendTerminalInputCommand.Execute(null);

        terminal.ActiveTerminal.DidNotReceive();
    }

    [Fact]
    public void SendTerminalInput_WithText_WritesToTerminalAndClears()
    {
        var terminal = Substitute.For<ITerminalService>();
        var instance = Substitute.For<ITerminalInstance>();
        terminal.ActiveTerminal.Returns(instance);
        var vm = CreateViewModel(terminalService: terminal);

        vm.TerminalInput = "echo hello";
        vm.SendTerminalInputCommand.Execute(null);

        instance.Received(1).WriteLine("echo hello");
        vm.TerminalInput.Should().BeEmpty();
    }

    [Fact]
    public void SendTerminalInput_NoActiveTerminal_CreatesTerminal()
    {
        var terminal = Substitute.For<ITerminalService>();
        var instance = Substitute.For<ITerminalInstance>();
        terminal.ActiveTerminal.Returns((ITerminalInstance?)null, instance);
        terminal.CreateTerminal(Arg.Any<TerminalOptions>()).Returns(instance);
        var vm = CreateViewModel(terminalService: terminal);

        vm.TerminalInput = "dir";
        vm.SendTerminalInputCommand.Execute(null);

        terminal.Received(1).CreateTerminal(Arg.Any<TerminalOptions>());
        instance.Received(1).WriteLine("dir");
    }

    // --- Property change notifications ---

    [Fact]
    public void IsSidebarShowingGit_RaisesPropertyChanged_ForBothProperties()
    {
        var vm = CreateViewModel();
        var changedProperties = new List<string>();
        vm.PropertyChanged += (_, e) => changedProperties.Add(e.PropertyName!);

        vm.IsSidebarShowingGit = true;

        changedProperties.Should().Contain("IsSidebarShowingGit");
        changedProperties.Should().Contain("IsSidebarShowingExplorer");
    }

    [Fact]
    public void IsTerminalVisible_RaisesPropertyChanged()
    {
        var vm = CreateViewModel();
        var changed = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == "IsTerminalVisible") changed = true;
        };

        vm.IsTerminalVisible = true;

        changed.Should().BeTrue();
    }

    // --- RefreshFileTree ---

    [Fact]
    public async Task RefreshFileTreeCommand_WithNoWorkspace_DoesNotThrow()
    {
        var vm = CreateViewModel();
        var act = () => vm.RefreshFileTreeCommand.ExecuteAsync(null);
        await act.Should().NotThrowAsync();
    }
}
