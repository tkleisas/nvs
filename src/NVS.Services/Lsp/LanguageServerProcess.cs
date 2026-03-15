using System.Diagnostics;
using NVS.Core.Models.Settings;

namespace NVS.Services.Lsp;

/// <summary>
/// Manages a language server subprocess. Redirects stdin/stdout for JSON-RPC
/// communication and stderr for logging.
/// </summary>
public sealed class LanguageServerProcess : IAsyncDisposable
{
    private Process? _process;
    private bool _disposed;

    public Stream? InputStream => _process?.StandardInput.BaseStream;
    public Stream? OutputStream => _process?.StandardOutput.BaseStream;
    /// <summary>
    /// The StreamReader wrapping stdout. Use this for reading instead of OutputStream
    /// to avoid buffering conflicts between StreamReader and BaseStream.
    /// </summary>
    public StreamReader? OutputReader => _process?.StandardOutput;
    public bool IsRunning => _process is { HasExited: false };
    public int? ProcessId => _process?.Id;

    public event EventHandler<string>? ErrorDataReceived;
    public event EventHandler<int>? Exited;

    /// <summary>
    /// Starts a language server process with the given configuration.
    /// </summary>
    public Task StartAsync(LanguageServerConfig config, string? workingDirectory = null, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_process is not null)
            throw new InvalidOperationException("Process is already running.");

        var startInfo = new ProcessStartInfo
        {
            FileName = config.Command,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? config.Cwd ?? string.Empty,
        };

        foreach (var arg in config.Args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        foreach (var (key, value) in config.Environment)
        {
            startInfo.Environment[key] = value;
        }

        _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                ErrorDataReceived?.Invoke(this, e.Data);
        };

        _process.Exited += (_, _) =>
        {
            Exited?.Invoke(this, _process.ExitCode);
        };

        if (!_process.Start())
            throw new InvalidOperationException($"Failed to start language server: {config.Command}");

        _process.BeginErrorReadLine();

        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops the language server process gracefully, or kills it after a timeout.
    /// </summary>
    public async Task StopAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        if (_process is null or { HasExited: true })
            return;

        var killTimeout = timeout ?? TimeSpan.FromSeconds(5);

        try
        {
            // Close stdin to signal the server to exit
            _process.StandardInput.Close();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(killTimeout);

            try
            {
                await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Timeout — force kill
                _process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;

        if (_process is not null)
        {
            await StopAsync().ConfigureAwait(false);
            _process.Dispose();
            _process = null;
        }
    }
}
