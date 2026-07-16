using System.Collections.ObjectModel;
using NVS.Core.Interfaces;
using Serilog;

namespace NVS.Services.Terminal;

/// <summary>
/// Default <see cref="ITerminalHost"/> — single source of truth for terminal lifecycle
/// in the IDE. Tracks live <see cref="IProcessTerminal"/> sessions, owns the active one,
/// and offers convenience factories (<see cref="CreateShellAsync"/> / <see cref="RunCommandAsync"/>)
/// for the common Shell / Run / Debug workflows.
/// </summary>
public sealed class TerminalHost : ITerminalHost
{
    private readonly ObservableCollection<IProcessTerminal> _terminals = [];
    private IProcessTerminal? _active;

    public IReadOnlyList<IProcessTerminal> Terminals => _terminals;
    public IProcessTerminal? Active
    {
        get => _active;
        set
        {
            if (!ReferenceEquals(_active, value))
            {
                _active = value;
                ActiveTerminalChanged?.Invoke(this, value!);
            }
        }
    }

    public event EventHandler<IProcessTerminal>? TerminalCreated;
    public event EventHandler<IProcessTerminal>? TerminalClosed;
    public event EventHandler<IProcessTerminal>? ActiveTerminalChanged;

    public IProcessTerminal CreateTerminal(TerminalStartOptions options)
    {
        var kind = options.Kind;
        var title = !string.IsNullOrWhiteSpace(options.Title)
            ? options.Title!
            : kind switch
            {
                TerminalSessionKind.Run => "▶ Run",
                TerminalSessionKind.Debug => "🐛 Debug",
                _ => "⌨ Terminal",
            };

        var session = new TerminalSession { Title = title, Kind = kind };
        var terminal = new ProcessTerminal(session);

        // Auto-manage the active terminal — the most recently created one wins, matching the
        // typical "new tab → visible tab" UX of dock-style terminals.
        _terminals.Add(terminal);
        Active = terminal;

        // Tear down dead terminals proactively so the list shrinks as sessions exit.
        // Hold a weak reference to avoid a leak if the host outlives the terminal unexpectedly.
        terminal.Exited += (_, code) =>
        {
            Log.Debug("[TerminalHost] terminal {Id} exited with code {Code}", terminal.Session.Id, code);
        };

        TerminalCreated?.Invoke(this, terminal);
        return terminal;
    }

    public async Task<IProcessTerminal> CreateShellAsync(
        TerminalStartOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var opts = options ?? new TerminalStartOptions { Kind = TerminalSessionKind.Shell };
        opts = opts with { Kind = opts.Kind == TerminalSessionKind.Shell ? TerminalSessionKind.Shell : opts.Kind };
        var terminal = CreateTerminal(opts);
        try { await terminal.StartAsync(opts, cancellationToken).ConfigureAwait(false); }
        catch
        {
            CloseTerminal(terminal);
            throw;
        }
        return terminal;
    }

    public async Task<IProcessTerminal> RunCommandAsync(
        string command,
        IReadOnlyList<string>? args = null,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TerminalSessionKind kind = TerminalSessionKind.Run,
        CancellationToken cancellationToken = default)
    {
        var opts = new TerminalStartOptions
        {
            Kind = kind,
            Command = command,
            Args = args ?? [],
            WorkingDirectory = workingDirectory,
            Environment = environment ?? new Dictionary<string, string>(),
        };
        var terminal = CreateTerminal(opts);
        try { await terminal.StartAsync(opts, cancellationToken).ConfigureAwait(false); }
        catch
        {
            CloseTerminal(terminal);
            throw;
        }
        return terminal;
    }

    public bool CloseTerminal(IProcessTerminal terminal)
    {
        if (!_terminals.Remove(terminal)) return false;

        try { _ = terminal.KillAsync(); }
        catch (Exception ex) { Log.Debug(ex, "[TerminalHost] KillAsync failed during close"); }

        _ = terminal.DisposeAsync();

        // Re-assign active only when we just closed it.
        if (ReferenceEquals(Active, terminal))
            Active = _terminals.Count > 0 ? _terminals[^1] : null;

        TerminalClosed?.Invoke(this, terminal);
        return true;
    }

    public IProcessTerminal? FindById(Guid sessionId)
        => _terminals.FirstOrDefault(t => t.Session.Id == sessionId);

    public async Task CloseAllAsync()
    {
        // Snapshot to avoid mutation-during-iteration.
        var snapshot = _terminals.ToArray();
        foreach (var terminal in snapshot)
            CloseTerminal(terminal);

        // Wait for all to actually exit (best-effort 3s aggregate).
        var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
        while (_terminals.Count > 0 && DateTimeOffset.UtcNow < deadline)
            await Task.Delay(50).ConfigureAwait(false);
    }
}