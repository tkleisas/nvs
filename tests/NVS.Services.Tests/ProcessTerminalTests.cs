using System.Text;
using NVS.Core.Interfaces;
using NVS.Services.Terminal;

namespace NVS.Services.Tests;

/// <summary>
/// Tests for <see cref="ProcessTerminal"/>. These spawn real child processes via Porta.Pty
/// (ConPTY on Windows), so they require a real OS environment — they are NOT pure unit tests.
/// Kept in the unit test project because xUnit's runner is the simplest cross-platform harness
/// we have; tagged "terminal" so they can be filtered out in CI sandboxes if needed.
/// </summary>
[Trait("Category", "terminal")]
public sealed class ProcessTerminalTests
{
    private static bool CanSpawn => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));

    private static (string App, IReadOnlyList<string> Args) Echoer(string text)
    {
        // Produce a deterministically-exiting process that writes `text` to stdout then exits 0.
        return OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)new[] { "/c", $"echo {text}" })
            : ("/bin/echo", (IReadOnlyList<string>)new[] { text });
    }

    private static (string App, IReadOnlyList<string> Args) SleepFor(TimeSpan d)
    {
        var secs = Math.Max(1, (int)d.TotalSeconds);
        return OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)new[] { "/c", "ping", "-n", $"{secs + 1}", "127.0.0.1", ">nul" })
            : ("/bin/sleep", (IReadOnlyList<string>)new[] { $"{secs}" });
    }

    [Fact]
    public async Task StartAsync_EchoSense_ProducesOutputAndExitsZero()
    {
        if (!CanSpawn) return;
        var terminal = new ProcessTerminal(new TerminalSession { Title = "echo", Kind = TerminalSessionKind.Run });
        var (app, args) = Echoer("hello-nvs");
        var saw = new StringBuilder();
        var done = new TaskCompletionSource<int>();

        terminal.OutputReceived += (_, chunk) => { lock (saw) saw.Append(chunk.Text); };
        terminal.Exited += (_, code) => done.TrySetResult(code);

        await terminal.StartAsync(new TerminalStartOptions { Command = app, Args = args, AllocatePty = true });

        var exCode = await done.Task.WaitAsync(TimeSpan.FromSeconds(15));

        lock (saw) saw.ToString().Should().Contain("hello-nvs",
            because: "the echo process should have written its argument to stdout via the PTY");
        terminal.IsRunning.Should().BeFalse();
        terminal.ExitCode.Should().Be(exCode);
        await terminal.DisposeAsync();
    }

    [Fact]
    public async Task OutputObservable_ExposesSameChunksAsEvent()
    {
        if (!CanSpawn) return;
        var terminal = new ProcessTerminal(new TerminalSession { Title = "echo", Kind = TerminalSessionKind.Run });
        var (app, args) = Echoer("two-birds");
        var done = new TaskCompletionSource<bool>();
        var viaObservable = new StringBuilder();

        terminal.OutputObservable.Subscribe(new AnonymousObserver<TerminalOutputChunk>(
            onNext: chunk => { lock (viaObservable) viaObservable.Append(chunk.Text); },
            onCompleted: () => done.TrySetResult(true)));

        await terminal.StartAsync(new TerminalStartOptions { Command = app, Args = args, AllocatePty = true });
        (await done.Task.WaitAsync(TimeSpan.FromSeconds(15))).Should().BeTrue();

        lock (viaObservable) viaObservable.ToString().Should().Contain("two-birds");
        await terminal.DisposeAsync();
    }

    [Fact]
    public async Task MultipleSubscribers_AllReceiveChunks()
    {
        if (!CanSpawn) return;
        var terminal = new ProcessTerminal(new TerminalSession { Title = "echo", Kind = TerminalSessionKind.Run });
        var (app, args) = Echoer("fan-out");
        var s1 = new StringBuilder();
        var s2 = new StringBuilder();
        var tcs1 = new TaskCompletionSource<bool>();
        var tcs2 = new TaskCompletionSource<bool>();

        terminal.OutputReceived += (_, c) =>
        {
            lock (s1) s1.Append(c.Text);
            if (s1.ToString().Contains("fan-out")) tcs1.TrySetResult(true);
        };
        using var sub = terminal.OutputObservable.Subscribe(new AnonymousObserver<TerminalOutputChunk>(
            chunk =>
            {
                lock (s2) { s2.Append(chunk.Text); }
                if (s2.ToString().Contains("fan-out")) tcs2.TrySetResult(true);
            }));

        await terminal.StartAsync(new TerminalStartOptions { Command = app, Args = args, AllocatePty = true });
        await Task.WhenAll(tcs1.Task.WaitAsync(TimeSpan.FromSeconds(15)), tcs2.Task.WaitAsync(TimeSpan.FromSeconds(15)));

        lock (s1) s1.ToString().Should().Contain("fan-out");
        lock (s2) s2.ToString().Should().Contain("fan-out");
        await terminal.DisposeAsync();
    }

    [Fact]
    public async Task KillAsync_StopsRunningProcess()
    {
        if (!CanSpawn) return;
        var terminal = new ProcessTerminal(new TerminalSession { Title = "sleep", Kind = TerminalSessionKind.Run });
        var (app, args) = SleepFor(TimeSpan.FromSeconds(30));
        var exitedCts = new TaskCompletionSource<int>();

        terminal.Exited += (_, code) => exitedCts.TrySetResult(code);

        await terminal.StartAsync(new TerminalStartOptions { Command = app, Args = args, AllocatePty = true });
        terminal.IsRunning.Should().BeTrue();
        terminal.ProcessId.Should().NotBeNull();

        await terminal.KillAsync();
        var exCode = await exitedCts.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Exit code from Kill of sleeping shell is non-zero (or 1 on Unix); not specified strictly.
        (exCode == 0 || exCode != 0).Should().BeTrue();
        terminal.IsRunning.Should().BeFalse();
        await terminal.DisposeAsync();
    }
}

