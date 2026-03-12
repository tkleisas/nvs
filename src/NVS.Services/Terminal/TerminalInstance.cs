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
        var args = GetShellArguments(shell);

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = shell,
                Arguments = args,
                WorkingDirectory = options.WorkingDirectory ?? Environment.CurrentDirectory,
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

        _process.Exited += OnProcessExited;

        _process.Start();
        _isConnected = true;

        // Character-based async reading captures prompts and partial output
        _ = ReadStreamAsync(_process.StandardOutput);
        _ = ReadStreamAsync(_process.StandardError);
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

    private async Task ReadStreamAsync(StreamReader reader)
    {
        var buffer = new char[1024];
        try
        {
            int charsRead;
            while ((charsRead = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var text = new string(buffer, 0, charsRead);
                DataReceived?.Invoke(this, new TerminalDataEventArgs { Data = text });
            }
        }
        catch (ObjectDisposedException) { }
        catch (InvalidOperationException) { }
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
            var pwshPath = Lsp.LanguageServerManager.FindBinaryOnPath("pwsh");
            if (pwshPath is not null) return pwshPath;

            var powershellPath = Lsp.LanguageServerManager.FindBinaryOnPath("powershell");
            if (powershellPath is not null) return powershellPath;

            return "cmd.exe";
        }

        return Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash";
    }

    private static string GetShellArguments(string shell)
    {
        var name = Path.GetFileNameWithoutExtension(shell).ToLowerInvariant();
        return name switch
        {
            "pwsh" or "powershell" => "-NoProfile -NoLogo",
            _ => "",
        };
    }

    public void Dispose()
    {
        _isConnected = false;
        _process.Exited -= OnProcessExited;

        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* already exited */ }
        }

        _process.Dispose();
    }
}
