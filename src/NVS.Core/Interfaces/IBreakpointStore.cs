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

    /// <summary>
    /// Replaces the store contents with the breakpoints persisted for the given
    /// workspace (`.nvs/breakpoints.json`). Missing or corrupt files yield an
    /// empty store. Raises <see cref="BreakpointChanged"/> for loaded entries.
    /// </summary>
    void Load(string workspacePath);

    /// <summary>Persists the current breakpoints for the given workspace (`.nvs/breakpoints.json`).</summary>
    void Save(string workspacePath);

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
