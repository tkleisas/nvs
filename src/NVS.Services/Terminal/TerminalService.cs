using System.Collections.ObjectModel;
using NVS.Core.Interfaces;

namespace NVS.Services.Terminal;

public sealed class TerminalService : ITerminalService, IDisposable
{
    private readonly List<ITerminalInstance> _terminals = [];
    private ITerminalInstance? _activeTerminal;

    public IReadOnlyList<ITerminalInstance> Terminals => _terminals.AsReadOnly();

    public ITerminalInstance? ActiveTerminal
    {
        get => _activeTerminal;
        private set
        {
            if (_activeTerminal != value)
            {
                _activeTerminal = value;
                ActiveTerminalChanged?.Invoke(this, value!);
            }
        }
    }

    public event EventHandler<ITerminalInstance>? TerminalCreated;
    public event EventHandler<ITerminalInstance>? TerminalClosed;
    public event EventHandler<ITerminalInstance>? ActiveTerminalChanged;

    public ITerminalInstance CreateTerminal(TerminalOptions? options = null)
    {
        var instance = new TerminalInstance(options);
        instance.Exited += OnTerminalExited;
        _terminals.Add(instance);
        ActiveTerminal = instance;
        TerminalCreated?.Invoke(this, instance);
        return instance;
    }

    public void CloseTerminal(ITerminalInstance terminal)
    {
        if (!_terminals.Remove(terminal))
            return;

        if (terminal is TerminalInstance ti)
        {
            ti.Exited -= OnTerminalExited;
            ti.Kill();
            ti.Dispose();
        }

        TerminalClosed?.Invoke(this, terminal);

        if (ActiveTerminal == terminal)
        {
            ActiveTerminal = _terminals.Count > 0 ? _terminals[^1] : null;
        }
    }

    public void CloseAllTerminals()
    {
        foreach (var terminal in _terminals.ToList())
        {
            if (terminal is TerminalInstance ti)
            {
                ti.Exited -= OnTerminalExited;
                ti.Kill();
                ti.Dispose();
            }
        }

        _terminals.Clear();
        ActiveTerminal = null;
    }

    private void OnTerminalExited(object? sender, TerminalExitedEventArgs e)
    {
        if (sender is ITerminalInstance terminal)
        {
            CloseTerminal(terminal);
        }
    }

    public void Dispose()
    {
        CloseAllTerminals();
    }
}
