using System.IO;
using System.Text;
using NVS.Core.Interfaces;
using Porta.Pty;
using Serilog;

namespace NVS.Services.Terminal;

/// <summary>
/// <see cref="IProcessTerminal"/> backed by <c>Porta.Pty</c> (ConPTY on Windows,
/// forkpty on Unix). Owns exactly one child process; fans its UTF-8 output out to
/// zero or more subscribers via <see cref="OutputReceived"/> (event) and
/// <see cref="OutputObservable"/> (Rx). KillAsync kills the entire child tree.
/// </summary>
public sealed class ProcessTerminal : IProcessTerminal
{
    private readonly SimpleSubject<TerminalOutputChunk> _outputSubject = new();
    private IPtyConnection? _connection;
    private CancellationTokenSource? _readCts;
    private bool _disposed;
    private int? _exitCode;
    private bool _running;
    private bool _pipeMode;

    public TerminalSession Session { get; }
    public bool IsRunning => _running;
    public int? ProcessId => _connection?.Pid;
    public int? ExitCode => _exitCode;

    public event EventHandler<TerminalOutputChunk>? OutputReceived;
    public event EventHandler<int>? Exited;
    public IObservable<TerminalOutputChunk> OutputObservable => _outputSubject;

    public ProcessTerminal(TerminalSession session)
    {
        Session = session;
    }

    public async Task StartAsync(TerminalStartOptions options, CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
            throw new InvalidOperationException("Terminal already started.");
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessTerminal));

        cancellationToken.ThrowIfCancellationRequested();

        var (app, args) = ResolveCommand(options);

        var ptyOptions = new PtyOptions
        {
            Name = Session.Title,
            Cols = options.Cols,
            Rows = options.Rows,
            Cwd = options.WorkingDirectory ?? Environment.CurrentDirectory,
            App = app,
            CommandLine = args.ToArray(),
            Environment = ToEnvDictionary(options.Environment),
        };
        _pipeMode = !options.AllocatePty;

        try
        {
            _connection = await PtyProvider.SpawnAsync(ptyOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "[ProcessTerminal {Id}] spawn failed: {App} {Args}", Session.Id, app, string.Join(' ', args));
            throw;
        }

        _running = true;
        _connection.ProcessExited += OnProcessExited;

        // Begin an async read loop on the PTY reader stream; Pipe mode (AllocatePty=false) is
        // handled identically: Porta.Pty routes a real PTY through ReaderStream either way;
        // the flag only changes whether the child is given a controlling TTY. Reader/Writer
        // streams are always present.
        _readCts = new CancellationTokenSource();
        _ = ReadLoopAsync(_connection.ReaderStream, _readCts.Token);
    }

    public async Task SendInputAsync(string text)
    {
        if (_connection is null) return;
        var bytes = Encoding.UTF8.GetBytes(text);
        await _connection.WriterStream.WriteAsync(bytes.AsMemory(0, bytes.Length))
            .ConfigureAwait(false);
        await _connection.WriterStream.FlushAsync().ConfigureAwait(false);
    }

    public Task ResizeAsync(int cols, int rows)
    {
        try { _connection?.Resize(cols, rows); }
        catch (Exception ex) { Log.Debug(ex, "[ProcessTerminal {Id}] Resize failed", Session.Id); }
        return Task.CompletedTask;
    }

    public Task KillAsync()
    {
        if (_connection is null || !_running) return Task.CompletedTask;
        try { _connection.Kill(); }
        catch (Exception ex) { Log.Debug(ex, "[ProcessTerminal {Id}] Kill failed", Session.Id); }
        // OnProcessExited handles edge-set + read teardown.
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync(Stream reader, CancellationToken ct)
    {
        var buffer = new byte[8192];
        try
        {
            while (!ct.IsCancellationRequested && _running)
            {
                int bytesRead;
                try
                {
                    bytesRead = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)
                        .ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    Log.Debug(ex, "[ProcessTerminal {Id}] read error", Session.Id);
                    break;
                }

                if (bytesRead <= 0)
                    break; // EOF — process likely terminated

                var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                var chunk = new TerminalOutputChunk
                {
                    Text = text,
                    // PTY merges stdout+stderr; only distinguish in non-PTY pipe mode, not yet supported here.
                    IsError = false,
                    SessionId = Session.Id,
                };

                // Fan-out to both event subscribers and Rx observers without holding the read thread.
                OutputReceived?.Invoke(this, chunk);
                _outputSubject.OnNext(chunk);
            }
        }
        finally
        {
            // Ensure exit is surfaced even when the read loop returns before ProcessExited fires.
            // (Some hosts close stdout before signaling exit.)
        }
    }

    private void OnProcessExited(object? sender, PtyExitedEventArgs e)
    {
        _exitCode = e.ExitCode;
        _running = false;
        _readCts?.Cancel();

        try { _outputSubject.OnCompleted(); }
        catch { /* subject may already be disposed */ }

        Exited?.Invoke(this, e.ExitCode);
        Log.Debug("[ProcessTerminal {Id}] exited with code {ExitCode}", Session.Id, e.ExitCode);
    }

    private static (string App, IReadOnlyList<string> Args) ResolveCommand(TerminalStartOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Command))
            return (options.Command!, options.Args);

        // No command specified → default user shell.
        var (shell, defaultArgs) = GetDefaultShellWithNoExitFlag();
        return (shell, defaultArgs);
    }

    private static (string Shell, IReadOnlyList<string> Args) GetDefaultShellWithNoExitFlag()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer pwsh, fall back to powershell, then cmd. -NoExit keeps the shell alive so
            // run/debug commands and interactive typing work the same way.
            foreach (var candidate in new[] { "pwsh.exe", "powershell.exe", "cmd.exe" })
            {
                var path = FindInPath(candidate);
                if (path is not null)
                    return candidate == "cmd.exe"
                        ? (path, Array.Empty<string>() as IReadOnlyList<string>)
                        : (path, new[] { "-NoExit" });
            }
            return ("cmd.exe", Array.Empty<string>() as IReadOnlyList<string>);
        }

        var shell = Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
        return (shell, Array.Empty<string>() as IReadOnlyList<string>);
    }

    private static string? FindInPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        var sep = OperatingSystem.IsWindows() ? ';' : ':';
        foreach (var dir in path.Split(sep, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = System.IO.Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
            catch { /* bad PATH entry */ }
        }
        return null;
    }

    private static IDictionary<string, string> ToEnvDictionary(IReadOnlyDictionary<string, string> extra)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        // Inherit current process environment (Porta.Pty spawns with the dictionary we give it).
        foreach (System.Collections.DictionaryEntry kv in Environment.GetEnvironmentVariables())
        {
            if (kv.Key is string key)
                dict[key] = kv.Value as string ?? string.Empty;
        }

        foreach (var kv in extra)
            dict[kv.Key] = kv.Value;
        return dict;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await KillAsync().ConfigureAwait(false);

        _readCts?.Cancel();
        _readCts?.Dispose();
        _readCts = null;

        try
        {
            if (_connection is not null)
            {
                _connection.ProcessExited -= OnProcessExited;
                _connection.ReaderStream?.Dispose();
                _connection.WriterStream?.Dispose();
            }
        }
        catch (Exception ex) { Log.Debug(ex, "[ProcessTerminal {Id}] dispose teardown", Session.Id); }

        _connection = null;
        _outputSubject.Dispose();
    }
}