namespace NVS.Core.Interfaces;

public interface ITerminalService
{
    IReadOnlyList<ITerminalInstance> Terminals { get; }
    ITerminalInstance? ActiveTerminal { get; }
    
    ITerminalInstance CreateTerminal(TerminalOptions? options = null);
    void CloseTerminal(ITerminalInstance terminal);
    void CloseAllTerminals();
    
    event EventHandler<ITerminalInstance>? TerminalCreated;
    event EventHandler<ITerminalInstance>? TerminalClosed;
    event EventHandler<ITerminalInstance>? ActiveTerminalChanged;
}

public interface ITerminalInstance
{
    Guid Id { get; }
    string Name { get; }
    string? WorkingDirectory { get; }
    bool IsConnected { get; }
    
    void Write(string data);
    void WriteLine(string data);
    void Resize(int columns, int rows);
    Task<int> WaitForExitAsync(CancellationToken cancellationToken = default);
    void Kill();
    
    event EventHandler<TerminalDataEventArgs>? DataReceived;
    event EventHandler<TerminalExitedEventArgs>? Exited;
}

public sealed record TerminalOptions
{
    public string? Name { get; init; }
    public string? WorkingDirectory { get; init; }
    public string? Shell { get; init; }
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    public int Columns { get; init; } = 80;
    public int Rows { get; init; } = 24;
}

public sealed class TerminalDataEventArgs : EventArgs
{
    public required string Data { get; init; }
}

public sealed class TerminalExitedEventArgs : EventArgs
{
    public int ExitCode { get; init; }
}
