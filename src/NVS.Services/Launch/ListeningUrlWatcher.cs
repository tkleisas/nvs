using System.Text;

namespace NVS.Services.Launch;

/// <summary>
/// Stateful watcher that accumulates output chunks and fires
/// <see cref="UrlDetected"/> the first time a "Now listening on:" line appears.
/// Only fires once per instance; call <see cref="Reset"/> to reuse.
/// </summary>
public sealed class ListeningUrlWatcher
{
    private readonly StringBuilder _line = new();
    private bool _fired;

    public event Action<string>? UrlDetected;

    /// <summary>Feed a chunk of stdout/stderr. Returns true when a URL was detected in this chunk.</summary>
    public bool Append(string chunk)
    {
        if (_fired || string.IsNullOrEmpty(chunk)) return false;

        for (int i = 0; i < chunk.Length; i++)
        {
            var ch = chunk[i];
            if (ch == '\n' || ch == '\r')
            {
                // Treat \r\n as a single break: only scan when there's buffered content.
                if (_line.Length > 0 && TryScanLine())
                    return true;
            }
            else
            {
                _line.Append(ch);
            }
        }

        return _fired;
    }

    /// <summary>
    /// Scans any buffered (newline-less) tail. Call once when the process exits
    /// to catch a final "Now listening on:" line that lacks a trailing newline.
    /// </summary>
    public bool Flush()
    {
        if (_fired || _line.Length == 0) return false;
        return TryScanLine();
    }

    private bool TryScanLine()
    {
        if (_fired || _line.Length == 0) return false;

        var url = ListeningUrlParser.TryExtract(_line.ToString());
        _line.Clear();

        if (url is null) return false;

        _fired = true;
        UrlDetected?.Invoke(url);
        return true;
    }

    public void Reset()
    {
        _line.Clear();
        _fired = false;
    }
}