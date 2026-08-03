using NVS.Core.Interfaces;
using NVS.Core.Models.Settings;
using NVS.ViewModels;
using NVS.ViewModels.Dock;

namespace NVS.Tests;

public class TerminalToolViewModelTests
{
    [Fact]
    public async Task CommandSentBeforePtyReady_IsFlushedWhenTerminalAssigned()
    {
        // Regression (debug launch): DebugViewModel sends the debuggee launch command
        // the moment the terminal tool is created, before its view has started the PTY.
        // The command must not be lost — it has to go out once the session exists.
        var vm = new TerminalToolViewModel(CreateMain());

        await vm.SendCommandToTerminalAsync("dotnet exec app.dll");

        var sent = new List<string>();
        var terminal = Substitute.For<IProcessTerminal>();
        terminal.IsRunning.Returns(true);
        _ = terminal.SendInputAsync(Arg.Do<string>(s => sent.Add(s)));

        vm.Terminal = terminal;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (sent.Count == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        sent.Should().Equal("dotnet exec app.dll\r");
    }

    [Fact]
    public async Task CommandsSentWhileNoChannel_StayQueuedAndFlushInOrder()
    {
        var vm = new TerminalToolViewModel(CreateMain());

        await vm.SendCommandToTerminalAsync("first");
        await vm.SendCommandToTerminalAsync("second");

        var sent = new List<string>();
        var terminal = Substitute.For<IProcessTerminal>();
        terminal.IsRunning.Returns(true);
        _ = terminal.SendInputAsync(Arg.Do<string>(s => sent.Add(s)));

        vm.Terminal = terminal;

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (sent.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(25);
        }

        sent.Should().Equal("first\r", "second\r");
    }

    [Fact]
    public async Task CommandSentWithRunningTerminal_GoesStraightThrough()
    {
        var vm = new TerminalToolViewModel(CreateMain());
        var sent = new List<string>();
        var terminal = Substitute.For<IProcessTerminal>();
        terminal.IsRunning.Returns(true);
        _ = terminal.SendInputAsync(Arg.Do<string>(s => sent.Add(s)));
        vm.Terminal = terminal;

        await vm.SendCommandToTerminalAsync("echo hi");

        sent.Should().Equal("echo hi\r");
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
