using NVS.Core.Interfaces;

namespace NVS.Core.Interfaces;

/// <summary>
/// In-memory store of breakpoints per file.
/// Manages CRUD operations and persistence.
/// </summary>
public interface IBreakpointStore
{
    IReadOnlyList<Breakpoint> GetBreakpoints(string filePath);
    IReadOnlyList<Breakpoint> GetAllBreakpoints();
    Breakpoint ToggleBreakpoint(string filePath, int line);
    void RemoveBreakpoint(string filePath, int line);
    void ClearBreakpoints(string filePath);
    void ClearAllBreakpoints();
    void UpdateVerifiedStatus(string filePath, int line, bool verified);

    event EventHandler<BreakpointChangedEventArgs>? BreakpointChanged;
}

public sealed record BreakpointChangedEventArgs
{
    public required string FilePath { get; init; }
    public required Breakpoint Breakpoint { get; init; }
    public required BreakpointChangeKind Kind { get; init; }
}

public enum BreakpointChangeKind
{
    Added,
    Removed,
    Updated,
}
