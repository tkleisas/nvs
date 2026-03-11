using System.Diagnostics;
using NVS.Core.Interfaces;

namespace NVS.Services.Terminal;

public sealed class TerminalInstance : ITerminalInstance, IDisposable
{
    private readonly Process _process;
    private volatile bool _isConnected;

    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; }
    public string? WorkingDirectory { get; }
    public bool IsConnected => _isConnected;

    public event EventHandler<TerminalDataEventArgs>? DataReceived;
    public event EventHandler<TerminalExitedEventArgs>? Exited;

    public TerminalInstance(TerminalOptions? options = null)
    {
        options ??= new TerminalOptions();
        Name = options.Name ?? "Terminal";
        WorkingDirectory = options.WorkingDirectory;

        var shell = options.Shell ?? GetDefaultShell();

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                WorkingDirectory = options.WorkingDirectory ?? System.Environment.CurrentDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        if (options.Environment is not null)
        {
            foreach (var (key, value) in options.Environment)
            {
                _process.StartInfo.EnvironmentVariables[key] = value;
            }
        }

        _process.OutputDataReceived += OnOutputData;
        _process.ErrorDataReceived += OnErrorData;
        _process.Exited += OnProcessExited;

        _process.Start();
        _isConnected = true;
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void Write(string data)
    {
        if (!_isConnected) return;
        _process.StandardInput.Write(data);
        _process.StandardInput.Flush();
    }

    public void WriteLine(string data)
    {
        if (!_isConnected) return;
        _process.StandardInput.WriteLine(data);
        _process.StandardInput.Flush();
    }

    public void Resize(int columns, int rows)
    {
        // Process-based terminals don't support resize.
        // A full PTY implementation would handle this.
    }

    public async Task<int> WaitForExitAsync(CancellationToken cancellationToken = default)
    {
        await _process.WaitForExitAsync(cancellationToken);
        return _process.ExitCode;
    }

    public void Kill()
    {
        if (!_isConnected) return;

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process already exited
        }
    }

    private void OnOutputData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            DataReceived?.Invoke(this, new TerminalDataEventArgs { Data = e.Data });
        }
    }

    private void OnErrorData(object sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
        {
            DataReceived?.Invoke(this, new TerminalDataEventArgs { Data = e.Data });
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        _isConnected = false;
        Exited?.Invoke(this, new TerminalExitedEventArgs { ExitCode = _process.ExitCode });
    }

    internal static string GetDefaultShell()
    {
        if (OperatingSystem.IsWindows())
        {
            // Prefer PowerShell Core, fall back to Windows PowerShell, then cmd
            var pwshPath = Lsp.LanguageServerManager.FindBinaryOnPath("pwsh");
            if (pwshPath is not null) return pwshPath;

            var powershellPath = Lsp.LanguageServerManager.FindBinaryOnPath("powershell");
            if (powershellPath is not null) return powershellPath;

            return "cmd.exe";
        }

        return System.Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    public void Dispose()
    {
        _isConnected = false;
        _process.OutputDataReceived -= OnOutputData;
        _process.ErrorDataReceived -= OnErrorData;
        _process.Exited -= OnProcessExited;

        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        }

        _process.Dispose();
    }
}