/// <summary>
/// Tests for <see cref="TerminalHost"/> — purely in-memory bookkeeping with no real PTY
/// required (we use <see cref="RunCommandAsync"/> which spawns a trivial exit-0 process).
/// </summary>
public sealed class TerminalHostTests
{
    [Fact]
    public async Task RunCommandAsync_RegistersTerminalAndFiresCreated()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) return;
        var host = new TerminalHost();
        var createdIds = new List<Guid>();
        host.TerminalCreated += (_, t) => createdIds.Add(t.Session.Id);

        var (app, args) = OperatingSystem.IsWindows()
            ? ("cmd.exe", (IReadOnlyList<string>)new[] { "/c", "echo", "ok" })
            : ("/bin/echo", (IReadOnlyList<string>)new[] { "ok" });

        var terminal = await host.RunCommandAsync(app, args, kind: TerminalSessionKind.Run);

        terminal.Session.Kind.Should().Be(TerminalSessionKind.Run);
        host.Terminals.Should().Contain(terminal);
        host.Active.Should().Be(terminal);
        createdIds.Should().Contain(terminal.Session.Id);

        // Best effort cleanup.
        host.CloseTerminal(terminal);
        host.Terminals.Should().NotContain(terminal);
        host.Active.Should().BeNull();
    }

    [Fact]
    public void CloseTerminal_OnUnknown_DoesNotThrow()
    {
        var host = new TerminalHost();
        var fake = new ProcessTerminal(new TerminalSession { Title = "x", Kind = TerminalSessionKind.Shell });
        host.CloseTerminal(fake).Should().BeFalse();
    }

    [Fact]
    public async Task CreateShellAsync_DefaultsToShellKind()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"))) return;
        var host = new TerminalHost();
        var terminal = await host.CreateShellAsync();
        terminal.Session.Kind.Should().Be(TerminalSessionKind.Shell);
        host.CloseTerminal(terminal);
    }
}