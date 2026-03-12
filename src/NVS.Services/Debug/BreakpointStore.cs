using NVS.Core.Interfaces;

namespace NVS.Services.Debug;

/// <summary>
/// In-memory breakpoint store with per-file tracking.
/// Thread-safe for concurrent access from UI and debug service.
/// </summary>
public sealed class BreakpointStore : IBreakpointStore
{
    private readonly Dictionary<string, List<Breakpoint>> _breakpoints = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public event EventHandler<BreakpointChangedEventArgs>? BreakpointChanged;

    public IReadOnlyList<Breakpoint> GetBreakpoints(string filePath)
    {
        lock (_lock)
        {
            return _breakpoints.TryGetValue(filePath, out var list)
                ? [.. list]
                : [];
        }
    }

    public IReadOnlyList<Breakpoint> GetAllBreakpoints()
    {
        lock (_lock)
        {
            return _breakpoints.Values.SelectMany(x => x).ToList();
        }
    }

    public Breakpoint ToggleBreakpoint(string filePath, int line)
    {
        lock (_lock)
        {
            if (!_breakpoints.TryGetValue(filePath, out var list))
            {
                list = [];
                _breakpoints[filePath] = list;
            }

            var existing = list.FirstOrDefault(b => b.Line == line);
            if (existing is not null)
            {
                list.Remove(existing);
                if (list.Count == 0) _breakpoints.Remove(filePath);

                BreakpointChanged?.Invoke(this, new BreakpointChangedEventArgs
                {
                    FilePath = filePath,
                    Breakpoint = existing,
                    Kind = BreakpointChangeKind.Removed,
                });
                return existing;
            }

            var bp = new Breakpoint
            {
                Id = Guid.NewGuid(),
                Path = filePath,
                Line = line,
                IsEnabled = true,
                IsVerified = false,
            };

            list.Add(bp);

            BreakpointChanged?.Invoke(this, new BreakpointChangedEventArgs
            {
                FilePath = filePath,
                Breakpoint = bp,
                Kind = BreakpointChangeKind.Added,
            });

            return bp;
        }
    }

    public void RemoveBreakpoint(string filePath, int line)
    {
        lock (_lock)
        {
            if (!_breakpoints.TryGetValue(filePath, out var list)) return;

            var bp = list.FirstOrDefault(b => b.Line == line);
            if (bp is null) return;

            list.Remove(bp);
            if (list.Count == 0) _breakpoints.Remove(filePath);

            BreakpointChanged?.Invoke(this, new BreakpointChangedEventArgs
            {
                FilePath = filePath,
                Breakpoint = bp,
                Kind = BreakpointChangeKind.Removed,
            });
        }
    }

    public void ClearBreakpoints(string filePath)
    {
        lock (_lock)
        {
            if (!_breakpoints.TryGetValue(filePath, out var list)) return;

            var copy = list.ToList();
            _breakpoints.Remove(filePath);

            foreach (var bp in copy)
            {
                BreakpointChanged?.Invoke(this, new BreakpointChangedEventArgs
                {
                    FilePath = filePath,
                    Breakpoint = bp,
                    Kind = BreakpointChangeKind.Removed,
                });
            }
        }
    }

    public void ClearAllBreakpoints()
    {
        lock (_lock)
        {
            var allFiles = _breakpoints.Keys.ToList();
            foreach (var file in allFiles)
            {
                ClearBreakpoints(file);
            }
        }
    }

    public void UpdateVerifiedStatus(string filePath, int line, bool verified)
    {
        lock (_lock)
        {
            if (!_breakpoints.TryGetValue(filePath, out var list)) return;

            var index = list.FindIndex(b => b.Line == line);
            if (index < 0) return;

            var bp = list[index];
            var updated = bp with { IsVerified = verified };
            list[index] = updated;

            BreakpointChanged?.Invoke(this, new BreakpointChangedEventArgs
            {
                FilePath = filePath,
                Breakpoint = updated,
                Kind = BreakpointChangeKind.Updated,
            });
        }
    }
}
