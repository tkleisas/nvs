using FluentAssertions;
using NVS.Core.Interfaces;
using NVS.Services.Terminal;

namespace NVS.Services.Tests;

public sealed class TerminalServiceTests : IDisposable
{
    private readonly TerminalService _service = new();

    private static TerminalOptions NonInteractiveOptions(string name = "Test")
    {
        // Use a command that exits immediately rather than an interactive shell
        var shell = OperatingSystem.IsWindows() ? "cmd.exe" : "/bin/sh";
        return new TerminalOptions { Name = name, Shell = shell };
    }

    [Fact]
    public void CreateTerminal_ReturnsInstance_AndSetsActive()
    {
        var terminal = _service.CreateTerminal(NonInteractiveOptions());

        terminal.Should().NotBeNull();
        terminal.Name.Should().Be("Test");
        _service.ActiveTerminal.Should().Be(terminal);
        _service.Terminals.Should().HaveCount(1);
    }

    [Fact]
    public void CreateTerminal_MultipleTerminals_SetsLastAsActive()
    {
        _service.CreateTerminal(NonInteractiveOptions("T1"));
        var t2 = _service.CreateTerminal(NonInteractiveOptions("T2"));

        _service.Terminals.Should().HaveCount(2);
        _service.ActiveTerminal.Should().Be(t2);
    }

    [Fact]
    public void CloseTerminal_RemovesFromList()
    {
        var terminal = _service.CreateTerminal(NonInteractiveOptions());

        _service.CloseTerminal(terminal);

        _service.Terminals.Should().BeEmpty();
        _service.ActiveTerminal.Should().BeNull();
    }

    [Fact]
    public void CloseTerminal_WithMultiple_SetsActiveToPrevious()
    {
        var t1 = _service.CreateTerminal(NonInteractiveOptions("T1"));
        var t2 = _service.CreateTerminal(NonInteractiveOptions("T2"));

        _service.CloseTerminal(t2);

        _service.ActiveTerminal.Should().Be(t1);
    }

    [Fact]
    public void CloseAllTerminals_ClearsEverything()
    {
        _service.CreateTerminal(NonInteractiveOptions("T1"));
        _service.CreateTerminal(NonInteractiveOptions("T2"));

        _service.CloseAllTerminals();

        _service.Terminals.Should().BeEmpty();
        _service.ActiveTerminal.Should().BeNull();
    }

    [Fact]
    public void CreateTerminal_FiresTerminalCreatedEvent()
    {
        ITerminalInstance? created = null;
        _service.TerminalCreated += (_, t) => created = t;

        var terminal = _service.CreateTerminal(NonInteractiveOptions());

        created.Should().Be(terminal);
    }

    [Fact]
    public void CloseTerminal_FiresTerminalClosedEvent()
    {
        var terminal = _service.CreateTerminal(NonInteractiveOptions());

        ITerminalInstance? closed = null;
        _service.TerminalClosed += (_, t) => closed = t;

        _service.CloseTerminal(terminal);

        closed.Should().Be(terminal);
    }

    [Fact]
    public void CreateTerminal_DefaultOptions_StillWorks()
    {
        var terminal = _service.CreateTerminal(NonInteractiveOptions());

        terminal.Should().NotBeNull();
        terminal.IsConnected.Should().BeTrue();
    }

    [Fact]
    public void TerminalInstance_GetDefaultShell_ReturnsNonNull()
    {
        var shell = TerminalInstance.GetDefaultShell();

        shell.Should().NotBeNullOrEmpty();
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}
