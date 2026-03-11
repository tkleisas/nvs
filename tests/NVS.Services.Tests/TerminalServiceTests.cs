using FluentAssertions;
using NVS.Core.Interfaces;
using NVS.Services.Terminal;

namespace NVS.Services.Tests;

public sealed class TerminalServiceTests : IDisposable
{
    private readonly TerminalService _service = new();

    [Fact]
    public void CreateTerminal_ReturnsInstance_AndSetsActive()
    {
        var terminal = _service.CreateTerminal(new TerminalOptions { Name = "Test" });

        terminal.Should().NotBeNull();
        terminal.Name.Should().Be("Test");
        _service.ActiveTerminal.Should().Be(terminal);
        _service.Terminals.Should().HaveCount(1);
    }

    [Fact]
    public void CreateTerminal_MultipleTerminals_SetsLastAsActive()
    {
        var t1 = _service.CreateTerminal(new TerminalOptions { Name = "T1" });
        var t2 = _service.CreateTerminal(new TerminalOptions { Name = "T2" });

        _service.Terminals.Should().HaveCount(2);
        _service.ActiveTerminal.Should().Be(t2);
    }

    [Fact]
    public void CloseTerminal_RemovesFromList()
    {
        var terminal = _service.CreateTerminal(new TerminalOptions { Name = "Test" });

        _service.CloseTerminal(terminal);

        _service.Terminals.Should().BeEmpty();
        _service.ActiveTerminal.Should().BeNull();
    }

    [Fact]
    public void CloseTerminal_WithMultiple_SetsActiveToPrevious()
    {
        var t1 = _service.CreateTerminal(new TerminalOptions { Name = "T1" });
        var t2 = _service.CreateTerminal(new TerminalOptions { Name = "T2" });

        _service.CloseTerminal(t2);

        _service.ActiveTerminal.Should().Be(t1);
    }

    [Fact]
    public void CloseAllTerminals_ClearsEverything()
    {
        _service.CreateTerminal(new TerminalOptions { Name = "T1" });
        _service.CreateTerminal(new TerminalOptions { Name = "T2" });

        _service.CloseAllTerminals();

        _service.Terminals.Should().BeEmpty();
        _service.ActiveTerminal.Should().BeNull();
    }

    [Fact]
    public void CreateTerminal_FiresTerminalCreatedEvent()
    {
        ITerminalInstance? created = null;
        _service.TerminalCreated += (_, t) => created = t;

        var terminal = _service.CreateTerminal(new TerminalOptions { Name = "Test" });

        created.Should().Be(terminal);
    }

    [Fact]
    public void CloseTerminal_FiresTerminalClosedEvent()
    {
        var terminal = _service.CreateTerminal(new TerminalOptions { Name = "Test" });

        ITerminalInstance? closed = null;
        _service.TerminalClosed += (_, t) => closed = t;

        _service.CloseTerminal(terminal);

        closed.Should().Be(terminal);
    }

    [Fact]
    public void CreateTerminal_DefaultOptions_StillWorks()
    {
        var terminal = _service.CreateTerminal();

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
